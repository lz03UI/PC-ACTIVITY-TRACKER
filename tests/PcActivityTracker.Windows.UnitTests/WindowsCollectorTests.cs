using PcActivityTracker.Core.Domain;
using PcActivityTracker.Core.Persistence;
using PcActivityTracker.Core.Tracking;
using PcActivityTracker.Windows;
using Xunit;

namespace PcActivityTracker.Windows.UnitTests;

public sealed class WindowsCollectorTests
{
    [Fact] public async Task HookIsRemovedAndWorkersCompleteAtStop() { var native = new FakeNative(); await using var collector = new WindowsTrackingCollector(native); await collector.StartAsync(); await collector.StopAsync(); Assert.Equal(1, native.UnhookCount); }
    [Fact] public async Task StartStopStartInstallsAndRemovesFreshHooks() { var native = new FakeNative(); await using var collector = new WindowsTrackingCollector(native); await collector.StartAsync(); await collector.StopAsync(); await collector.StartAsync(); await collector.StopAsync(); Assert.Equal(2, native.HookCount); Assert.Equal(2, native.UnhookCount); }
    [Fact] public async Task DisposeDuringActivityUnhooks() { var native = new FakeNative(); var collector = new WindowsTrackingCollector(native); await collector.StartAsync(); native.Raise(4); await collector.DisposeAsync(); Assert.Equal(1, native.UnhookCount); }
    [Fact] public void SessionMessagesMapLockUnlockDisconnectReconnect() { Assert.Equal(TrackingSignalKind.Locked, WindowsMessageMapper.Map(WindowsMessageMapper.WmWtsSessionChange, 0x7)); Assert.Equal(TrackingSignalKind.Unlocked, WindowsMessageMapper.Map(WindowsMessageMapper.WmWtsSessionChange, 0x8)); Assert.Equal(TrackingSignalKind.SessionDisconnected, WindowsMessageMapper.Map(WindowsMessageMapper.WmWtsSessionChange, 0x4)); Assert.Equal(TrackingSignalKind.SessionReconnected, WindowsMessageMapper.Map(WindowsMessageMapper.WmWtsSessionChange, 0x3)); }
    [Fact] public void PowerClockAndTimeZoneMessagesAreMapped() { Assert.Equal(TrackingSignalKind.Suspended, WindowsMessageMapper.Map(WindowsMessageMapper.WmPowerBroadcast, 0x4)); Assert.Equal(TrackingSignalKind.Resumed, WindowsMessageMapper.Map(WindowsMessageMapper.WmPowerBroadcast, 0x12)); Assert.Equal(TrackingSignalKind.ClockChanged, WindowsMessageMapper.Map(WindowsMessageMapper.WmTimeChange, 0)); Assert.Equal(TrackingSignalKind.TimeZoneChanged, WindowsMessageMapper.Map(WindowsMessageMapper.WmSettingChange, 0)); }
    [Fact]
    public async Task ProducerTimestampIsPreservedWhenConsumerIsSlow()
    {
        var clock = new MutableTimeProvider(); var native = new FakeNative(); await using var collector = new WindowsTrackingCollector(native, timeProvider: clock); await collector.StartAsync();
        clock.Advance(TimeSpan.FromSeconds(10)); native.Raise(7); clock.Advance(TimeSpan.FromSeconds(40));
        await using var reader = collector.ReadAllAsync().GetAsyncEnumerator(); Assert.True(await reader.MoveNextAsync());
        Assert.Equal(clock.Origin.AddSeconds(10), reader.Current.At.Value); Assert.Equal(10, reader.Current.Monotonic.Value);
        var machine = new TrackingStateMachine(new RuleExclusionEvaluator([]), () => new("UTC", TimeSpan.Zero));
        machine.Apply(new(TrackingSignalKind.Start, new(clock.Origin), new(0))); machine.Apply(new(TrackingSignalKind.Reconcile, new(clock.Origin), new(0), new(1, "initial")));
        var closed = Assert.IsType<IntervalClosed>(machine.Apply(reader.Current)[0]); Assert.Equal(clock.Origin.AddSeconds(10), closed.Interval.Period.End.Value);
    }
    [Fact]
    public async Task OverflowDoesNoResolutionInCallbackAndYieldsSignalLoss()
    {
        var native = new FakeNative(); await using var collector = new WindowsTrackingCollector(native, new() { ChannelCapacity = 1 }); await collector.StartAsync();
        native.Raise(1); native.Raise(2); Assert.Equal(0, native.ReadCount); Assert.Equal(1, collector.DroppedSignalCount);
        await using var reader = collector.ReadAllAsync().GetAsyncEnumerator(); Assert.True(await reader.MoveNextAsync()); Assert.Equal(TrackingSignalKind.ForegroundChanged, reader.Current.Kind);
        Assert.True(await reader.MoveNextAsync()); Assert.Equal(TrackingSignalKind.SignalLossDetected, reader.Current.Kind);
    }
    [Fact]
    public async Task ReconciliationBarrierRejectsOlderForegroundButKeepsNewerEvent()
    {
        var native = new FakeNative(); await using var collector = new WindowsTrackingCollector(native, new() { ChannelCapacity = 4 }); await collector.StartAsync();
        native.Raise(1); collector.RequestReconciliation(); native.Raise(2);
        await using var reader = collector.ReadAllAsync().GetAsyncEnumerator(); Assert.True(await reader.MoveNextAsync()); Assert.Equal(TrackingSignalKind.Reconcile, reader.Current.Kind); Assert.True(await reader.MoveNextAsync());
        Assert.Equal(2, reader.Current.Foreground?.ProcessId);
    }
    [Fact]
    public async Task LifecycleQueuedBeforeReconciliationPreservesSequenceOrder()
    {
        var native = new FakeNative(); await using var collector = new WindowsTrackingCollector(native, new() { ChannelCapacity = 4 }); await collector.StartAsync();
        Assert.True(collector.TryPublish(TrackingSignalKind.Locked)); collector.RequestReconciliation();
        await using var reader = collector.ReadAllAsync().GetAsyncEnumerator(); Assert.True(await reader.MoveNextAsync()); Assert.Equal(TrackingSignalKind.Locked, reader.Current.Kind);
        Assert.True(await reader.MoveNextAsync()); Assert.Equal(TrackingSignalKind.Reconcile, reader.Current.Kind); Assert.True(reader.Current.Sequence > 0);
    }
    [Fact]
    public async Task SlowPersistenceCannotCreateUnboundedPendingWork()
    {
        var native = new FakeNative(); await using var collector = new WindowsTrackingCollector(native, new() { ChannelCapacity = 2 }); await collector.StartAsync();
        var store = new BlockingStore(); var machine = new TrackingStateMachine(new RuleExclusionEvaluator([]), () => new("UTC", TimeSpan.Zero)); var coordinator = new TrackingCoordinator(machine, store, collector);
        var run = coordinator.RunAsync(); Assert.True(collector.TryPublish(TrackingSignalKind.Start)); await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        for (var i = 1; i <= 20; i++) native.Raise(i);
        Assert.True(collector.DroppedSignalCount >= 18); Assert.Equal(1, store.MaxConcurrency);
        store.Release.TrySetResult(); while (!collector.TryPublish(TrackingSignalKind.Stop)) await Task.Delay(10); await run.WaitAsync(TimeSpan.FromSeconds(2));
    }
    [Fact] public void NativeFacadeShapeContainsNoWindowTitle() => Assert.DoesNotContain(typeof(ForegroundSnapshot).GetProperties(), x => x.Name.Contains("Title", StringComparison.Ordinal));
    [Fact]
    public void LocalDatabaseMetricIncludesWalWithoutMetadata()
    {
        var path = Path.Combine(Path.GetTempPath(), $"metric-{Guid.NewGuid():N}.db");
        try { File.WriteAllBytes(path, new byte[10]); File.WriteAllBytes(path + "-wal", new byte[15]); var snapshot = LocalResourceSnapshot.Capture(path); Assert.Equal(25, snapshot.DatabaseBytes); Assert.Equal(3, typeof(LocalResourceSnapshot).GetProperties().Length); }
        finally { File.Delete(path); File.Delete(path + "-wal"); }
    }

    private sealed class BlockingStore : ITrackingBatchStore
    {
        private int concurrent;
        public int MaxConcurrency { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task PersistTrackingBatchAsync(TrackingPersistenceBatch batch, CancellationToken cancellationToken = default)
        {
            var value = Interlocked.Increment(ref concurrent); MaxConcurrency = Math.Max(MaxConcurrency, value); Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken); Interlocked.Decrement(ref concurrent);
        }
    }
    private sealed class MutableTimeProvider : TimeProvider
    {
        private long seconds; public DateTimeOffset Origin { get; } = new(2026, 8, 15, 0, 0, 0, TimeSpan.Zero);
        public void Advance(TimeSpan value) => seconds += (long)value.TotalSeconds;
        public override DateTimeOffset GetUtcNow() => Origin.AddSeconds(seconds);
        public override long GetTimestamp() => seconds;
        public override long TimestampFrequency => 1;
    }
    private sealed class FakeNative : IWindowsNativeFacade
    {
        private WinEventCallback? callback;
        public int HookCount { get; private set; }
        public int UnhookCount { get; private set; }
        public int ReadCount { get; private set; }
        public nint SetForegroundHook(WinEventCallback value) { callback = value; HookCount++; return HookCount; }
        public bool Unhook(nint hook) { UnhookCount++; callback = null; return true; }
        public nint GetForegroundWindow() => 42;
        public ForegroundSnapshot? ReadForeground(nint window) { ReadCount++; return new((int)window, $"app-{window}"); }
        public TimeSpan GetIdleDuration() => TimeSpan.Zero;
        public void Raise(int window) => callback?.Invoke(1, 3, window, 0, 0, 0, 0);
    }
}
