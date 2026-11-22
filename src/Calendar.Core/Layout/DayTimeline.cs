namespace Calendar.Core.Layout;

/// <summary>Maps instants to the vertical axis of a day column (wall-clock time, 0..24h).</summary>
public static class DayTimeline
{
    public const double MinutesPerDay = 24 * 60;

    /// <summary>
    /// Wall-clock minutes since local midnight of <paramref name="day"/> in <paramref name="zone"/>,
    /// clamped to [0, 1440]. On DST days the axis stays 24h long: the repeated hour is drawn twice
    /// over the same rows and the skipped hour is empty, like any wall clock.
    /// </summary>
    public static double MinutesFromMidnight(DateTimeOffset instant, DateOnly day, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(instant, zone).DateTime;
        var minutes = (local - day.ToDateTime(TimeOnly.MinValue)).TotalMinutes;
        return Math.Clamp(minutes, 0, MinutesPerDay);
    }
}
