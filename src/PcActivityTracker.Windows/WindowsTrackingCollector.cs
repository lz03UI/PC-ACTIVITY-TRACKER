using System.ComponentModel;
using System.Diagnostics;
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

public sealed class WindowsTrackingCollector : ITrackingSignalSource, IReconciliationAwareForegroundSnapshotProvider
{
    private readonly IWindowsNativeFacade native;
    private readonly WindowsCollectorOptions options;
    private readonly TimeProvider time;
    private readonly Channel<(nint Window, long Generation)> queue;
    private readonly RuntimeMetrics metrics;
    private readonly WinEventCallback callback;
    private CancellationTokenSource? lifetime;
    private nint hook;
    private bool idle;
    private long dropped;
    private long generation;

    public WindowsTrackingCollector(IWindowsNativeFacade native, WindowsCollectorOptions? options = null, TimeProvider? timeProvider = null, RuntimeMetrics? runtimeMetrics = null)
    {
        this.native = native; this.options = options ?? new(); time = timeProvider ?? TimeProvider.System; metrics = runtimeMetrics ?? new();
        queue = Channel.CreateBounded<(nint, long)>(new BoundedChannelOptions(this.options.ChannelCapacity) { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
        callback = OnForeground;
    }
    public event EventHandler<TrackingSignal>? Signal;
    public long DroppedSignalCount => Interlocked.Read(ref dropped);
    public bool IsDegraded => DroppedSignalCount > 0;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (lifetime is not null) return Task.CompletedTask;
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        hook = native.SetForegroundHook(callback);
        if (hook == 0) { lifetime.Dispose(); lifetime = null; throw new Win32Exception(Marshal.GetLastPInvokeError(), "SetWinEventHook non riuscito."); }
        _ = ConsumeAsync(lifetime.Token); _ = MonitorIdleAsync(lifetime.Token);
        return Task.CompletedTask;
    }
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var source = Interlocked.Exchange(ref lifetime, null); if (source is null) return;
        source.Cancel(); if (hook != 0) native.Unhook(hook); hook = 0;
        await Task.CompletedTask; source.Dispose();
    }
    public ValueTask<ForegroundSnapshot?> GetCurrentAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(native.ReadForeground(native.GetForegroundWindow()));
    public void DiscardPendingSignals()
    {
        Interlocked.Increment(ref generation);
        while (queue.Reader.TryRead(out _)) { }
    }
    private void OnForeground(nint _, uint __, nint window, int ___, int ____, uint _____, uint ______)
    {
        if (window == 0) return;
        if (!queue.Writer.TryWrite((window, Interlocked.Read(ref generation)))) { Interlocked.Increment(ref dropped); metrics.SignalDropped(); Emit(TrackingSignalKind.CollectorRestarted); }
    }
    private async Task ConsumeAsync(CancellationToken token)
    {
        try
        {
            await foreach (var item in queue.Reader.ReadAllAsync(token))
            {
                if (item.Generation != Interlocked.Read(ref generation)) continue;
                var snapshot = native.ReadForeground(item.Window);
                if (item.Generation == Interlocked.Read(ref generation)) Emit(TrackingSignalKind.ForegroundChanged, snapshot);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    private async Task MonitorIdleAsync(CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(options.IdleCheckInterval, time);
            while (await timer.WaitForNextTickAsync(token))
            {
                var current = native.GetIdleDuration() >= options.IdleThreshold;
                if (current != idle) { idle = current; Emit(current ? TrackingSignalKind.IdleEntered : TrackingSignalKind.IdleExited); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    public void EmitLifecycle(TrackingSignalKind kind) => Emit(kind);
    private void Emit(TrackingSignalKind kind, ForegroundSnapshot? snapshot = null) => Signal?.Invoke(this, new(kind, new(time.GetUtcNow()), new(time.GetTimestamp()), snapshot));
    public async ValueTask DisposeAsync() { await StopAsync(); GC.SuppressFinalize(this); }
}

public static class WindowsMessageMapper
{
    public const uint WmWtsSessionChange = 0x02B1, WmPowerBroadcast = 0x0218, WmQueryEndSession = 0x0011, WmEndSession = 0x0016;
    public static TrackingSignalKind? Map(uint message, nuint parameter) => (message, parameter) switch
    {
        (WmWtsSessionChange, 0x7) => TrackingSignalKind.Locked,
        (WmWtsSessionChange, 0x8) => TrackingSignalKind.Unlocked,
        (WmPowerBroadcast, 0x4) => TrackingSignalKind.Suspended,
        (WmPowerBroadcast, 0x12 or 0x7) => TrackingSignalKind.Resumed,
        (WmEndSession, 1) => TrackingSignalKind.Stop,
        _ => null
    };
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
        var titleLength = GetWindowTextLength(window); string? title = null;
        if (titleLength > 0) { var buffer = new StringBuilder(Math.Min(titleLength + 1, 2048)); if (GetWindowText(window, buffer, buffer.Capacity) > 0) title = buffer.ToString(); }
        return new((int)pid, name, path, title);
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
    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")] private static partial int GetWindowTextLength(nint window);
    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int GetWindowText(nint window, StringBuilder text, int maximum);
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
    private readonly Action<TrackingSignalKind> publish;
    private readonly SubclassProcedure procedure;
    private readonly SessionNotificationRegistration session;
    public WindowsLifecycleRegistration(nint window, Action<TrackingSignalKind> publish)
    {
        this.window = window; this.publish = publish; procedure = WindowProcedure;
        if (!SetWindowSubclass(window, procedure, 1, 0)) throw new Win32Exception(Marshal.GetLastPInvokeError());
        try { session = new(window); } catch { _ = RemoveWindowSubclass(window, procedure, 1); throw; }
    }
    private nint WindowProcedure(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        var mapped = WindowsMessageMapper.Map(message, wParam);
        if (mapped is { } signal) publish(signal);
        if (message == WindowsMessageMapper.WmQueryEndSession) publish(TrackingSignalKind.Stop);
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }
    public void Dispose() { session.Dispose(); _ = RemoveWindowSubclass(window, procedure, 1); GC.SuppressFinalize(this); }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint SubclassProcedure(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [LibraryImport("comctl32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetWindowSubclass(nint window, SubclassProcedure callback, nuint id, nuint data);
    [LibraryImport("comctl32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool RemoveWindowSubclass(nint window, SubclassProcedure callback, nuint id);
    [LibraryImport("comctl32.dll")] private static partial nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
}
