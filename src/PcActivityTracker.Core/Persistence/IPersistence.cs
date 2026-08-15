using PcActivityTracker.Core.Domain;

namespace PcActivityTracker.Core.Persistence;

public interface IObservationStore
{
    Task AddObservationAsync(RawObservation observation, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RawObservation>> GetObservationsAsync(TimeRange period, CancellationToken cancellationToken = default);
    Task AddActivityIntervalAsync(ActivityInterval interval, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivityInterval>> GetActivityIntervalsAsync(TimeRange period, CancellationToken cancellationToken = default);
    Task AddActivityGapAsync(ActivityGap gap, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivityGap>> GetActivityGapsAsync(TimeRange period, CancellationToken cancellationToken = default);
}
public sealed record TrackingPersistenceBatch(
    IReadOnlyList<RawObservation> Observations,
    IReadOnlyList<ActivityInterval> Intervals,
    IReadOnlyList<ActivityGap> Gaps)
{
    public int Count => Observations.Count + Intervals.Count + Gaps.Count;
    public bool IsEmpty => Count == 0;
}
public interface ITrackingBatchStore
{
    Task PersistTrackingBatchAsync(TrackingPersistenceBatch batch, CancellationToken cancellationToken = default);
}
public interface IClassificationStore
{
    Task AddClassificationAsync(Classification classification, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Classification>> GetClassificationsAsync(ClassificationTargetType type, Guid targetId, CancellationToken cancellationToken = default);
}
public interface IPrivacyStore
{
    Task SaveExclusionAsync(ExclusionRule rule, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExclusionRule>> GetExclusionsAsync(CancellationToken cancellationToken = default);
}
public interface IWorkTaxonomyStore
{
    Task SaveProjectAsync(Project project, CancellationToken cancellationToken = default);
    Task SaveJobAsync(Job job, CancellationToken cancellationToken = default);
    Task SaveCategoryAsync(Category category, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Project>> GetProjectsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Job>> GetJobsAsync(ProjectId projectId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default);
}
public interface IRetentionStore
{
    /// <summary>
    /// Elimina evidenza e periodi conclusi entro il cutoff, tronca i periodi attraversanti e restituisce
    /// il numero di osservazioni grezze eliminate. I periodi successivi al cutoff restano invariati.
    /// </summary>
    Task<int> DeleteActivityBeforeAsync(UtcInstant cutoff, CancellationToken cancellationToken = default);
}
