using System.Runtime.CompilerServices;
using PcActivityTracker.Core.Domain;
using PcActivityTracker.Core.Persistence;
using PcActivityTracker.Core.Tracking;
using Xunit;

namespace PcActivityTracker.Core.UnitTests;

public sealed class TrackingCoordinatorTests
{
    private static readonly DateTimeOffset Origin = new(2026, 8, 15, 0, 0, 0, TimeSpan.Zero);
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task PersistenceFailureFaultsRuntimeAndStopsFurtherBatches(int failAt)
    {
        var source = new FakeSource([
            Signal(TrackingSignalKind.Start, 0),
            Signal(TrackingSignalKind.ForegroundChanged, 1, new(2, "second")),
            Signal(TrackingSignalKind.ForegroundChanged, 2, new(3, "third")),
            Signal(TrackingSignalKind.Stop, 3)], new(1, "first"));
        var store = new FailingStore(failAt);
        var coordinator = new TrackingCoordinator(new(new RuleExclusionEvaluator([]), () => new("UTC", TimeSpan.Zero)), store, source);
        await coordinator.RunAsync();
        Assert.Equal(TrackingStatus.Faulted, coordinator.Status);
        Assert.Equal(failAt, store.Attempts);
        Assert.True(source.Stopped);
        Assert.DoesNotContain(store.Committed.SelectMany(x => x.Intervals), interval =>
            store.Committed.SelectMany(x => x.Observations).All(observation => observation.Id != interval.ObservationId));
    }

    [Fact]
    public async Task EffectsOfOneSignalAreSentAsOneBatch()
    {
        var source = new FakeSource([Signal(TrackingSignalKind.Start, 0), Signal(TrackingSignalKind.ForegroundChanged, 1, new(2, "second")), Signal(TrackingSignalKind.Stop, 2)], new(1, "first"));
        var store = new FailingStore(int.MaxValue); var coordinator = new TrackingCoordinator(new(new RuleExclusionEvaluator([]), () => new("UTC", TimeSpan.Zero)), store, source);
        await coordinator.RunAsync();
        var change = store.Committed.Single(x => x.Observations.Count == 1 && x.Intervals.Count == 1);
        Assert.Equal(change.Intervals[0].ObservationId, store.Committed[0].Observations[0].Id);
    }

    private static TrackingSignal Signal(TrackingSignalKind kind, int second, ForegroundSnapshot? foreground = null) => new(kind, new(Origin.AddSeconds(second)), new(second), foreground);
    private sealed class FailingStore(int failAt) : ITrackingBatchStore
    {
        public int Attempts { get; private set; }
        public List<TrackingPersistenceBatch> Committed { get; } = [];
        public Task PersistTrackingBatchAsync(TrackingPersistenceBatch batch, CancellationToken cancellationToken = default)
        {
            Attempts++; if (Attempts == failAt) throw new IOException("simulated persistence fault");
            Committed.Add(batch); return Task.CompletedTask;
        }
    }
    private sealed class FakeSource(IEnumerable<TrackingSignal> values, ForegroundSnapshot foreground) : ITrackingSignalSource
    {
        private TrackingSignal current = Signal(TrackingSignalKind.Start, 0);
        private bool reconcile;
        public bool Stopped { get; private set; }
        public async IAsyncEnumerable<TrackingSignal> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default) { foreach (var value in values) { current = value; yield return value; if (reconcile) { reconcile = false; yield return current with { Kind = TrackingSignalKind.Reconcile, Foreground = foreground }; } await Task.Yield(); } }
        public bool TryPublish(TrackingSignalKind kind) => true;
        public void RequestReconciliation() => reconcile = true;
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) { Stopped = true; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
