using System.Globalization;

namespace Calendar.Core.Layout;

public enum CalendarViewMode
{
    Day,
    Week,
    Month,
}

/// <summary>Which days each view shows around a selected date, how ‹ › move, and the header label.</summary>
public static class CalendarNavigation
{
    /// <summary>
    /// Day: the date. Week: the week containing it. Month: whole weeks covering the date's month
    /// (4 to 6 rows, starting on <paramref name="firstDayOfWeek"/>).
    /// </summary>
    public static (DateOnly First, int Days) VisibleDays(CalendarViewMode mode, DateOnly date, DayOfWeek firstDayOfWeek)
    {
        switch (mode)
        {
            case CalendarViewMode.Day:
                return (date, 1);
            case CalendarViewMode.Week:
                return (WeekNavigation.StartOfWeek(date, firstDayOfWeek), 7);
            case CalendarViewMode.Month:
                var firstOfMonth = new DateOnly(date.Year, date.Month, 1);
                var lastOfMonth = firstOfMonth.AddMonths(1).AddDays(-1);
                var first = WeekNavigation.StartOfWeek(firstOfMonth, firstDayOfWeek);
                var weeks = (lastOfMonth.DayNumber - first.DayNumber) / 7 + 1;
                return (first, weeks * 7);
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }

    /// <summary>
    /// Days whose events must be loaded. Same as <see cref="VisibleDays"/> for Day and Week; the scrolling
    /// Month view also shows parts of the neighbouring months, so it loads the previous and next month too.
    /// </summary>
    public static (DateOnly First, int Days) LoadRange(CalendarViewMode mode, DateOnly date, DayOfWeek firstDayOfWeek)
    {
        if (mode != CalendarViewMode.Month)
            return VisibleDays(mode, date, firstDayOfWeek);
        var (first, _) = VisibleDays(mode, date.AddMonths(-1), firstDayOfWeek);
        var (lastStart, lastDays) = VisibleDays(mode, date.AddMonths(1), firstDayOfWeek);
        return (first, lastStart.DayNumber + lastDays - first.DayNumber);
    }

    /// <summary>The date ‹ (direction -1) or › (+1) moves to. Month steps keep the day, clamped to the month's end.</summary>
    public static DateOnly Step(CalendarViewMode mode, DateOnly date, int direction) => mode switch
    {
        CalendarViewMode.Day => date.AddDays(direction),
        CalendarViewMode.Week => date.AddDays(7 * direction),
        CalendarViewMode.Month => date.AddMonths(direction),
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    public static string Label(CalendarViewMode mode, DateOnly date, DayOfWeek firstDayOfWeek, CultureInfo culture) => mode switch
    {
        CalendarViewMode.Day => date.ToString(culture.DateTimeFormat.LongDatePattern, culture),
        CalendarViewMode.Week => WeekNavigation.FormatRangeLabel(WeekNavigation.StartOfWeek(date, firstDayOfWeek), 7, culture),
        CalendarViewMode.Month => date.ToString(culture.DateTimeFormat.YearMonthPattern, culture),
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };
}
