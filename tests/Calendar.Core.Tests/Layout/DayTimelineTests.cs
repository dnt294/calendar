using Calendar.Core.Layout;

namespace Calendar.Core.Tests.Layout;

public class DayTimelineTests
{
    private static readonly TimeZoneInfo Hanoi = TimeZoneInfo.CreateCustomTimeZone("UTC+7", TimeSpan.FromHours(7), "UTC+7", "UTC+7");
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private static readonly DateOnly Day = new(2026, 11, 23);

    [Fact]
    public void Converts_to_local_wall_clock()
    {
        // 02:30Z = 09:30 in UTC+7.
        var t = new DateTimeOffset(2026, 11, 23, 2, 30, 0, TimeSpan.Zero);
        Assert.Equal(9.5 * 60, DayTimeline.MinutesFromMidnight(t, Day, Hanoi));
    }

    [Fact]
    public void Clamps_to_the_day()
    {
        var before = new DateTimeOffset(2026, 11, 22, 23, 0, 0, TimeSpan.FromHours(7));
        var after = new DateTimeOffset(2026, 11, 24, 1, 0, 0, TimeSpan.FromHours(7));
        var nextMidnight = new DateTimeOffset(2026, 11, 24, 0, 0, 0, TimeSpan.FromHours(7));
        Assert.Equal(0, DayTimeline.MinutesFromMidnight(before, Day, Hanoi));
        Assert.Equal(DayTimeline.MinutesPerDay, DayTimeline.MinutesFromMidnight(after, Day, Hanoi));
        Assert.Equal(DayTimeline.MinutesPerDay, DayTimeline.MinutesFromMidnight(nextMidnight, Day, Hanoi));
    }

    [Fact]
    public void Dst_day_uses_wall_clock_not_elapsed_time()
    {
        // 2026-11-01 New York: 01:00-02:00 happens twice. 03:00 local is 4h elapsed but drawn at 3h.
        var day = new DateOnly(2026, 11, 1);
        var threeAm = new DateTimeOffset(2026, 11, 1, 3, 0, 0, TimeSpan.FromHours(-5));
        Assert.Equal(180, DayTimeline.MinutesFromMidnight(threeAm, day, NewYork));
    }
}
