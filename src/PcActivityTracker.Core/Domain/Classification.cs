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

public sealed record Project(ProjectId Id, string Name) { public string Name { get; } = Require(Name); private static string Require(string value) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Il nome è obbligatorio.") : value.Trim(); }
public sealed record Job(JobId Id, ProjectId ProjectId, string Name) { public string Name { get; } = string.IsNullOrWhiteSpace(Name) ? throw new ArgumentException("Il nome è obbligatorio.") : Name.Trim(); }
public sealed record Category(CategoryId Id, string Name) { public string Name { get; } = string.IsNullOrWhiteSpace(Name) ? throw new ArgumentException("Il nome è obbligatorio.") : Name.Trim(); }
