using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using PcActivityTracker.Core.Domain;
using PcActivityTracker.Core.Tracking;

namespace PcActivityTracker.Windows;

public sealed record WindowsCollectorOptions
{
    public TimeSpan IdleThreshold { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan IdleCheckInterval { get; init; } = TimeSpan.FromSeconds(5);
    public int ChannelCapacity { get; init; } = 128;
}

public interface IWindowsNativeFacade
{
    nint SetForegroundHook(WinEventCallback callback);
    bool Unhook(nint hook);
    nint GetForegroundWindow();
    ForegroundSnapshot? ReadForeground(nint window);
    TimeSpan GetIdleDuration();
}
[UnmanagedFunctionPointer(CallingConvention.Winapi)]
public delegate void WinEventCallback(nint hook, uint eventType, nint window, int objectId, int childId, uint threadId, uint eventTime);

public sealed class WindowsTrackingCollector : ITrackingSignalSource
{
    private sealed record RawSignal(TrackingSignalKind Kind, nint Window, UtcInstant At, MonotonicTimestamp Monotonic, long Sequence, long Generation, RuntimeConditions? Conditions = null);
    private readonly IWindowsNativeFacade native;
    private readonly WindowsCollectorOptions options;
    private readonly TimeProvider time;
    private readonly RuntimeMetrics metrics;
    private readonly Channel<RawSignal> queue;
    private readonly Channel<RawSignal> commandQueue;
    private readonly Channel<bool> wake;
    private readonly WinEventCallback callback;
    private readonly SemaphoreSlim producerGate = new(1, 1);
    private CancellationTokenSource? lifetime;
    private Task? idleWorker;
    private nint hook;
    private bool idle;
    private long dropped, generation, sequence;
    private RawSignal? signalLoss;
    private RawSignal? reconciliation;
    private RawSignal? controlOverflow;
    private RawSignal? conditionsOverflow;
    private int desiredIdle, desiredLocked, desiredDisconnected, desiredSuspended;
    private volatile bool accepting;

    public WindowsTrackingCollector(IWindowsNativeFacade native, WindowsCollectorOptions? options = null,
        TimeProvider? timeProvider = null, RuntimeMetrics? runtimeMetrics = null)
    {
        this.native = native; this.options = options ?? new(); time = timeProvider ?? TimeProvider.System; metrics = runtimeMetrics ?? new();
        if (this.options.ChannelCapacity < 1) throw new ArgumentOutOfRangeException(nameof(options));
        queue = Channel.CreateBounded<RawSignal>(new BoundedChannelOptions(this.options.ChannelCapacity)
        { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
        commandQueue = Channel.CreateBounded<RawSignal>(new BoundedChannelOptions(32)
        { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
        wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.DropOldest });
        callback = OnForeground;
    }

    public long DroppedSignalCount => Interlocked.Read(ref dropped);
    public bool IsDegraded => DroppedSignalCount > 0;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (lifetime is not null) return Task.CompletedTask;
        while (queue.Reader.TryRead(out _)) { }
        while (commandQueue.Reader.TryRead(out _)) { }
        while (wake.Reader.TryRead(out _)) { }
        Interlocked.Exchange(ref signalLoss, null);
        Interlocked.Exchange(ref reconciliation, null);
        Interlocked.Exchange(ref controlOverflow, null);
        Interlocked.Exchange(ref conditionsOverflow, null);
        idle = false;
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        hook = native.SetForegroundHook(callback);
        if (hook == 0) { lifetime.Dispose(); lifetime = null; throw new Win32Exception(Marshal.GetLastPInvokeError(), "SetWinEventHook non riuscito."); }
        accepting = true;
        idleWorker = MonitorIdleAsync(lifetime.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var source = Interlocked.Exchange(ref lifetime, null); if (source is null) return;
        accepting = false;
        var currentHook = Interlocked.Exchange(ref hook, 0);
        if (currentHook != 0) _ = native.Unhook(currentHook);
        source.Cancel();
        if (idleWorker is { } worker)
            try { await worker.WaitAsync(cancellationToken); } catch (OperationCanceledException) when (source.IsCancellationRequested) { }
        idleWorker = null; source.Dispose();
    }

    public async ValueTask PublishAsync(TrackingSignalKind kind, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            await producerGate.WaitAsync(cancellationToken);
            try { if (commandQueue.Writer.TryWrite(CreateRaw(kind))) { Wake(); return; } }
            finally { producerGate.Release(); }
            if (!await commandQueue.Writer.WaitToWriteAsync(cancellationToken)) throw new ChannelClosedException();
        }
    }

    public bool TryPublishOsSignal(TrackingSignalKind kind)
    {
        UpdateDesiredConditions(kind);
        if (!accepting) return false;
        if (!producerGate.Wait(0)) { DeferOsSignal(kind); return false; }
        try
        {
            var item = CreateRaw(kind);
            if (queue.Writer.TryWrite(item)) { Wake(); return true; }
            DeferOsSignal(kind, item); return false;
        }
        finally { producerGate.Release(); }
    }

    public void RequestReconciliation()
    {
        RawSignal barrier;
        producerGate.Wait();
        try
        {
            Interlocked.Increment(ref generation);
            barrier = CreateRaw(TrackingSignalKind.Reconcile, native.GetForegroundWindow()) with { Conditions = CurrentConditions() };
        }
        finally { producerGate.Release(); }
        Interlocked.Exchange(ref reconciliation, barrier);
        Wake();
    }

    public async IAsyncEnumerable<TrackingSignal> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        RawSignal? pending = null;
        RawSignal? pendingCommand = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            var lost = Interlocked.CompareExchange(ref signalLoss, null, null);
            var reconcile = Interlocked.CompareExchange(ref reconciliation, null, null);
            var control = Interlocked.CompareExchange(ref controlOverflow, null, null);
            var conditions = Interlocked.CompareExchange(ref conditionsOverflow, null, null);
            if (pending is null) _ = queue.Reader.TryRead(out pending);
            if (pendingCommand is null) _ = commandQueue.Reader.TryRead(out pendingCommand);
            if (pending is null && pendingCommand is null)
            {
                var deferred = Earlier(Earlier(Earlier(lost, reconcile), control), conditions);
                if (deferred is not null && TakeDeferred(deferred))
                { yield return Resolve(deferred); continue; }
                await wake.Reader.ReadAsync(cancellationToken);
                continue;
            }
            lost = Interlocked.CompareExchange(ref signalLoss, null, null);
            reconcile = Interlocked.CompareExchange(ref reconciliation, null, null);
            control = Interlocked.CompareExchange(ref controlOverflow, null, null);
            conditions = Interlocked.CompareExchange(ref conditionsOverflow, null, null);
            var nextQueued = Earlier(pending, pendingCommand)!;
            var beforePending = Earlier(Earlier(Earlier(lost, reconcile), control), conditions);
            if (beforePending is not null && beforePending.Sequence < nextQueued.Sequence && TakeDeferred(beforePending))
            { yield return Resolve(beforePending); continue; }

            var item = nextQueued;
            if (ReferenceEquals(item, pending)) pending = null; else pendingCommand = null;
            if (item.Kind == TrackingSignalKind.ForegroundChanged && item.Generation != Interlocked.Read(ref generation)) continue;
            var snapshot = item.Kind == TrackingSignalKind.ForegroundChanged ? native.ReadForeground(item.Window) : null;
            if (item.Kind == TrackingSignalKind.ForegroundChanged && item.Generation != Interlocked.Read(ref generation)) continue;
            yield return ToTrackingSignal(item, snapshot);
        }
    }

    private static RawSignal? Earlier(RawSignal? left, RawSignal? right) => left is null ? right : right is null || left.Sequence < right.Sequence ? left : right;
    private bool TakeDeferred(RawSignal item) => item.Kind == TrackingSignalKind.Reconcile
        ? Interlocked.CompareExchange(ref reconciliation, null, item) == item
        : item.Kind == TrackingSignalKind.SignalLossDetected
            ? Interlocked.CompareExchange(ref signalLoss, null, item) == item
            : item.Kind == TrackingSignalKind.ConditionsChanged
                ? Interlocked.CompareExchange(ref conditionsOverflow, null, item) == item
                : Interlocked.CompareExchange(ref controlOverflow, null, item) == item;
    private TrackingSignal Resolve(RawSignal item) => ToTrackingSignal(item,
        item.Kind == TrackingSignalKind.Reconcile ? native.ReadForeground(item.Window) : null);

    private void OnForeground(nint hookIgnored, uint eventIgnored, nint window, int objectIgnored, int childIgnored, uint threadIgnored, uint timeIgnored)
    {
        if (!accepting || window == 0) return;
        if (!producerGate.Wait(0)) { RegisterSignalLoss(); return; }
        try
        {
            var item = CreateRaw(TrackingSignalKind.ForegroundChanged, window);
            if (queue.Writer.TryWrite(item)) { Wake(); return; }
            RegisterSignalLoss(item);
        }
        finally { producerGate.Release(); }
    }

    private void RegisterSignalLoss(RawSignal? source = null)
    {
        Interlocked.Increment(ref dropped); metrics.SignalDropped();
        var loss = source is null ? CreateRaw(TrackingSignalKind.SignalLossDetected) : source with { Kind = TrackingSignalKind.SignalLossDetected, Window = 0 };
        _ = Interlocked.CompareExchange(ref signalLoss, loss, null);
        Wake();
    }

    private void DeferOsSignal(TrackingSignalKind kind, RawSignal? original = null)
    {
        var item = original ?? CreateRaw(kind);
        if (IsCondition(kind))
            Interlocked.Exchange(ref conditionsOverflow, item with { Kind = TrackingSignalKind.ConditionsChanged, Conditions = CurrentConditions() });
        else
            Interlocked.Exchange(ref controlOverflow, item);
        Wake();
    }
    private RawSignal CreateRaw(TrackingSignalKind kind, nint window = 0) => new(kind, window, new(time.GetUtcNow()),
        new(time.GetTimestamp()), Interlocked.Increment(ref sequence), Interlocked.Read(ref generation));
    private TrackingSignal ToTrackingSignal(RawSignal item, ForegroundSnapshot? snapshot) =>
        new(item.Kind, item.At, item.Monotonic, snapshot, item.Sequence, time.TimestampFrequency, item.Conditions);

    private void UpdateDesiredConditions(TrackingSignalKind kind)
    {
        switch (kind)
        {
            case TrackingSignalKind.IdleEntered: Interlocked.Exchange(ref desiredIdle, 1); break;
            case TrackingSignalKind.IdleExited: Interlocked.Exchange(ref desiredIdle, 0); break;
            case TrackingSignalKind.Locked: Interlocked.Exchange(ref desiredLocked, 1); break;
            case TrackingSignalKind.Unlocked: Interlocked.Exchange(ref desiredLocked, 0); break;
            case TrackingSignalKind.SessionDisconnected: Interlocked.Exchange(ref desiredDisconnected, 1); break;
            case TrackingSignalKind.SessionReconnected: Interlocked.Exchange(ref desiredDisconnected, 0); break;
            case TrackingSignalKind.Suspended: Interlocked.Exchange(ref desiredSuspended, 1); break;
            case TrackingSignalKind.Resumed: Interlocked.Exchange(ref desiredSuspended, 0); break;
        }
    }
    private RuntimeConditions CurrentConditions() => new(desiredIdle != 0, desiredLocked != 0, desiredDisconnected != 0, desiredSuspended != 0);
    private static bool IsCondition(TrackingSignalKind kind) => kind is TrackingSignalKind.IdleEntered or TrackingSignalKind.IdleExited or
        TrackingSignalKind.Locked or TrackingSignalKind.Unlocked or TrackingSignalKind.SessionDisconnected or TrackingSignalKind.SessionReconnected or
        TrackingSignalKind.Suspended or TrackingSignalKind.Resumed;
    private void Wake() => _ = wake.Writer.TryWrite(true);

    private async Task MonitorIdleAsync(CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(options.IdleCheckInterval, time);
            while (await timer.WaitForNextTickAsync(token))
            {
                var current = native.GetIdleDuration() >= options.IdleThreshold;
                if (current != idle) { idle = current; _ = TryPublishOsSignal(current ? TrackingSignalKind.IdleEntered : TrackingSignalKind.IdleExited); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync() { await StopAsync(); GC.SuppressFinalize(this); }
}

public static class WindowsMessageMapper
{
    public const uint WmWtsSessionChange = 0x02B1, WmPowerBroadcast = 0x0218, WmQueryEndSession = 0x0011,
        WmEndSession = 0x0016, WmTimeChange = 0x001E, WmSettingChange = 0x001A;
    public static TrackingSignalKind? Map(uint message, nuint parameter) => (message, parameter) switch
    {
        (WmWtsSessionChange, 0x7) => TrackingSignalKind.Locked,
        (WmWtsSessionChange, 0x8) => TrackingSignalKind.Unlocked,
        (WmWtsSessionChange, 0x2 or 0x4) => TrackingSignalKind.SessionDisconnected,
        (WmWtsSessionChange, 0x1 or 0x3) => TrackingSignalKind.SessionReconnected,
        (WmPowerBroadcast, 0x4) => TrackingSignalKind.Suspended,
        (WmPowerBroadcast, 0x12 or 0x7) => TrackingSignalKind.Resumed,
        (WmEndSession, 1) => TrackingSignalKind.Stop,
        (WmTimeChange, _) => TrackingSignalKind.ClockChanged,
        _ => null
    };
}

public readonly record struct LocalTimeZoneSnapshot(string Id, TimeSpan Offset);
public sealed class TimeZoneChangeDetector(Func<LocalTimeZoneSnapshot> read)
{
    private LocalTimeZoneSnapshot last = read();
    public bool HasChanged()
    {
        var current = read();
        if (current == last) return false;
        last = current; return true;
    }
}

public sealed partial class WindowsNativeFacade : IWindowsNativeFacade
{
    private const uint EventSystemForeground = 0x0003, WineventOutofcontext = 0, WineventSkipownprocess = 2, ProcessQueryLimitedInformation = 0x1000;
    public nint SetForegroundHook(WinEventCallback callback) => SetWinEventHook(EventSystemForeground, EventSystemForeground, 0, callback, 0, 0, WineventOutofcontext | WineventSkipownprocess);
    public bool Unhook(nint hook) => UnhookWinEvent(hook);
    public nint GetForegroundWindow() => NativeGetForegroundWindow();
    public ForegroundSnapshot? ReadForeground(nint window)
    {
        if (window == 0) return null;
        _ = GetWindowThreadProcessId(window, out var pid); if (pid == 0) return null;
        string name;
        try { name = Process.GetProcessById((int)pid).ProcessName; } catch (ArgumentException) { name = $"pid-{pid}"; } catch (InvalidOperationException) { name = $"pid-{pid}"; } catch (Win32Exception) { name = $"pid-{pid}"; }
        string? path = null;
        var process = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (process != 0)
        {
            try { uint length = 32768; var buffer = new StringBuilder((int)length); if (QueryFullProcessImageName(process, 0, buffer, ref length)) path = buffer.ToString(); }
            finally { CloseHandle(process); }
        }
        return new((int)pid, name, path);
    }
    public TimeSpan GetIdleDuration()
    {
        var input = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref input)) return TimeSpan.Zero;
        var current = unchecked((uint)Environment.TickCount64);
        return TimeSpan.FromMilliseconds(unchecked(current - input.Tick));
    }
    [StructLayout(LayoutKind.Sequential)] private struct LastInputInfo { public uint Size; public uint Tick; }
    [LibraryImport("user32.dll", SetLastError = true)] private static partial nint SetWinEventHook(uint eventMin, uint eventMax, nint module, WinEventCallback callback, uint processId, uint threadId, uint flags);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool UnhookWinEvent(nint hook);
    [LibraryImport("user32.dll", EntryPoint = "GetForegroundWindow")] private static partial nint NativeGetForegroundWindow();
    [LibraryImport("user32.dll")] private static partial uint GetWindowThreadProcessId(nint window, out uint processId);
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder name, ref uint size);
    [LibraryImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool CloseHandle(nint handle);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetLastInputInfo(ref LastInputInfo info);
}

public sealed partial class SessionNotificationRegistration : IDisposable
{
    private readonly nint window;
    public SessionNotificationRegistration(nint window) { this.window = window; if (!WTSRegisterSessionNotification(window, 0)) throw new Win32Exception(Marshal.GetLastPInvokeError()); }
    public void Dispose() { _ = WTSUnRegisterSessionNotification(window); GC.SuppressFinalize(this); }
    [LibraryImport("wtsapi32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static partial bool WTSRegisterSessionNotification(nint window, uint flags);
    [LibraryImport("wtsapi32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool WTSUnRegisterSessionNotification(nint window);
}

public sealed partial class WindowsLifecycleRegistration : IDisposable
{
    private readonly nint window;
    private readonly Func<TrackingSignalKind, bool> publish;
    private readonly SubclassProcedure procedure;
    private readonly SessionNotificationRegistration session;
    private readonly TimeZoneChangeDetector timeZone;
    public WindowsLifecycleRegistration(nint window, Func<TrackingSignalKind, bool> publish, Func<LocalTimeZoneSnapshot>? readTimeZone = null)
    {
        this.window = window; this.publish = publish; procedure = WindowProcedure;
        timeZone = new(readTimeZone ?? ReadTimeZone);
        if (!SetWindowSubclass(window, procedure, 1, 0)) throw new Win32Exception(Marshal.GetLastPInvokeError());
        try { session = new(window); } catch { _ = RemoveWindowSubclass(window, procedure, 1); throw; }
    }
    private nint WindowProcedure(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        var mapped = WindowsMessageMapper.Map(message, wParam);
        if (mapped is { } signal) _ = publish(signal);
        if (message == WindowsMessageMapper.WmSettingChange && timeZone.HasChanged()) _ = publish(TrackingSignalKind.TimeZoneChanged);
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }
    private static LocalTimeZoneSnapshot ReadTimeZone()
    {
        var zone = TimeZoneInfo.Local; return new(zone.Id, zone.GetUtcOffset(DateTimeOffset.UtcNow));
    }
    public void Dispose() { session.Dispose(); _ = RemoveWindowSubclass(window, procedure, 1); GC.SuppressFinalize(this); }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint SubclassProcedure(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [LibraryImport("comctl32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetWindowSubclass(nint window, SubclassProcedure callback, nuint id, nuint data);
    [LibraryImport("comctl32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool RemoveWindowSubclass(nint window, SubclassProcedure callback, nuint id);
    [LibraryImport("comctl32.dll")] private static partial nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
}
