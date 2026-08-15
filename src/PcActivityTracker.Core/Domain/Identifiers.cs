namespace PcActivityTracker.Core.Domain;

public interface IDomainId { Guid Value { get; } }

public readonly record struct ObservationId(Guid Value) : IDomainId { public static ObservationId New() => new(Guid.NewGuid()); }
public readonly record struct ActivityIntervalId(Guid Value) : IDomainId { public static ActivityIntervalId New() => new(Guid.NewGuid()); }
public readonly record struct ActivityGapId(Guid Value) : IDomainId { public static ActivityGapId New() => new(Guid.NewGuid()); }
public readonly record struct ClassificationId(Guid Value) : IDomainId { public static ClassificationId New() => new(Guid.NewGuid()); }
public readonly record struct ProjectId(Guid Value) : IDomainId { public static ProjectId New() => new(Guid.NewGuid()); }
public readonly record struct JobId(Guid Value) : IDomainId { public static JobId New() => new(Guid.NewGuid()); }
public readonly record struct CategoryId(Guid Value) : IDomainId { public static CategoryId New() => new(Guid.NewGuid()); }
public readonly record struct ExclusionRuleId(Guid Value) : IDomainId { public static ExclusionRuleId New() => new(Guid.NewGuid()); }

public static class DomainId
{
    public static void EnsureAssigned<T>(T id, string parameterName) where T : struct, IDomainId
    {
        if (id.Value == Guid.Empty) throw new ArgumentException("L'identificativo deve essere valorizzato.", parameterName);
    }
}
