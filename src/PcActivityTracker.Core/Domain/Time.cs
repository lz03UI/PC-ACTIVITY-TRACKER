namespace PcActivityTracker.Core.Domain;

public readonly record struct UtcInstant
{
    public UtcInstant(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero) throw new ArgumentException("L'istante deve essere espresso in UTC.", nameof(value));
        Value = value;
    }

    public DateTimeOffset Value { get; }
    public static UtcInstant FromUtc(DateTimeOffset value) => new(value);
    public static UtcInstant Now(TimeProvider timeProvider) => new(timeProvider.GetUtcNow());
}

/// <summary>Intervallo UTC semiaperto [Start, End).</summary>
public readonly record struct TimeRange
{
    public TimeRange(UtcInstant start, UtcInstant end)
    {
        if (end.Value < start.Value) throw new ArgumentOutOfRangeException(nameof(end), "La fine non può precedere l'inizio.");
        Start = start;
        End = end;
    }

    public UtcInstant Start { get; }
    public UtcInstant End { get; }
    public TimeSpan Duration => End.Value - Start.Value;
    public bool Overlaps(TimeRange other) => Start.Value < other.End.Value && other.Start.Value < End.Value;
}

public sealed record LocalTimeContext
{
    public LocalTimeContext(string timeZoneId, TimeSpan observedUtcOffset)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)) throw new ArgumentException("Il fuso orario è obbligatorio.", nameof(timeZoneId));
        if (observedUtcOffset < TimeSpan.FromHours(-14) || observedUtcOffset > TimeSpan.FromHours(14))
            throw new ArgumentOutOfRangeException(nameof(observedUtcOffset));
        TimeZoneId = timeZoneId;
        ObservedUtcOffset = observedUtcOffset;
    }
    public string TimeZoneId { get; }
    public TimeSpan ObservedUtcOffset { get; }
}

public readonly record struct MonotonicTimestamp(long Value)
{
    public static TimeSpan Elapsed(MonotonicTimestamp start, MonotonicTimestamp end, long frequency)
    {
        if (frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency));
        if (end.Value < start.Value) throw new ArgumentOutOfRangeException(nameof(end));
        return TimeSpan.FromSeconds((double)(end.Value - start.Value) / frequency);
    }
}
