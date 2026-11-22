using Calendar.Core.Models;

namespace Calendar.Core.Layout;

/// <summary>Placement of one all-day event in the all-day band of the Day/Week grid.</summary>
/// <param name="FirstColumn">0-based day column where the bar starts (clipped to the range).</param>
/// <param name="ColumnSpan">Number of day columns the bar covers.</param>
/// <param name="Row">0-based row inside the band.</param>
/// <param name="ContinuesBefore">The event started before the first visible day.</param>
/// <param name="ContinuesAfter">The event goes on after the last visible day.</param>
public sealed record AllDayLayoutItem(
    CalendarEvent Event,
    int FirstColumn,
    int ColumnSpan,
    int Row,
    bool ContinuesBefore,
    bool ContinuesAfter);

public sealed record AllDayLayoutResult(IReadOnlyList<AllDayLayoutItem> Items, int RowCount);

/// <summary>
/// Stacks all-day events of a range of days into rows: each event takes the first row that is free
/// on all its days. Earlier and longer events get the upper rows.
/// </summary>
public static class AllDayLayout
{
    public static AllDayLayoutResult Arrange(IEnumerable<CalendarEvent> events, DateOnly firstDay, int days)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(days);
        var first = firstDay.DayNumber;
        var endExclusive = first + days;

        var bars = events
            .Where(e => e.IsAllDay)
            .Select(e => (Event: e, Start: e.StartDate.DayNumber, End: Math.Max(e.EndDateExclusive.DayNumber, e.StartDate.DayNumber + 1)))
            .Where(b => b.Start < endExclusive && b.End > first)
            .Select(b => (b.Event,
                Col: Math.Max(b.Start, first) - first,
                Span: Math.Min(b.End, endExclusive) - Math.Max(b.Start, first),
                Before: b.Start < first,
                After: b.End > endExclusive))
            .OrderBy(b => b.Col)
            .ThenByDescending(b => b.Span)
            .ThenBy(b => b.Event.Id, StringComparer.Ordinal)
            .ToList();

        // rowEnds[r] = first column that is free again in row r.
        var rowEnds = new List<int>();
        var items = new List<AllDayLayoutItem>(bars.Count);
        foreach (var b in bars)
        {
            var row = rowEnds.FindIndex(end => end <= b.Col);
            if (row < 0)
            {
                row = rowEnds.Count;
                rowEnds.Add(0);
            }
            rowEnds[row] = b.Col + b.Span;
            items.Add(new AllDayLayoutItem(b.Event, b.Col, b.Span, row, b.Before, b.After));
        }
        return new AllDayLayoutResult(items, rowEnds.Count);
    }
}
