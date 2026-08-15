using PcActivityTracker.Core.Domain;
using PcActivityTracker.Core.Persistence;

namespace PcActivityTracker.Core.Tracking;

public enum TrackingStatus { Stopped, Running, Paused, Private }
public enum TrackingSignalKind { Start, Stop, Pause, Resume, EnterPrivate, ExitPrivate, ForegroundChanged, IdleEntered, IdleExited, Locked, Unlocked, Suspended, Resumed, Reconcile, CollectorRestarted, ClockChanged, TimeZoneChanged }

public sealed record ForegroundSnapshot(int ProcessId, string ProcessName, string? ExecutablePath = null, string? WindowTitle = null);
public sealed record TrackingSignal(TrackingSignalKind Kind, UtcInstant At, MonotonicTimestamp Monotonic, ForegroundSnapshot? Foreground = null);

public interface IForegroundSnapshotProvider { ValueTask<ForegroundSnapshot?> GetCurrentAsync(CancellationToken cancellationToken = default); }
public interface IReconciliationAwareForegroundSnapshotProvider : IForegroundSnapshotProvider { void DiscardPendingSignals(); }
public interface ITrackingSignalSource : IAsyncDisposable
{
    event EventHandler<TrackingSignal>? Signal;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
public interface IExclusionEvaluator { bool IsExcluded(ForegroundSnapshot snapshot); }

public sealed class RuntimeMetrics
{
    private long signals, reconciliations, writes, dropped;
    public long Signals => Interlocked.Read(ref signals);
    public long Reconciliations => Interlocked.Read(ref reconciliations);
    public long PersistenceWrites => Interlocked.Read(ref writes);
    public long DroppedSignals => Interlocked.Read(ref dropped);
    public void SignalReceived() => Interlocked.Increment(ref signals);
    public void ReconciliationRequested() => Interlocked.Increment(ref reconciliations);
    public void PersistenceWrite() => Interlocked.Increment(ref writes);
    public void SignalDropped() => Interlocked.Increment(ref dropped);
}

public sealed class RuleExclusionEvaluator(IEnumerable<ExclusionRule> rules) : IExclusionEvaluator
{
    private readonly ExclusionRule[] rules = rules.Where(x => x.IsEnabled).ToArray();
    public bool IsExcluded(ForegroundSnapshot value) => rules.Any(rule => rule.Kind switch
    {
        ExclusionKind.Application => rule.Matches(value.ProcessName) || (value.ExecutablePath is { } path && rule.Matches(path)),
        ExclusionKind.WindowTitle => value.WindowTitle is { } title && rule.Matches(title),
        ExclusionKind.FilePath => false,
        _ => false
    });
}

public abstract record TrackingEffect;
public sealed record ObservationAccepted(RawObservation Observation) : TrackingEffect;
public sealed record IntervalClosed(ActivityInterval Interval) : TrackingEffect;
public sealed record GapClosed(ActivityGap Gap) : TrackingEffect;
public sealed record ReconciliationRequired : TrackingEffect;

/// <summary>Riduttore deterministico. Il monotonic ordina i segnali; UTC serve solo a materializzare gli intervalli.</summary>
public sealed class TrackingStateMachine(IExclusionEvaluator exclusions, Func<LocalTimeContext> localTime)
{
    private sealed record OpenActivity(ObservationId ObservationId, ForegroundSnapshot Snapshot, UtcInstant At);
    private sealed record OpenGapState(ActivityState State, UtcInstant At);
    private OpenActivity? activity;
    private OpenGapState? gap;
    private MonotonicTimestamp? last;
    private bool idle;
    private bool locked;
    private bool suspended;

    public TrackingStatus Status { get; private set; } = TrackingStatus.Stopped;

    public IReadOnlyList<TrackingEffect> Apply(TrackingSignal signal)
    {
        if (last is { } previous && signal.Monotonic.Value < previous.Value) return [];
        last = signal.Monotonic;
        var effects = new List<TrackingEffect>();
        if (Status == TrackingStatus.Stopped && signal.Kind is not (TrackingSignalKind.Start or TrackingSignalKind.Stop)) return effects;

        switch (signal.Kind)
        {
            case TrackingSignalKind.Start when Status == TrackingStatus.Stopped:
                Status = TrackingStatus.Running; effects.Add(new ReconciliationRequired()); break;
            case TrackingSignalKind.Stop:
                CloseCurrent(signal.At, effects, DiscontinuityReason.None); Status = TrackingStatus.Stopped;
                idle = locked = suspended = false; break;
            case TrackingSignalKind.Pause when Status is TrackingStatus.Running:
                CloseCurrent(signal.At, effects); Status = TrackingStatus.Paused; RestoreGap(signal.At); break;
            case TrackingSignalKind.Resume when Status is TrackingStatus.Paused:
                CloseGap(signal.At, effects); Status = TrackingStatus.Running; RestoreGap(signal.At); if (CanObserve) effects.Add(new ReconciliationRequired()); break;
            case TrackingSignalKind.EnterPrivate when Status is TrackingStatus.Running or TrackingStatus.Paused:
                CloseCurrent(signal.At, effects); Status = TrackingStatus.Private; RestoreGap(signal.At); break;
            case TrackingSignalKind.ExitPrivate when Status is TrackingStatus.Private:
                CloseGap(signal.At, effects); Status = TrackingStatus.Running; RestoreGap(signal.At); if (CanObserve) effects.Add(new ReconciliationRequired()); break;
            case TrackingSignalKind.IdleEntered when Status == TrackingStatus.Running && !idle:
                idle = true; CloseCurrent(signal.At, effects); OpenGap(ActivityState.Idle, signal.At); break;
            case TrackingSignalKind.IdleExited when idle:
                idle = false; CloseGap(signal.At, effects); RestoreGap(signal.At); if (CanObserve) effects.Add(new ReconciliationRequired()); break;
            case TrackingSignalKind.Locked when !locked:
                locked = true; CloseCurrent(signal.At, effects, DiscontinuityReason.Lock); OpenGap(ActivityState.Locked, signal.At); break;
            case TrackingSignalKind.Unlocked when locked:
                locked = false; CloseGap(signal.At, effects); RestoreGap(signal.At); if (CanObserve) effects.Add(new ReconciliationRequired()); break;
            case TrackingSignalKind.Suspended when !suspended:
                suspended = true; CloseCurrent(signal.At, effects, DiscontinuityReason.Sleep); OpenGap(ActivityState.Suspended, signal.At); break;
            case TrackingSignalKind.Resumed when suspended:
                suspended = false; CloseGap(signal.At, effects); RestoreGap(signal.At); if (CanObserve) effects.Add(new ReconciliationRequired()); break;
            case TrackingSignalKind.CollectorRestarted:
                CloseCurrent(signal.At, effects, DiscontinuityReason.CollectorRestart); if (CanObserve) effects.Add(new ReconciliationRequired()); break;
            case TrackingSignalKind.ClockChanged:
                CloseCurrent(signal.At, effects, DiscontinuityReason.ClockChanged); RestoreGap(signal.At); if (CanObserve) effects.Add(new ReconciliationRequired()); break;
            case TrackingSignalKind.TimeZoneChanged:
                CloseCurrent(signal.At, effects, DiscontinuityReason.TimeZoneChanged); RestoreGap(signal.At); if (CanObserve) effects.Add(new ReconciliationRequired()); break;
            case TrackingSignalKind.ForegroundChanged or TrackingSignalKind.Reconcile when CanObserve:
                AcceptForeground(signal, effects); break;
        }
        return effects;
    }

    private bool CanObserve => Status == TrackingStatus.Running && !idle && !locked && !suspended;
    private void AcceptForeground(TrackingSignal signal, List<TrackingEffect> effects)
    {
        var snapshot = signal.Foreground;
        if (snapshot is null) { CloseCurrent(signal.At, effects); return; }
        if (activity is { } current && IsSameApplication(current.Snapshot, snapshot)) return;
        CloseActivity(signal.At, effects);
        if (exclusions.IsExcluded(snapshot)) return;
        var id = new ObservationId(Guid.NewGuid());
        var observation = new RawObservation(id, ObservationSource.ForegroundApplication, signal.At, localTime(), ActivityState.Active, new(snapshot.ProcessName, snapshot.ExecutablePath));
        activity = new(id, snapshot, signal.At);
        effects.Add(new ObservationAccepted(observation));
    }
    private static bool IsSameApplication(ForegroundSnapshot left, ForegroundSnapshot right) =>
        left.ProcessId == right.ProcessId && string.Equals(left.ProcessName, right.ProcessName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.ExecutablePath, right.ExecutablePath, StringComparison.OrdinalIgnoreCase);
    private void CloseCurrent(UtcInstant at, List<TrackingEffect> effects, DiscontinuityReason reason = DiscontinuityReason.None) { CloseActivity(at, effects, reason); CloseGap(at, effects); }
    private void CloseActivity(UtcInstant at, List<TrackingEffect> effects, DiscontinuityReason reason = DiscontinuityReason.None)
    {
        if (activity is not { } open) return;
        var end = at.Value < open.At.Value ? open.At : at;
        effects.Add(new IntervalClosed(new(new(Guid.NewGuid()), open.ObservationId, new(open.At, end), ActivityState.Active, reason)));
        activity = null;
    }
    private void OpenGap(ActivityState state, UtcInstant at) { if (Status != TrackingStatus.Stopped) gap = new(state, at); }
    private void RestoreGap(UtcInstant at)
    {
        if (suspended) OpenGap(ActivityState.Suspended, at);
        else if (locked) OpenGap(ActivityState.Locked, at);
        else if (idle) OpenGap(ActivityState.Idle, at);
        else if (Status == TrackingStatus.Paused) OpenGap(ActivityState.Paused, at);
        else if (Status == TrackingStatus.Private) OpenGap(ActivityState.Private, at);
    }
    private void CloseGap(UtcInstant at, List<TrackingEffect> effects)
    {
        if (gap is not { } open) return;
        var end = at.Value < open.At.Value ? open.At : at;
        effects.Add(new GapClosed(new(new(Guid.NewGuid()), new(open.At, end), open.State)));
        gap = null;
    }
}

public sealed class TrackingCoordinator(TrackingStateMachine machine, IObservationStore store, IForegroundSnapshotProvider foreground, RuntimeMetrics? runtimeMetrics = null)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly RuntimeMetrics metrics = runtimeMetrics ?? new();
    public TrackingStatus Status => machine.Status;
    public event EventHandler<TrackingStatus>? StatusChanged;

    public async Task HandleAsync(TrackingSignal signal, CancellationToken cancellationToken = default)
    {
        metrics.SignalReceived();
        await gate.WaitAsync(cancellationToken);
        try
        {
            var effects = machine.Apply(signal);
            await PersistAsync(effects, cancellationToken);
            foreach (var _ in effects.OfType<ReconciliationRequired>())
            {
                metrics.ReconciliationRequested();
                (foreground as IReconciliationAwareForegroundSnapshotProvider)?.DiscardPendingSignals();
                var current = await foreground.GetCurrentAsync(cancellationToken);
                await PersistAsync(machine.Apply(signal with { Kind = TrackingSignalKind.Reconcile, Foreground = current }), cancellationToken);
            }
            StatusChanged?.Invoke(this, machine.Status);
        }
        finally { gate.Release(); }
    }
    private async Task PersistAsync(IEnumerable<TrackingEffect> effects, CancellationToken token)
    {
        foreach (var effect in effects)
            switch (effect)
            {
                case ObservationAccepted x: await store.AddObservationAsync(x.Observation, token); metrics.PersistenceWrite(); break;
                case IntervalClosed x: await store.AddActivityIntervalAsync(x.Interval, token); metrics.PersistenceWrite(); break;
                case GapClosed x: await store.AddActivityGapAsync(x.Gap, token); metrics.PersistenceWrite(); break;
            }
    }
}
