using System.Globalization;
using Calendar.Core.Layout;

namespace Calendar.Core.Tests.Layout;

public class WeekNavigationTests
{
    [Theory]
    [InlineData("2026-11-23", "2026-11-23")] // Monday
    [InlineData("2026-11-25", "2026-11-23")] // Wednesday
    [InlineData("2026-11-29", "2026-11-23")] // Sunday belongs to the week starting the Monday before
    [InlineData("2026-12-01", "2026-11-30")] // across month boundary
    public void StartOfWeek_monday_first(string date, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected, CultureInfo.InvariantCulture),
            WeekNavigation.StartOfWeek(DateOnly.Parse(date, CultureInfo.InvariantCulture), DayOfWeek.Monday));
    }

    [Theory]
    [InlineData("2026-11-22", "2026-11-22")] // Sunday
    [InlineData("2026-11-28", "2026-11-22")] // Saturday
    public void StartOfWeek_sunday_first(string date, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected, CultureInfo.InvariantCulture),
            WeekNavigation.StartOfWeek(DateOnly.Parse(date, CultureInfo.InvariantCulture), DayOfWeek.Sunday));
    }

    [Fact]
    public void Label_within_one_month()
    {
        var label = WeekNavigation.FormatRangeLabel(new DateOnly(2026, 11, 16), 7, CultureInfo.InvariantCulture);
        Assert.Equal("2026 November", label);
    }

    [Fact]
    public void Label_across_two_months_of_same_year()
    {
        var label = WeekNavigation.FormatRangeLabel(new DateOnly(2026, 11, 30), 7, CultureInfo.InvariantCulture);
        Assert.Equal("Nov – 2026 December", label);
    }

    [Fact]
    public void Label_across_years()
    {
        var label = WeekNavigation.FormatRangeLabel(new DateOnly(2026, 12, 28), 7, CultureInfo.InvariantCulture);
        Assert.Equal("2026 December – 2027 January", label);
    }

    [Fact]
    public void Label_uses_culture()
    {
        var label = WeekNavigation.FormatRangeLabel(new DateOnly(2026, 11, 16), 7, new CultureInfo("vi-VN"));
        Assert.Contains("11", label);
        Assert.Contains("2026", label);
    }

    [Fact]
    public void Label_for_single_day_uses_last_day_of_range_only()
    {
        // A 1-day range ending on the 30th must not mention the next month.
        var label = WeekNavigation.FormatRangeLabel(new DateOnly(2026, 11, 30), 1, CultureInfo.InvariantCulture);
        Assert.Equal("2026 November", label);
    }
}
