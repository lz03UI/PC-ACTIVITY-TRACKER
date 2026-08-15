namespace PcActivityTracker.Core.Domain;

public enum ObservationSource { ForegroundApplication, FileDocument, Browser }
public enum ActivityState { Active, Idle, Locked, Suspended, Paused, Private }
public enum DiscontinuityReason { None, ClockChanged, TimeZoneChanged, Sleep, Lock, CollectorRestart, SignalLoss }

public sealed record ApplicationIdentity
{
    public ApplicationIdentity(string processName, string? executablePath = null)
    {
        if (string.IsNullOrWhiteSpace(processName)) throw new ArgumentException("Il processo è obbligatorio.", nameof(processName));
        ProcessName = processName.Trim();
        ExecutablePath = string.IsNullOrWhiteSpace(executablePath) ? null : executablePath;
    }
    public string ProcessName { get; }
    public string? ExecutablePath { get; }
}

public enum DocumentResolutionPrecision { FullPath, FileNameOnly, Unresolved }
public enum DocumentProvenance { DirectlyObserved, Derived, Unresolved }
public enum DocumentResolutionFailure { None, UnsupportedApplication, AccessDenied, TimedOut, ResolverError, ApplicationTerminated, ApiUnavailable }

public sealed record DocumentResolutionResult
{
    private DocumentResolutionResult(DocumentResolutionPrecision precision, DocumentProvenance provenance,
        string? value, string? resolverId, DocumentResolutionFailure failure)
    {
        if (precision == DocumentResolutionPrecision.Unresolved && value is not null)
            throw new ArgumentException("Un risultato non risolto non può contenere un riferimento documento.", nameof(value));
        if (precision != DocumentResolutionPrecision.Unresolved && string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Un risultato risolto richiede un riferimento documento.", nameof(value));
        if (precision == DocumentResolutionPrecision.Unresolved && provenance != DocumentProvenance.Unresolved)
            throw new ArgumentException("Un risultato non risolto richiede provenance Unresolved.", nameof(provenance));
        if (precision != DocumentResolutionPrecision.Unresolved && provenance == DocumentProvenance.Unresolved)
            throw new ArgumentException("Un risultato risolto richiede provenance osservata o derivata.", nameof(provenance));
        if (precision == DocumentResolutionPrecision.FileNameOnly && value is not null && (value.Contains('/') || value.Contains('\\')))
            throw new ArgumentException("FileNameOnly non può contenere componenti di percorso.", nameof(value));
        if (precision != DocumentResolutionPrecision.Unresolved && failure != DocumentResolutionFailure.None)
            throw new ArgumentException("Un risultato risolto non può contenere un errore.", nameof(failure));
        Precision = precision; Provenance = provenance; Value = value?.Trim(); ResolverId = resolverId; Failure = failure;
    }

    public DocumentResolutionPrecision Precision { get; }
    public DocumentProvenance Provenance { get; }
    public string? Value { get; }
    public string? ResolverId { get; }
    public DocumentResolutionFailure Failure { get; }
    public static DocumentResolutionResult FullPath(string path, DocumentProvenance provenance, string resolverId) =>
        new(DocumentResolutionPrecision.FullPath, provenance, path, resolverId, DocumentResolutionFailure.None);
    public static DocumentResolutionResult FileNameOnly(string fileName, DocumentProvenance provenance, string resolverId) =>
        new(DocumentResolutionPrecision.FileNameOnly, provenance, fileName, resolverId, DocumentResolutionFailure.None);
    public static DocumentResolutionResult Unresolved(DocumentResolutionFailure failure = DocumentResolutionFailure.None, string? resolverId = null) =>
        new(DocumentResolutionPrecision.Unresolved, DocumentProvenance.Unresolved, null, resolverId, failure);
}

public sealed record FileContext(string Path, string? DocumentType = null,
    DocumentResolutionPrecision Precision = DocumentResolutionPrecision.FullPath,
    DocumentProvenance Provenance = DocumentProvenance.DirectlyObserved)
{
    public string Path { get; } = string.IsNullOrWhiteSpace(Path) ? throw new ArgumentException("Il percorso è obbligatorio.", nameof(Path)) : Path;
}

public sealed record BrowserContext
{
    public BrowserContext(string domain, string? path = null)
    {
        if (string.IsNullOrWhiteSpace(domain)) throw new ArgumentException("Il dominio è obbligatorio.", nameof(domain));
        if (domain.Contains('/') || domain.Contains('?') || domain.Contains('#')) throw new ArgumentException("Il dominio non è valido.", nameof(domain));
        if (path?.Contains('?') == true || path?.Contains('#') == true) throw new ArgumentException("Query string e fragment non sono consentiti.", nameof(path));
        Domain = domain.ToLowerInvariant();
        Path = string.IsNullOrWhiteSpace(path) ? null : path;
    }
    public string Domain { get; }
    public string? Path { get; }
}

public sealed record RawObservation
{
    public RawObservation(ObservationId id, ObservationSource source, UtcInstant observedAt, LocalTimeContext localTime,
        ActivityState state, ApplicationIdentity application, FileContext? file = null, BrowserContext? browser = null)
    {
        DomainId.EnsureAssigned(id, nameof(id));
        if (state == ActivityState.Private) throw new ArgumentException("L'attività privata deve essere scartata prima di creare un'osservazione grezza.", nameof(state));
        if (source == ObservationSource.FileDocument && file is null) throw new ArgumentException("Il contesto file è richiesto.", nameof(file));
        if (source == ObservationSource.Browser && browser is null) throw new ArgumentException("Il contesto browser è richiesto.", nameof(browser));
        Id = id; Source = source; ObservedAt = observedAt; LocalTime = localTime; State = state;
        Application = application ?? throw new ArgumentNullException(nameof(application)); File = file; Browser = browser;
    }
    public ObservationId Id { get; }
    public ObservationSource Source { get; }
    public UtcInstant ObservedAt { get; }
    public LocalTimeContext LocalTime { get; }
    public ActivityState State { get; }
    public ApplicationIdentity Application { get; }
    public FileContext? File { get; }
    public BrowserContext? Browser { get; }
}

public sealed record ActivityInterval
{
    public ActivityInterval(ActivityIntervalId id, ObservationId? observationId, TimeRange period, ActivityState state,
        DiscontinuityReason endReason = DiscontinuityReason.None, TimeSpan? elapsed = null, bool isElapsedMonotonic = false)
    {
        DomainId.EnsureAssigned(id, nameof(id));
        if (observationId is { } assignedObservationId) DomainId.EnsureAssigned(assignedObservationId, nameof(observationId));
        if (state == ActivityState.Private) throw new ArgumentException("Un intervallo privato deve essere rappresentato come gap privo di contenuto.", nameof(state));
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        Id = id; ObservationId = observationId; Period = period; State = state; EndReason = endReason;
        Elapsed = elapsed ?? period.Duration; IsElapsedMonotonic = isElapsedMonotonic;
    }
    public ActivityIntervalId Id { get; }
    public ObservationId? ObservationId { get; }
    public TimeRange Period { get; }
    public ActivityState State { get; }
    public DiscontinuityReason EndReason { get; }
    public TimeSpan Elapsed { get; }
    public bool IsElapsedMonotonic { get; }
}

/// <summary>Periodo senza contenuto identificativo, usato anche per la modalità privata.</summary>
public sealed record ActivityGap
{
    public ActivityGap(ActivityGapId id, TimeRange period, ActivityState state, TimeSpan? elapsed = null, bool isElapsedMonotonic = false)
    {
        DomainId.EnsureAssigned(id, nameof(id));
        if (state is not (ActivityState.Private or ActivityState.Idle or ActivityState.Locked or ActivityState.Suspended or ActivityState.Paused))
            throw new ArgumentException("Lo stato non rappresenta un gap temporale.", nameof(state));
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        Id = id;
        Period = period;
        State = state;
        Elapsed = elapsed ?? period.Duration;
        IsElapsedMonotonic = isElapsedMonotonic;
    }

    public ActivityGapId Id { get; }
    public TimeRange Period { get; }
    public ActivityState State { get; }
    public TimeSpan Elapsed { get; }
    public bool IsElapsedMonotonic { get; }
}
