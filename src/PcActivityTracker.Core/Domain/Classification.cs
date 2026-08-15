namespace PcActivityTracker.Core.Domain;

public enum ClassificationProvenance { Manual, DeterministicRule, SystemInferred, AiSuggestion }
public enum ClassificationTargetType { Observation, ActivityInterval }

public sealed record Classification
{
    public Classification(ClassificationId id, ClassificationTargetType targetType, Guid targetId,
        ClassificationProvenance provenance, UtcInstant classifiedAt, string rationale, ProjectId? projectId = null,
        JobId? jobId = null, CategoryId? categoryId = null, string? ruleId = null)
    {
        DomainId.EnsureAssigned(id, nameof(id));
        if (targetId == Guid.Empty) throw new ArgumentException("Il target è obbligatorio.", nameof(targetId));
        if (string.IsNullOrWhiteSpace(rationale)) throw new ArgumentException("La motivazione è obbligatoria.", nameof(rationale));
        if (provenance == ClassificationProvenance.DeterministicRule && string.IsNullOrWhiteSpace(ruleId))
            throw new ArgumentException("Una regola deterministica richiede rule id.", nameof(ruleId));
        if (projectId is not null && jobId is not null)
            throw new ArgumentException("Il progetto è derivato dalla commessa e non può essere duplicato.", nameof(projectId));
        if (projectId is { } assignedProjectId) DomainId.EnsureAssigned(assignedProjectId, nameof(projectId));
        if (jobId is { } assignedJobId) DomainId.EnsureAssigned(assignedJobId, nameof(jobId));
        if (categoryId is { } assignedCategoryId) DomainId.EnsureAssigned(assignedCategoryId, nameof(categoryId));
        Id = id; TargetType = targetType; TargetId = targetId; Provenance = provenance; ClassifiedAt = classifiedAt;
        Rationale = rationale; ProjectId = projectId; JobId = jobId; CategoryId = categoryId; RuleId = ruleId;
    }
    public ClassificationId Id { get; }
    public ClassificationTargetType TargetType { get; }
    public Guid TargetId { get; }
    public ClassificationProvenance Provenance { get; }
    public UtcInstant ClassifiedAt { get; }
    public string Rationale { get; }
    public ProjectId? ProjectId { get; }
    public JobId? JobId { get; }
    public CategoryId? CategoryId { get; }
    public string? RuleId { get; }
    public bool IsAuthoritative => Provenance != ClassificationProvenance.AiSuggestion;
}

public sealed record Project
{
    public Project(ProjectId id, string name) { DomainId.EnsureAssigned(id, nameof(id)); Id = id; Name = RequireName(name); }
    public ProjectId Id { get; }
    public string Name { get; }
    private static string RequireName(string value) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Il nome è obbligatorio.", nameof(value)) : value.Trim();
}

public sealed record Job
{
    public Job(JobId id, ProjectId projectId, string name) { DomainId.EnsureAssigned(id, nameof(id)); DomainId.EnsureAssigned(projectId, nameof(projectId)); Id = id; ProjectId = projectId; Name = RequireName(name); }
    public JobId Id { get; }
    public ProjectId ProjectId { get; }
    public string Name { get; }
    private static string RequireName(string value) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Il nome è obbligatorio.", nameof(value)) : value.Trim();
}

public sealed record Category
{
    public Category(CategoryId id, string name) { DomainId.EnsureAssigned(id, nameof(id)); Id = id; Name = RequireName(name); }
    public CategoryId Id { get; }
    public string Name { get; }
    private static string RequireName(string value) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Il nome è obbligatorio.", nameof(value)) : value.Trim();
}
