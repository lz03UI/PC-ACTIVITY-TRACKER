using PcActivityTracker.Core.Domain;
using PcActivityTracker.Core.Tracking;
using Xunit;

namespace PcActivityTracker.Core.UnitTests;

public sealed class TrackingStateMachineTests
{
    private static readonly DateTimeOffset Origin = new(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);
    private static TrackingStateMachine Create(params ExclusionRule[] rules) => new(new RuleExclusionEvaluator(rules), () => new("Europe/Rome", TimeSpan.FromHours(2)));
    private static TrackingSignal Signal(TrackingSignalKind kind, int second, ForegroundSnapshot? foreground = null) => new(kind, new(Origin.AddSeconds(second)), new(second), foreground);
    private static readonly ForegroundSnapshot A = new(1, "alpha", "C:\\alpha.exe");
    private static readonly ForegroundSnapshot B = new(2, "beta", "C:\\beta.exe");

    [Fact] public void StartTransitionsToRunningAndRequestsReconciliation() { var m = Create(); var effects = m.Apply(Signal(TrackingSignalKind.Start, 0)); Assert.Equal(TrackingStatus.Running, m.Status); Assert.IsType<ReconciliationRequired>(Assert.Single(effects)); }
    [Fact] public void ForegroundChangeClosesPreviousInterval() { var m = Started(); m.Apply(Signal(TrackingSignalKind.Reconcile, 1, A)); var effects = m.Apply(Signal(TrackingSignalKind.ForegroundChanged, 2, B)); Assert.Contains(effects, x => x is IntervalClosed); Assert.Contains(effects, x => x is ObservationAccepted); }
    [Fact] public void DuplicateForegroundIsIgnored() { var m = Started(); m.Apply(Signal(TrackingSignalKind.Reconcile, 1, A)); Assert.Empty(m.Apply(Signal(TrackingSignalKind.ForegroundChanged, 2, A))); }
    [Fact] public void NullForegroundClosesCurrent() { var m = Started(); m.Apply(Signal(TrackingSignalKind.Reconcile, 1, A)); Assert.IsType<IntervalClosed>(Assert.Single(m.Apply(Signal(TrackingSignalKind.ForegroundChanged, 2)))); }
    [Fact] public void IdleCreatesGapAndExitReconciles() { var m = Active(); Assert.Contains(m.Apply(Signal(TrackingSignalKind.IdleEntered, 2)), x => x is IntervalClosed); var effects = m.Apply(Signal(TrackingSignalKind.IdleExited, 3)); Assert.Contains(effects, x => x is GapClosed); Assert.Contains(effects, x => x is ReconciliationRequired); }
    [Fact] public void DuplicateIdleTransitionIsIgnored() { var m = Active(); m.Apply(Signal(TrackingSignalKind.IdleEntered, 2)); Assert.Empty(m.Apply(Signal(TrackingSignalKind.IdleEntered, 3))); }
    [Fact] public void LockUnlockCreatesLockedGap() { var m = Active(); m.Apply(Signal(TrackingSignalKind.Locked, 2)); var gap = Assert.IsType<GapClosed>(m.Apply(Signal(TrackingSignalKind.Unlocked, 3)).First()); Assert.Equal(ActivityState.Locked, gap.Gap.State); }
    [Fact] public void SuspendResumeCreatesSuspendedGap() { var m = Active(); m.Apply(Signal(TrackingSignalKind.Suspended, 2)); var effects = m.Apply(Signal(TrackingSignalKind.Resumed, 3)); Assert.Equal(ActivityState.Suspended, Assert.IsType<GapClosed>(effects[0]).Gap.State); Assert.IsType<ReconciliationRequired>(effects[1]); }
    [Fact] public void PauseResumeCreatesPausedGap() { var m = Active(); m.Apply(Signal(TrackingSignalKind.Pause, 2)); Assert.Equal(TrackingStatus.Paused, m.Status); var effects = m.Apply(Signal(TrackingSignalKind.Resume, 3)); Assert.Equal(ActivityState.Paused, Assert.IsType<GapClosed>(effects[0]).Gap.State); }
    [Fact] public void PrivateCreatesOnlyAnonymousGap() { var m = Active(); var enter = m.Apply(Signal(TrackingSignalKind.EnterPrivate, 2)); Assert.DoesNotContain(enter, x => x is ObservationAccepted); var exit = m.Apply(Signal(TrackingSignalKind.ExitPrivate, 3)); Assert.Equal(ActivityState.Private, Assert.IsType<GapClosed>(exit[0]).Gap.State); }
    [Fact] public void ForegroundDuringPrivateIsDiscarded() { var m = Active(); m.Apply(Signal(TrackingSignalKind.EnterPrivate, 2)); Assert.Empty(m.Apply(Signal(TrackingSignalKind.ForegroundChanged, 3, B))); }
    [Fact] public void ForegroundDuringPauseIsDiscarded() { var m = Active(); m.Apply(Signal(TrackingSignalKind.Pause, 2)); Assert.Empty(m.Apply(Signal(TrackingSignalKind.ForegroundChanged, 3, B))); }
    [Fact] public void StopClosesOpenActivity() { var m = Active(); Assert.Contains(m.Apply(Signal(TrackingSignalKind.Stop, 2)), x => x is IntervalClosed); Assert.Equal(TrackingStatus.Stopped, m.Status); }
    [Fact] public void StaleSignalIsIgnored() { var m = Active(); Assert.Empty(m.Apply(Signal(TrackingSignalKind.ForegroundChanged, 0, B))); }
    [Fact] public void EqualTimestampTransitionsAreAllowed() { var m = Active(); m.Apply(Signal(TrackingSignalKind.Pause, 2)); var gap = Assert.IsType<GapClosed>(m.Apply(Signal(TrackingSignalKind.Resume, 2))[0]); Assert.Equal(TimeSpan.Zero, gap.Gap.Period.Duration); }
    [Fact] public void CollectorRestartClosesAndReconciles() { var m = Active(); var effects = m.Apply(Signal(TrackingSignalKind.CollectorRestarted, 2)); Assert.IsType<IntervalClosed>(effects[0]); Assert.IsType<ReconciliationRequired>(effects[1]); }
    [Fact] public void WallClockGoingBackwardCannotCreateNegativeRange() { var m = Started(); m.Apply(new(TrackingSignalKind.Reconcile, new(Origin.AddHours(1)), new(1), A)); var effects = m.Apply(new(TrackingSignalKind.ForegroundChanged, new(Origin), new(2), B)); Assert.Equal(TimeSpan.Zero, Assert.IsType<IntervalClosed>(effects[0]).Interval.Period.Duration); }
    [Fact] public void ClockChangeClosesWithExplicitReason() { var m = Active(); var interval = Assert.IsType<IntervalClosed>(m.Apply(Signal(TrackingSignalKind.ClockChanged, 2))[0]); Assert.Equal(DiscontinuityReason.ClockChanged, interval.Interval.EndReason); }
    [Fact] public void TimeZoneChangeClosesWithExplicitReason() { var m = Active(); var interval = Assert.IsType<IntervalClosed>(m.Apply(Signal(TrackingSignalKind.TimeZoneChanged, 2))[0]); Assert.Equal(DiscontinuityReason.TimeZoneChanged, interval.Interval.EndReason); }
    [Fact] public void ApplicationExclusionPreventsObservation() { var rule = new ExclusionRule(ExclusionRuleId.New(), ExclusionKind.Application, "alpha"); var m = Create(rule); m.Apply(Signal(TrackingSignalKind.Start, 0)); Assert.Empty(m.Apply(Signal(TrackingSignalKind.Reconcile, 1, A))); }
    [Fact] public void WindowTitleExclusionPreventsObservation() { var rule = new ExclusionRule(ExclusionRuleId.New(), ExclusionKind.WindowTitle, "secret"); var m = Create(rule); m.Apply(Signal(TrackingSignalKind.Start, 0)); Assert.Empty(m.Apply(Signal(TrackingSignalKind.Reconcile, 1, A with { WindowTitle = "Secret document" }))); }

    private static TrackingStateMachine Started() { var m = Create(); m.Apply(Signal(TrackingSignalKind.Start, 0)); return m; }
    private static TrackingStateMachine Active() { var m = Started(); m.Apply(Signal(TrackingSignalKind.Reconcile, 1, A)); return m; }
}
