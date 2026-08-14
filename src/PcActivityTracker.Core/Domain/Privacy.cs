namespace PcActivityTracker.Core.Domain;

public enum ExclusionKind { Application, WindowTitle, FilePath, BrowserDomain }

public sealed record ExclusionRule
{
    public ExclusionRule(ExclusionRuleId id, ExclusionKind kind, string pattern, bool isEnabled = true)
    {
        DomainId.EnsureAssigned(id, nameof(id));
        if (string.IsNullOrWhiteSpace(pattern)) throw new ArgumentException("Il pattern è obbligatorio.", nameof(pattern));
        Id = id; Kind = kind; Pattern = pattern.Trim(); IsEnabled = isEnabled;
    }
    public ExclusionRuleId Id { get; }
    public ExclusionKind Kind { get; }
    public string Pattern { get; }
    public bool IsEnabled { get; }
    public bool Matches(string candidate) => IsEnabled && candidate.Contains(Pattern, StringComparison.OrdinalIgnoreCase);
}
