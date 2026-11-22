using Calendar.Core.Models;

namespace Calendar.Core.Tests.Models;

public class DateRangeTests
{
    private static readonly TimeZoneInfo Hanoi = TimeZoneInfo.CreateCustomTimeZone("UTC+7", TimeSpan.FromHours(7), "UTC+7", "UTC+7");
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private static DateTimeOffset At(int hour, int minute = 0) => new(2026, 11, 23, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void Ctor_rejects_end_before_start()
    {
        Assert.Throws<ArgumentException>(() => new DateRange(At(10), At(9)));
    }

    [Fact]
    public void Empty_range_is_allowed()
    {
        var r = new DateRange(At(10), At(10));
        Assert.Equal(TimeSpan.Zero, r.Duration);
        Assert.False(r.Contains(At(10)));
    }

    [Fact]
    public void Contains_is_half_open()
    {
        var r = new DateRange(At(9), At(10));
        Assert.True(r.Contains(At(9)));
        Assert.True(r.Contains(At(9, 59)));
        Assert.False(r.Contains(At(10)));
    }

    [Theory]
    [InlineData(8, 9, false)]   // ends exactly when r starts
    [InlineData(8, 10, true)]
    [InlineData(9, 10, true)]   // inside
    [InlineData(10, 12, true)]
    [InlineData(11, 12, false)] // starts exactly when r ends
    [InlineData(8, 12, true)]   // covers r
    public void Overlaps(int startHour, int endHour, bool expected)
    {
        var r = new DateRange(At(9), At(11));
        var other = new DateRange(At(startHour), At(endHour));
        Assert.Equal(expected, r.Overlaps(other));
        Assert.Equal(expected, other.Overlaps(r));
    }

    [Fact]
    public void ForDays_uses_local_midnights()
    {
        var week = DateRange.ForDays(new DateOnly(2026, 11, 23), 7, Hanoi);
        Assert.Equal(new DateTimeOffset(2026, 11, 23, 0, 0, 0, TimeSpan.FromHours(7)), week.Start);
        Assert.Equal(new DateTimeOffset(2026, 11, 30, 0, 0, 0, TimeSpan.FromHours(7)), week.End);
        Assert.Equal(TimeSpan.FromDays(7), week.Duration);
    }

    [Fact]
    public void ForDays_across_dst_change_is_not_24h_per_day()
    {
        // US DST ended 2026-11-01 02:00 → that day is 25h long.
        var day = DateRange.ForDays(new DateOnly(2026, 11, 1), 1, NewYork);
        Assert.Equal(TimeSpan.FromHours(25), day.Duration);
        Assert.Equal(TimeSpan.FromHours(-4), day.Start.Offset);
        Assert.Equal(TimeSpan.FromHours(-5), day.End.Offset);
    }
}
