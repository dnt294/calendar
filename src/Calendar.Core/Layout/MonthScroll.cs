namespace Calendar.Core.Layout;

/// <summary>
/// Geometry of the continuously scrolling Month view: a fixed list of week rows around today,
/// indexed from <see cref="Origin"/>, and which month the middle of the viewport is showing.
/// </summary>
public sealed class MonthScroll
{
    private readonly DayOfWeek _firstDayOfWeek;

    public MonthScroll(DateOnly today, DayOfWeek firstDayOfWeek, int yearsEachSide)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(yearsEachSide);
        _firstDayOfWeek = firstDayOfWeek;
        Origin = WeekNavigation.StartOfWeek(today.AddYears(-yearsEachSide), firstDayOfWeek);
        var last = WeekNavigation.StartOfWeek(today.AddYears(yearsEachSide), firstDayOfWeek);
        WeekCount = (last.DayNumber - Origin.DayNumber) / 7 + 1;
    }

    /// <summary>First day of row 0.</summary>
    public DateOnly Origin { get; }

    public int WeekCount { get; }

    public DateOnly WeekStart(int index) => Origin.AddDays(index * 7);

    /// <summary>Row containing <paramref name="date"/>, clamped to the list.</summary>
    public int WeekIndexOf(DateOnly date) =>
        Math.Clamp((WeekNavigation.StartOfWeek(date, _firstDayOfWeek).DayNumber - Origin.DayNumber) / 7, 0, WeekCount - 1);

    /// <summary>Row to put at the top to show <paramref name="month"/>: the week of its first day.</summary>
    public int TopWeekFor(DateOnly month) => WeekIndexOf(new DateOnly(month.Year, month.Month, 1));

    /// <summary>
    /// The month shown in the middle of the viewport (first day of it). <paramref name="topRow"/> is the
    /// fractional row index at the top edge; <paramref name="visibleRows"/> how many rows fit.
    /// </summary>
    public DateOnly MonthAtCenter(double topRow, double visibleRows)
    {
        var centerRow = Math.Clamp((int)Math.Floor(topRow + visibleRows / 2), 0, WeekCount - 1);
        // Mid-week day of the center row decides, so a row shared by two months goes to the one owning most of it.
        var day = WeekStart(centerRow).AddDays(3);
        return new DateOnly(day.Year, day.Month, 1);
    }
}
