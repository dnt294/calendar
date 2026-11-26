namespace Calendar.App.Helpers;

/// <summary>Wall-clock times of this PC turned into <see cref="DateTimeOffset"/> (spec: show in the machine's time zone).</summary>
internal static class LocalTime
{
    public static DateTimeOffset At(DateOnly day, TimeSpan timeOfDay)
    {
        var local = day.ToDateTime(TimeOnly.MinValue).Add(timeOfDay);
        var zone = TimeZoneInfo.Local;
        // A wall time skipped by DST does not exist; move it to the first valid minute.
        while (zone.IsInvalidTime(local))
            local = local.AddMinutes(30);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    /// <summary>Next whole half hour from now, for a new event without a chosen slot.</summary>
    public static DateTimeOffset NextHalfHour()
    {
        var now = DateTimeOffset.Now;
        var minutes = now.Minute < 30 ? 30 - now.Minute : 60 - now.Minute;
        var rounded = now.AddMinutes(minutes);
        return At(DateOnly.FromDateTime(rounded.DateTime), new TimeSpan(rounded.Hour, rounded.Minute, 0));
    }
}
