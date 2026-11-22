using System.Globalization;

namespace Calendar.Core.Layout;

/// <summary>Date math and labels for navigating the Day/Week views.</summary>
public static class WeekNavigation
{
    public static DateOnly StartOfWeek(DateOnly date, DayOfWeek firstDayOfWeek)
    {
        var diff = ((int)date.DayOfWeek - (int)firstDayOfWeek + 7) % 7;
        return date.AddDays(-diff);
    }

    /// <summary>
    /// Header label for the visible days, e.g. "November 2026", "Nov – December 2026",
    /// "December 2026 – January 2027" (formats follow <paramref name="culture"/>).
    /// </summary>
    public static string FormatRangeLabel(DateOnly firstDay, int days, CultureInfo culture)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(days);
        var lastDay = firstDay.AddDays(days - 1);
        var yearMonth = culture.DateTimeFormat.YearMonthPattern;

        if (firstDay.Year == lastDay.Year && firstDay.Month == lastDay.Month)
            return firstDay.ToString(yearMonth, culture);
        if (firstDay.Year == lastDay.Year)
            return $"{firstDay.ToString("MMM", culture)} – {lastDay.ToString(yearMonth, culture)}";
        return $"{firstDay.ToString(yearMonth, culture)} – {lastDay.ToString(yearMonth, culture)}";
    }
}
