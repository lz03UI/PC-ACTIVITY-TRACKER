using PcActivityTracker.Core.Domain;
using Xunit;

namespace PcActivityTracker.Core.UnitTests;

public sealed class DomainTests
{
    private static readonly UtcInstant Noon = new(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));

    [Fact] public void UtcInstantRejectsNonUtcOffset() => Assert.Throws<ArgumentException>(() => new UtcInstant(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(1))));
    [Fact] public void TimeRangeRejectsNegativeDuration() => Assert.Throws<ArgumentOutOfRangeException>(() => new TimeRange(Noon, new(Noon.Value.AddSeconds(-1))));
    [Fact] public void TimeRangeUsesHalfOpenOverlap() { var first = new TimeRange(Noon, new(Noon.Value.AddHours(1))); var second = new TimeRange(first.End, new(first.End.Value.AddHours(1))); Assert.False(first.Overlaps(second)); }
    [Fact] public void MonotonicTimeRejectsBackwardMovement() => Assert.Throws<ArgumentOutOfRangeException>(() => MonotonicTimestamp.Elapsed(new(2), new(1), 1000));
    [Fact] public void TimeProviderSuppliesTestableUtcTime() { var provider = new FixedTimeProvider(Noon.Value); Assert.Equal(Noon, UtcInstant.Now(provider)); }
    [Fact] public void LocalContextPreservesZoneAndObservedDstOffset() { var context = new LocalTimeContext("Europe/Rome", TimeSpan.FromHours(2)); Assert.Equal(TimeSpan.FromHours(2), context.ObservedUtcOffset); }
    [Fact] public void EmptyIdentityIsRejected() => Assert.Throws<ArgumentException>(() => new RawObservation(new(Guid.Empty), ObservationSource.ForegroundApplication, Noon, new("UTC", TimeSpan.Zero), ActivityState.Active, new("app")));
    [Fact] public void BrowserRequiresMinimizedUrl() { Assert.Throws<ArgumentException>(() => new BrowserContext("example.test", "/work?q=secret")); Assert.Throws<ArgumentException>(() => new BrowserContext("example.test", "/#private")); }
    [Fact] public void BrowserObservationRequiresContext() => Assert.Throws<ArgumentException>(() => new RawObservation(ObservationId.New(), ObservationSource.Browser, Noon, new("UTC", TimeSpan.Zero), ActivityState.Active, new("browser")));
    [Fact] public void ObservationIsImmutableByShape() { var setters = typeof(RawObservation).GetProperties().Where(p => p.SetMethod?.IsPublic == true); Assert.Empty(setters); }
    [Fact] public void DeterministicClassificationRequiresRuleId() => Assert.Throws<ArgumentException>(() => new Classification(ClassificationId.New(), ClassificationTargetType.Observation, Guid.NewGuid(), ClassificationProvenance.DeterministicRule, Noon, "match"));
    [Fact] public void AiSuggestionIsNotAuthoritative() { var value = new Classification(ClassificationId.New(), ClassificationTargetType.Observation, Guid.NewGuid(), ClassificationProvenance.AiSuggestion, Noon, "suggestion"); Assert.False(value.IsAuthoritative); }
    [Fact] public void ManualClassificationIsSeparateFromObservation() { var observation = CreateObservation(); var classification = new Classification(ClassificationId.New(), ClassificationTargetType.Observation, observation.Id.Value, ClassificationProvenance.Manual, Noon, "utente"); Assert.Equal(observation.Id.Value, classification.TargetId); Assert.Equal("app", observation.Application.ProcessName); }
    [Fact] public void ExclusionMatchesBeforePersistenceCaseInsensitively() { var rule = new ExclusionRule(ExclusionRuleId.New(), ExclusionKind.BrowserDomain, "private.test"); Assert.True(rule.Matches("PRIVATE.TEST")); }
    [Fact] public void DisabledExclusionDoesNotMatch() => Assert.False(new ExclusionRule(ExclusionRuleId.New(), ExclusionKind.Application, "secret", false).Matches("secret-app"));
    [Fact] public void IntervalModelsClockDiscontinuity() { var value = new ActivityInterval(ActivityIntervalId.New(), ObservationId.New(), new(Noon, new(Noon.Value.AddMinutes(1))), ActivityState.Active, DiscontinuityReason.ClockChanged); Assert.Equal(DiscontinuityReason.ClockChanged, value.EndReason); }
    [Fact] public void PrivateActivityCannotBecomeRawObservation() => Assert.Throws<ArgumentException>(() => new RawObservation(ObservationId.New(), ObservationSource.ForegroundApplication, Noon, new("UTC", TimeSpan.Zero), ActivityState.Private, new("secret")));
    [Fact] public void PrivatePeriodIsContentFreeGap() { var gap = new ActivityGap(ActivityGapId.New(), new(Noon, new(Noon.Value.AddMinutes(5))), ActivityState.Private); Assert.Equal(ActivityState.Private, gap.State); }
    [Fact] public void PrivateActivityCannotReferenceObservation() => Assert.Throws<ArgumentException>(() => new ActivityInterval(ActivityIntervalId.New(), ObservationId.New(), new(Noon, new(Noon.Value.AddMinutes(5))), ActivityState.Private));
    [Fact] public void ClassificationCannotDuplicateProjectWithJob() => Assert.Throws<ArgumentException>(() => new Classification(ClassificationId.New(), ClassificationTargetType.Observation, Guid.NewGuid(), ClassificationProvenance.Manual, Noon, "incoerente", ProjectId.New(), JobId.New()));
    [Fact] public void TaxonomyRejectsEmptyIdentifiers() { Assert.Throws<ArgumentException>(() => new Project(new(Guid.Empty), "project")); Assert.Throws<ArgumentException>(() => new Job(new(Guid.Empty), ProjectId.New(), "job")); Assert.Throws<ArgumentException>(() => new Job(JobId.New(), new(Guid.Empty), "job")); Assert.Throws<ArgumentException>(() => new Category(new(Guid.Empty), "category")); }

    private static RawObservation CreateObservation() => new(ObservationId.New(), ObservationSource.ForegroundApplication, Noon, new("UTC", TimeSpan.Zero), ActivityState.Active, new("app"));
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
