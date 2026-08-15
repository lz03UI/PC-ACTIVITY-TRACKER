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

public sealed record FileContext(string Path, string? DocumentType = null)
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
