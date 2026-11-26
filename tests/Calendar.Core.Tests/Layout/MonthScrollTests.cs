using Calendar.Core.Layout;

namespace Calendar.Core.Tests.Layout;

public class MonthScrollTests
{
    private const DayOfWeek Monday = DayOfWeek.Monday;
    private static readonly MonthScroll Scroll = new(new DateOnly(2026, 11, 26), Monday, yearsEachSide: 10);

    [Fact]
    public void Origin_is_a_week_start_years_before_today()
    {
        Assert.Equal(DayOfWeek.Monday, Scroll.Origin.DayOfWeek);
        Assert.True(Scroll.Origin <= new DateOnly(2016, 11, 26));
        Assert.True(Scroll.Origin > new DateOnly(2016, 11, 18));
        Assert.InRange(Scroll.WeekCount, 52 * 20, 53 * 20 + 1);
    }

    [Fact]
    public void Week_index_round_trips()
    {
        var index = Scroll.WeekIndexOf(new DateOnly(2026, 11, 26));
        Assert.Equal(new DateOnly(2026, 11, 23), Scroll.WeekStart(index));
        Assert.Equal(index, Scroll.WeekIndexOf(new DateOnly(2026, 11, 23)));
        Assert.Equal(index, Scroll.WeekIndexOf(new DateOnly(2026, 11, 29)));
        Assert.Equal(index + 1, Scroll.WeekIndexOf(new DateOnly(2026, 11, 30)));
    }

    [Fact]
    public void Dates_outside_the_range_are_clamped()
    {
        Assert.Equal(0, Scroll.WeekIndexOf(new DateOnly(1990, 1, 1)));
        Assert.Equal(Scroll.WeekCount - 1, Scroll.WeekIndexOf(new DateOnly(2090, 1, 1)));
    }

    [Fact]
    public void Scrolling_to_a_month_puts_its_first_week_on_top()
    {
        // Nov 1 2026 is a Sunday → its week starts Mon Oct 26.
        Assert.Equal(new DateOnly(2026, 10, 26), Scroll.WeekStart(Scroll.TopWeekFor(new DateOnly(2026, 11, 1))));
    }

    [Theory]
    // Top of the viewport at the first week of the month, 6 rows visible → center shows that month.
    [InlineData("2026-11-01", 0.0, 6.0, 2026, 11)]
    [InlineData("2026-12-01", 0.0, 6.0, 2026, 12)]
    [InlineData("2027-02-01", 0.0, 6.0, 2027, 2)]
    // Scrolled four rows further, the center is in the next month.
    [InlineData("2026-11-01", 4.0, 6.0, 2026, 12)]
    public void Month_at_center(string topMonth, double extraRows, double visibleRows, int year, int month)
    {
        var top = Scroll.TopWeekFor(DateOnly.Parse(topMonth, System.Globalization.CultureInfo.InvariantCulture)) + extraRows;
        Assert.Equal(new DateOnly(year, month, 1), Scroll.MonthAtCenter(top, visibleRows));
    }

    [Fact]
    public void Round_trip_top_week_then_center_is_stable_for_every_month_of_a_year()
    {
        for (var m = 1; m <= 12; m++)
        {
            var month = new DateOnly(2027, m, 1);
            Assert.Equal(month, Scroll.MonthAtCenter(Scroll.TopWeekFor(month), 6));
        }
    }

    [Fact]
    public void Month_load_range_covers_the_previous_and_next_month()
    {
        var (first, days) = CalendarNavigation.LoadRange(CalendarViewMode.Month, new DateOnly(2026, 11, 26), Monday);
        Assert.Equal(new DateOnly(2026, 9, 28), first);                     // week of Oct 1
        Assert.Equal(new DateOnly(2027, 1, 4), first.AddDays(days));        // after the week of Dec 31
    }

    [Theory]
    [InlineData(CalendarViewMode.Day)]
    [InlineData(CalendarViewMode.Week)]
    public void Day_and_week_load_exactly_what_they_show(CalendarViewMode mode)
    {
        var date = new DateOnly(2026, 11, 26);
        Assert.Equal(CalendarNavigation.VisibleDays(mode, date, Monday), CalendarNavigation.LoadRange(mode, date, Monday));
    }
}
