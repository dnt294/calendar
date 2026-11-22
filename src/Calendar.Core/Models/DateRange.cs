namespace Calendar.Core.Models;

/// <summary>Half-open time range <c>[Start, End)</c>.</summary>
public readonly record struct DateRange
{
    public DateRange(DateTimeOffset start, DateTimeOffset end)
    {
        if (end < start)
            throw new ArgumentException("End must not be before Start.", nameof(end));
        Start = start;
        End = end;
    }

    public DateTimeOffset Start { get; }
    public DateTimeOffset End { get; }

    public TimeSpan Duration => End - Start;

    public bool Contains(DateTimeOffset instant) => instant >= Start && instant < End;

    /// <summary>True when the ranges share any instant. Touching ranges (a.End == b.Start) do not overlap.</summary>
    public bool Overlaps(DateRange other) => Start < other.End && other.Start < End;

    /// <summary>The local days <paramref name="firstDay"/> .. <paramref name="firstDay"/> + <paramref name="days"/> in <paramref name="zone"/>.</summary>
    public static DateRange ForDays(DateOnly firstDay, int days, TimeZoneInfo zone)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(days);
        return new DateRange(StartOfDay(firstDay, zone), StartOfDay(firstDay.AddDays(days), zone));
    }

    private static DateTimeOffset StartOfDay(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // Midnight can fall in a DST gap in some zones; move forward until it is a valid time.
        while (zone.IsInvalidTime(local))
            local = local.AddMinutes(30);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}
