using PcActivityTracker.Core.Domain;
using PcActivityTracker.Core.Persistence;

namespace PcActivityTracker.Core.Tracking;

public enum TrackingStatus { Stopped, Running, Paused, Private, Faulted }
public enum TrackingSignalKind
{
    Start, Stop, Pause, Resume, EnterPrivate, ExitPrivate, ForegroundChanged, IdleEntered, IdleExited,
    Locked, Unlocked, SessionDisconnected, SessionReconnected, Suspended, Resumed, Reconcile,
    SignalLossDetected, CollectorRestarted, ClockChanged, TimeZoneChanged
}

public sealed record ForegroundSnapshot(int ProcessId, string ProcessName, string? ExecutablePath = null);
public sealed record TrackingSignal(TrackingSignalKind Kind, UtcInstant At, MonotonicTimestamp Monotonic, ForegroundSnapshot? Foreground = null, long Sequence = 0);

public interface ITrackingSignalSource : IAsyncDisposable
{
    IAsyncEnumerable<TrackingSignal> ReadAllAsync(CancellationToken cancellationToken = default);
    bool TryPublish(TrackingSignalKind kind);
    void RequestReconciliation();
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
    public void PersistenceWrite(int count) => Interlocked.Add(ref writes, count);
    public void SignalDropped() => Interlocked.Increment(ref dropped);
}

/// <summary>In 02A sono supportate solo esclusioni applicazione; titolo finestra e percorsi documento sono rinviati.</summary>
public sealed class RuleExclusionEvaluator(IEnumerable<ExclusionRule> rules) : IExclusionEvaluator
{
    private readonly ExclusionRule[] applicationRules = rules.Where(x => x.IsEnabled && x.Kind == ExclusionKind.Application).ToArray();
    public bool IsExcluded(ForegroundSnapshot value) => applicationRules.Any(rule =>
        rule.Matches(value.ProcessName) || (value.ExecutablePath is { } path && rule.Matches(path)));
}

public abstract record TrackingEffect;
public sealed record ObservationAccepted(RawObservation Observation) : TrackingEffect;
public sealed record IntervalClosed(ActivityInterval Interval) : TrackingEffect;
public sealed record GapClosed(ActivityGap Gap) : TrackingEffect;
public sealed record ReconciliationRequired : TrackingEffect;

/// <summary>
/// Riduttore deterministico. La priorità unica delle condizioni è Suspended &gt; Locked/Disconnected &gt;
/// Idle &gt; Paused &gt; Private. Il monotonic ordina i segnali; UTC materializza gli intervalli.
/// </summary>
public sealed class TrackingStateMachine(IExclusionEvaluator exclusions, Func<LocalTimeContext> localTime)
{
    private sealed record OpenActivity(ObservationId ObservationId, ForegroundSnapshot Snapshot, UtcInstant At);
    private sealed record OpenGap(ActivityState State, UtcInstant At);
    private sealed record MachineSnapshot(TrackingStatus Status, TrackingStatus PrivateReturnStatus, OpenActivity? Activity,
        OpenGap? Gap, MonotonicTimestamp? Last, bool Idle, bool Locked, bool Disconnected, bool Suspended);

    private OpenActivity? activity;
    private OpenGap? gap;
    private MonotonicTimestamp? last;
    private bool idle, locked, disconnected, suspended;
    private TrackingStatus privateReturnStatus = TrackingStatus.Running;

    public TrackingStatus Status { get; private set; } = TrackingStatus.Stopped;
    public ActivityState? EffectiveGap => suspended ? ActivityState.Suspended : locked || disconnected ? ActivityState.Locked : idle ? ActivityState.Idle :
        Status == TrackingStatus.Paused || Status == TrackingStatus.Private && privateReturnStatus == TrackingStatus.Paused ? ActivityState.Paused :
        Status == TrackingStatus.Private ? ActivityState.Private : null;

    public IReadOnlyList<TrackingEffect> Apply(TrackingSignal signal)
    {
        if (Status == TrackingStatus.Faulted)
        {
            if (signal.Kind != TrackingSignalKind.Start) return [];
            Status = TrackingStatus.Stopped; idle = locked = disconnected = suspended = false; last = null;
        }
        if (last is { } previous && signal.Monotonic.Value < previous.Value) return [];
        last = signal.Monotonic;
        var effects = new List<TrackingEffect>();
        if (Status == TrackingStatus.Stopped && signal.Kind is not (TrackingSignalKind.Start or TrackingSignalKind.Stop)) return effects;

        switch (signal.Kind)
        {
            case TrackingSignalKind.Start when Status == TrackingStatus.Stopped:
                Status = TrackingStatus.Running; effects.Add(new ReconciliationRequired()); break;
            case TrackingSignalKind.Stop:
                CloseActivity(signal.At, effects); CloseGap(signal.At, effects); Status = TrackingStatus.Stopped;
                idle = locked = disconnected = suspended = false; break;
            case TrackingSignalKind.Pause when Status == TrackingStatus.Running:
                Transition(signal.At, effects, () => Status = TrackingStatus.Paused); break;
            case TrackingSignalKind.Resume when Status == TrackingStatus.Paused:
                Transition(signal.At, effects, () => Status = TrackingStatus.Running, reconcileWhenObservable: true); break;
            case TrackingSignalKind.EnterPrivate when Status is TrackingStatus.Running or TrackingStatus.Paused:
                Transition(signal.At, effects, () => { privateReturnStatus = Status; Status = TrackingStatus.Private; }); break;
            case TrackingSignalKind.ExitPrivate when Status == TrackingStatus.Private:
                Transition(signal.At, effects, () => Status = privateReturnStatus, reconcileWhenObservable: true); break;
            case TrackingSignalKind.IdleEntered when !idle:
                Transition(signal.At, effects, () => idle = true); break;
            case TrackingSignalKind.IdleExited when idle:
                Transition(signal.At, effects, () => idle = false, reconcileWhenObservable: true); break;
            case TrackingSignalKind.Locked when !locked:
                Transition(signal.At, effects, () => locked = true, DiscontinuityReason.Lock); break;
            case TrackingSignalKind.Unlocked when locked:
                Transition(signal.At, effects, () => locked = false, reconcileWhenObservable: true); break;
            case TrackingSignalKind.SessionDisconnected when !disconnected:
                Transition(signal.At, effects, () => disconnected = true, DiscontinuityReason.Lock); break;
            case TrackingSignalKind.SessionReconnected when disconnected:
                Transition(signal.At, effects, () => disconnected = false, reconcileWhenObservable: true); break;
            case TrackingSignalKind.Suspended when !suspended:
                Transition(signal.At, effects, () => suspended = true, DiscontinuityReason.Sleep); break;
            case TrackingSignalKind.Resumed when suspended:
                Transition(signal.At, effects, () => suspended = false, reconcileWhenObservable: true); break;
            case TrackingSignalKind.SignalLossDetected:
                Boundary(signal.At, effects, DiscontinuityReason.SignalLoss); break;
            case TrackingSignalKind.CollectorRestarted:
                Boundary(signal.At, effects, DiscontinuityReason.CollectorRestart); break;
            case TrackingSignalKind.ClockChanged:
                Boundary(signal.At, effects, DiscontinuityReason.ClockChanged); break;
            case TrackingSignalKind.TimeZoneChanged:
                Boundary(signal.At, effects, DiscontinuityReason.TimeZoneChanged); break;
            case TrackingSignalKind.ForegroundChanged or TrackingSignalKind.Reconcile when CanObserve:
                AcceptForeground(signal, effects); break;
        }
        return effects;
    }

    internal object Capture() => new MachineSnapshot(Status, privateReturnStatus, activity, gap, last, idle, locked, disconnected, suspended);
    internal void Restore(object snapshot)
    {
        var value = (MachineSnapshot)snapshot;
        Status = value.Status; privateReturnStatus = value.PrivateReturnStatus; activity = value.Activity; gap = value.Gap;
        last = value.Last; idle = value.Idle; locked = value.Locked; disconnected = value.Disconnected; suspended = value.Suspended;
    }
    internal void MarkFaulted() { activity = null; gap = null; Status = TrackingStatus.Faulted; }

    private bool CanObserve => Status == TrackingStatus.Running && EffectiveGap is null;
    private void Transition(UtcInstant at, List<TrackingEffect> effects, Action mutation,
        DiscontinuityReason reason = DiscontinuityReason.None, bool reconcileWhenObservable = false)
    {
        var oldGap = EffectiveGap;
        mutation();
        var newGap = EffectiveGap;
        if (activity is not null && (newGap is not null || reason != DiscontinuityReason.None)) CloseActivity(at, effects, reason);
        if (gap is not null && oldGap != newGap) CloseGap(at, effects);
        if (gap is null && newGap is { } state) gap = new(state, at);
        if (reconcileWhenObservable && CanObserve) effects.Add(new ReconciliationRequired());
    }
    private void Boundary(UtcInstant at, List<TrackingEffect> effects, DiscontinuityReason reason)
    {
        CloseActivity(at, effects, reason);
        if (gap is not null) { CloseGap(at, effects); if (EffectiveGap is { } state) gap = new(state, at); }
        if (CanObserve) effects.Add(new ReconciliationRequired());
    }
    private void AcceptForeground(TrackingSignal signal, List<TrackingEffect> effects)
    {
        var snapshot = signal.Foreground;
        if (snapshot is null) { CloseActivity(signal.At, effects); return; }
        // L'esclusione precede sempre l'ottimizzazione same-application.
        if (exclusions.IsExcluded(snapshot)) { CloseActivity(signal.At, effects); return; }
        if (activity is { } current && IsSameApplication(current.Snapshot, snapshot)) return;
        CloseActivity(signal.At, effects);
        var id = ObservationId.New();
        var observation = new RawObservation(id, ObservationSource.ForegroundApplication, signal.At, localTime(), ActivityState.Active, new(snapshot.ProcessName, snapshot.ExecutablePath));
        activity = new(id, snapshot, signal.At);
        effects.Add(new ObservationAccepted(observation));
    }
    private static bool IsSameApplication(ForegroundSnapshot left, ForegroundSnapshot right) =>
        left.ProcessId == right.ProcessId && string.Equals(left.ProcessName, right.ProcessName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.ExecutablePath, right.ExecutablePath, StringComparison.OrdinalIgnoreCase);
    private void CloseActivity(UtcInstant at, List<TrackingEffect> effects, DiscontinuityReason reason = DiscontinuityReason.None)
    {
        if (activity is not { } open) return;
        var end = at.Value < open.At.Value ? open.At : at;
        effects.Add(new IntervalClosed(new(ActivityIntervalId.New(), open.ObservationId, new(open.At, end), ActivityState.Active, reason)));
        activity = null;
    }
    private void CloseGap(UtcInstant at, List<TrackingEffect> effects)
    {
        if (gap is not { } open) return;
        var end = at.Value < open.At.Value ? open.At : at;
        effects.Add(new GapClosed(new(ActivityGapId.New(), new(open.At, end), open.State)));
        gap = null;
    }
}

public sealed class TrackingCoordinator(TrackingStateMachine machine, ITrackingBatchStore store, ITrackingSignalSource source, RuntimeMetrics? runtimeMetrics = null)
{
    private readonly RuntimeMetrics metrics = runtimeMetrics ?? new();
    public TrackingStatus Status => machine.Status;
    public event EventHandler<TrackingStatus>? StatusChanged;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await foreach (var signal in source.ReadAllAsync(cancellationToken))
        {
            if (!await ProcessAsync(signal, cancellationToken)) break;
        }
    }

    private async Task<bool> ProcessAsync(TrackingSignal signal, CancellationToken token)
    {
        metrics.SignalReceived();
        var snapshot = machine.Capture();
        try
        {
            var effects = machine.Apply(signal).ToList();
            if (effects.RemoveAll(x => x is ReconciliationRequired) > 0)
            {
                metrics.ReconciliationRequested();
                source.RequestReconciliation();
            }
            var batch = new TrackingPersistenceBatch(
                effects.OfType<ObservationAccepted>().Select(x => x.Observation).ToArray(),
                effects.OfType<IntervalClosed>().Select(x => x.Interval).ToArray(),
                effects.OfType<GapClosed>().Select(x => x.Gap).ToArray());
            if (!batch.IsEmpty) { await store.PersistTrackingBatchAsync(batch, token); metrics.PersistenceWrite(batch.Count); }
            StatusChanged?.Invoke(this, machine.Status);
            return machine.Status != TrackingStatus.Stopped;
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            machine.Restore(snapshot);
            machine.MarkFaulted();
            StatusChanged?.Invoke(this, machine.Status);
            await source.StopAsync(CancellationToken.None);
            return false;
        }
    }
}
