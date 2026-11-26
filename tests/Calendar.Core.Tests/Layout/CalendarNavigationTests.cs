using System.Globalization;
using Calendar.Core.Layout;

namespace Calendar.Core.Tests.Layout;

public class CalendarNavigationTests
{
    private static readonly DateOnly Thu26Nov = new(2026, 11, 26);
    private const DayOfWeek Monday = DayOfWeek.Monday;

    [Fact]
    public void Day_view_shows_one_day()
    {
        Assert.Equal((Thu26Nov, 1), CalendarNavigation.VisibleDays(CalendarViewMode.Day, Thu26Nov, Monday));
    }

    [Fact]
    public void Week_view_shows_the_week_containing_the_date()
    {
        Assert.Equal((new DateOnly(2026, 11, 23), 7), CalendarNavigation.VisibleDays(CalendarViewMode.Week, Thu26Nov, Monday));
    }

    [Theory]
    // Nov 2026: Sun 1 .. Mon 30 → weeks from Mon Oct 26 to Sun Dec 6 = 6 weeks.
    [InlineData(2026, 11, "2026-10-26", 42)]
    // Feb 2027: Mon 1 .. Sun 28 → exactly 4 weeks.
    [InlineData(2027, 2, "2027-02-01", 28)]
    // Dec 2026: Tue 1 .. Thu 31 → Mon Nov 30 .. Sun Jan 3 = 5 weeks.
    [InlineData(2026, 12, "2026-11-30", 35)]
    public void Month_view_covers_whole_weeks_of_the_month(int year, int month, string first, int days)
    {
        var anyDayInMonth = new DateOnly(year, month, 15);
        Assert.Equal((DateOnly.Parse(first, CultureInfo.InvariantCulture), days),
            CalendarNavigation.VisibleDays(CalendarViewMode.Month, anyDayInMonth, Monday));
    }

    [Fact]
    public void Month_view_respects_first_day_of_week()
    {
        var (first, days) = CalendarNavigation.VisibleDays(CalendarViewMode.Month, Thu26Nov, DayOfWeek.Sunday);
        Assert.Equal(new DateOnly(2026, 11, 1), first); // Nov 1 2026 is a Sunday
        Assert.Equal(35, days);
    }

    [Theory]
    [InlineData(CalendarViewMode.Day, 1, "2026-11-27")]
    [InlineData(CalendarViewMode.Day, -1, "2026-11-25")]
    [InlineData(CalendarViewMode.Week, 1, "2026-12-03")]
    [InlineData(CalendarViewMode.Week, -1, "2026-11-19")]
    [InlineData(CalendarViewMode.Month, 1, "2026-12-26")]
    [InlineData(CalendarViewMode.Month, -1, "2026-10-26")]
    public void Step_moves_by_the_view_unit(CalendarViewMode mode, int direction, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected, CultureInfo.InvariantCulture),
            CalendarNavigation.Step(mode, Thu26Nov, direction));
    }

    [Fact]
    public void Month_step_clamps_to_last_day()
    {
        Assert.Equal(new DateOnly(2027, 2, 28), CalendarNavigation.Step(CalendarViewMode.Month, new DateOnly(2027, 1, 31), 1));
    }

    [Fact]
    public void Labels_per_view()
    {
        var c = CultureInfo.InvariantCulture;
        Assert.Equal("Thursday, 26 November 2026", CalendarNavigation.Label(CalendarViewMode.Day, Thu26Nov, Monday, c));
        Assert.Equal("2026 November", CalendarNavigation.Label(CalendarViewMode.Week, Thu26Nov, Monday, c));
        Assert.Equal("Nov – 2026 December", CalendarNavigation.Label(CalendarViewMode.Week, new DateOnly(2026, 12, 2), Monday, c));
        // Month label names the month itself, even though the grid starts in October.
        Assert.Equal("2026 November", CalendarNavigation.Label(CalendarViewMode.Month, Thu26Nov, Monday, c));
    }
}
