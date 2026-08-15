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
    [Fact] public void PowerClockAndShutdownMessagesAreMappedPrecisely() { Assert.Equal(TrackingSignalKind.Suspended, WindowsMessageMapper.Map(WindowsMessageMapper.WmPowerBroadcast, 0x4)); Assert.Equal(TrackingSignalKind.Resumed, WindowsMessageMapper.Map(WindowsMessageMapper.WmPowerBroadcast, 0x12)); Assert.Equal(TrackingSignalKind.ClockChanged, WindowsMessageMapper.Map(WindowsMessageMapper.WmTimeChange, 0)); Assert.Null(WindowsMessageMapper.Map(WindowsMessageMapper.WmSettingChange, 0)); Assert.Null(WindowsMessageMapper.Map(WindowsMessageMapper.WmQueryEndSession, 1)); Assert.Null(WindowsMessageMapper.Map(WindowsMessageMapper.WmEndSession, 0)); Assert.Equal(TrackingSignalKind.Stop, WindowsMessageMapper.Map(WindowsMessageMapper.WmEndSession, 1)); }
    [Fact] public void CancelledEndSessionKeepsTrackingAndConfirmedEndStops() { var machine = new TrackingStateMachine(new RuleExclusionEvaluator([]), () => new("UTC", TimeSpan.Zero)); machine.Apply(TestSignal(TrackingSignalKind.Start, 0)); Assert.Null(WindowsMessageMapper.Map(WindowsMessageMapper.WmQueryEndSession, 1)); Assert.Null(WindowsMessageMapper.Map(WindowsMessageMapper.WmEndSession, 0)); Assert.Equal(TrackingStatus.Running, machine.Status); machine.Apply(TestSignal(WindowsMessageMapper.Map(WindowsMessageMapper.WmEndSession, 1)!.Value, 1)); Assert.Equal(TrackingStatus.Stopped, machine.Status); }
    [Fact] public void TimeZoneDetectorIgnoresGenericSettingAndDetectsEffectiveChange() { var current = new LocalTimeZoneSnapshot("UTC", TimeSpan.Zero); var detector = new TimeZoneChangeDetector(() => current); Assert.False(detector.HasChanged()); current = new("Europe/Rome", TimeSpan.FromHours(2)); Assert.True(detector.HasChanged()); Assert.False(detector.HasChanged()); }
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
        Assert.True(collector.TryPublishOsSignal(TrackingSignalKind.Locked)); collector.RequestReconciliation();
        await using var reader = collector.ReadAllAsync().GetAsyncEnumerator(); Assert.True(await reader.MoveNextAsync()); Assert.Equal(TrackingSignalKind.Locked, reader.Current.Kind);
        Assert.True(await reader.MoveNextAsync()); Assert.Equal(TrackingSignalKind.Reconcile, reader.Current.Kind); Assert.True(reader.Current.Sequence > 0);
    }
    [Fact]
    public async Task SlowPersistenceCannotCreateUnboundedPendingWork()
    {
        var native = new FakeNative(); await using var collector = new WindowsTrackingCollector(native, new() { ChannelCapacity = 2 }); await collector.StartAsync();
        var store = new BlockingStore(); var machine = new TrackingStateMachine(new RuleExclusionEvaluator([]), () => new("UTC", TimeSpan.Zero)); var coordinator = new TrackingCoordinator(machine, store, collector);
        var run = coordinator.RunAsync(); await collector.PublishAsync(TrackingSignalKind.Start); await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        for (var i = 1; i <= 20; i++) native.Raise(i);
        Assert.True(collector.DroppedSignalCount >= 18); Assert.Equal(1, store.MaxConcurrency);
        store.Release.TrySetResult(); await collector.PublishAsync(TrackingSignalKind.Stop); await run.WaitAsync(TimeSpan.FromSeconds(2));
    }
    [Fact]
    public async Task PreStartOsSignalsAreIgnoredWithoutTerminatingConsumer()
    {
        var native = new FakeNative(); await using var collector = new WindowsTrackingCollector(native, new() { ChannelCapacity = 8 }); await collector.StartAsync();
        var store = new RecordingStore(); var machine = new TrackingStateMachine(new RuleExclusionEvaluator([]), () => new("UTC", TimeSpan.Zero)); var coordinator = new TrackingCoordinator(machine, store, collector); var run = coordinator.RunAsync();
        native.Raise(9); collector.TryPublishOsSignal(TrackingSignalKind.IdleEntered); collector.TryPublishOsSignal(TrackingSignalKind.IdleExited);
        await collector.PublishAsync(TrackingSignalKind.Start); await store.ObservationWritten.Task.WaitAsync(TimeSpan.FromSeconds(2));
        native.Raise(10); await WaitUntilAsync(() => store.ObservationCount >= 2); await collector.PublishAsync(TrackingSignalKind.Stop); await run.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(TrackingStatus.Stopped, coordinator.Status); Assert.True(store.ObservationCount >= 2);
    }
    [Fact]
    public async Task SaturatedConditionsAndAcknowledgedCommandsConvergeToFinalState()
    {
        var native = new FakeNative(); await using var collector = new WindowsTrackingCollector(native, new() { ChannelCapacity = 1 }); await collector.StartAsync();
        var store = new BlockingStore(); var machine = new TrackingStateMachine(new RuleExclusionEvaluator([]), () => new("UTC", TimeSpan.Zero)); var coordinator = new TrackingCoordinator(machine, store, collector); var run = coordinator.RunAsync();
        await collector.PublishAsync(TrackingSignalKind.Start); await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        collector.TryPublishOsSignal(TrackingSignalKind.Locked); collector.TryPublishOsSignal(TrackingSignalKind.Unlocked);
        collector.TryPublishOsSignal(TrackingSignalKind.IdleEntered); collector.TryPublishOsSignal(TrackingSignalKind.IdleExited);
        collector.TryPublishOsSignal(TrackingSignalKind.Suspended); collector.TryPublishOsSignal(TrackingSignalKind.Resumed);
        var pause = collector.PublishAsync(TrackingSignalKind.Pause).AsTask(); var resume = collector.PublishAsync(TrackingSignalKind.Resume).AsTask();
        store.Release.TrySetResult(); await pause; await resume; await collector.PublishAsync(TrackingSignalKind.EnterPrivate); await collector.PublishAsync(TrackingSignalKind.ExitPrivate);
        await WaitUntilAsync(() => machine.Status == TrackingStatus.Running && machine.EffectiveGap is null);
        await collector.PublishAsync(TrackingSignalKind.Stop); await run.WaitAsync(TimeSpan.FromSeconds(2)); Assert.Equal(TrackingStatus.Stopped, machine.Status);
    }
    [Fact]
    public async Task ConcurrentProducersExposeStrictLogicalOrderAndFinalStop()
    {
        var native = new FakeNative(); await using var collector = new WindowsTrackingCollector(native, new() { ChannelCapacity = 128 }); await collector.StartAsync();
        await collector.PublishAsync(TrackingSignalKind.Start);
        var foreground = Task.Run(() => { for (var i = 1; i <= 30; i++) native.Raise(i); });
        var lifecycle = Task.Run(() => { for (var i = 0; i < 20; i++) { collector.TryPublishOsSignal(TrackingSignalKind.Locked); collector.TryPublishOsSignal(TrackingSignalKind.Unlocked); } });
        var commands = Task.Run(async () => { for (var i = 0; i < 10; i++) { await collector.PublishAsync(TrackingSignalKind.Pause); await collector.PublishAsync(TrackingSignalKind.Resume); } });
        await Task.WhenAll(foreground, lifecycle, commands); await collector.PublishAsync(TrackingSignalKind.Stop);
        var signals = new List<TrackingSignal>(); await foreach (var signal in collector.ReadAllAsync()) { signals.Add(signal); if (signal.Kind == TrackingSignalKind.Stop) break; }
        Assert.True(signals.Zip(signals.Skip(1)).All(pair => pair.First.Sequence < pair.Second.Sequence)); Assert.Contains(signals, x => x.Kind == TrackingSignalKind.Pause); Assert.Contains(signals, x => x.Kind is TrackingSignalKind.Unlocked or TrackingSignalKind.ConditionsChanged);
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
    private sealed class RecordingStore : ITrackingBatchStore
    {
        private int observations;
        public int ObservationCount => Volatile.Read(ref observations);
        public TaskCompletionSource ObservationWritten { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task PersistTrackingBatchAsync(TrackingPersistenceBatch batch, CancellationToken cancellationToken = default) { Interlocked.Add(ref observations, batch.Observations.Count); if (batch.Observations.Count > 0) ObservationWritten.TrySetResult(); return Task.CompletedTask; }
    }
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(2); while (!condition() && DateTime.UtcNow < timeout) await Task.Delay(10); Assert.True(condition());
    }
    private static TrackingSignal TestSignal(TrackingSignalKind kind, long second) => new(kind, new(new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero).AddSeconds(second)), new(second));
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
