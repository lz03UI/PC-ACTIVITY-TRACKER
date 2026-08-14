using PcActivityTracker.Core.Domain;

namespace PcActivityTracker.Core.Persistence;

public interface IObservationStore
{
    Task AddObservationAsync(RawObservation observation, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RawObservation>> GetObservationsAsync(TimeRange period, CancellationToken cancellationToken = default);
    Task AddActivityIntervalAsync(ActivityInterval interval, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivityInterval>> GetActivityIntervalsAsync(TimeRange period, CancellationToken cancellationToken = default);
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
    Task<int> DeleteActivityBeforeAsync(UtcInstant cutoff, CancellationToken cancellationToken = default);
}
