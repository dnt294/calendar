using Calendar.Core.Models;

namespace Calendar.Core.Layout;

/// <summary>One drawn piece of an event in a week row of the month grid.</summary>
/// <param name="FirstColumn">0-based weekday column where this piece starts.</param>
/// <param name="ColumnSpan">Number of day columns this piece covers.</param>
/// <param name="Lane">0-based line inside the day cells.</param>
/// <param name="IsBlock">Drawn as a filled bar (all-day or multi-day); otherwise "● 9:00 Title".</param>
/// <param name="ContinuesBefore">The event goes on to the left of this piece (earlier week or hidden day).</param>
/// <param name="ContinuesAfter">The event goes on to the right of this piece.</param>
public sealed record MonthBar(
    CalendarEvent Event,
    int FirstColumn,
    int ColumnSpan,
    int Lane,
    bool IsBlock,
    bool ContinuesBefore,
    bool ContinuesAfter);

/// <param name="Bars">Visible pieces only.</param>
/// <param name="HiddenCounts">Per weekday column: events not drawn ("+N").</param>
/// <param name="EventsByDay">Per weekday column: every event on that day, in display order.</param>
public sealed record MonthWeek(
    DateOnly Start,
    IReadOnlyList<MonthBar> Bars,
    IReadOnlyList<int> HiddenCounts,
    IReadOnlyList<IReadOnlyList<CalendarEvent>> EventsByDay);

/// <summary>
/// Lays out events in the month grid: per week row, all-day and multi-day events become bars on the
/// upper lanes, then single-day timed events by start time. A day showing more than
/// <c>maxLanes</c> events keeps one line for "+N".
/// </summary>
public static class MonthLayout
{
    private const int DaysPerWeek = 7;

    public static IReadOnlyList<MonthWeek> Arrange(IEnumerable<CalendarEvent> events, DateOnly firstDay, int weeks,
        int maxLanes, TimeZoneInfo zone)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(weeks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLanes);

        var spans = events.Select(e => ToSpan(e, zone)).ToList();
        var result = new List<MonthWeek>(weeks);
        for (var w = 0; w < weeks; w++)
            result.Add(ArrangeWeek(spans, firstDay.AddDays(w * DaysPerWeek), maxLanes));
        return result;
    }

    private static MonthWeek ArrangeWeek(List<Span> spans, DateOnly weekStart, int maxLanes)
    {
        var first = weekStart.DayNumber;
        var end = first + DaysPerWeek;

        var items = spans
            .Where(s => s.Start < end && s.End > first)
            .Select(s => new Item(s,
                Col: Math.Max(s.Start, first) - first,
                Span: Math.Min(s.End, end) - Math.Max(s.Start, first)))
            .OrderBy(i => i.Col)
            .ThenByDescending(i => i.Source.IsBlock)
            .ThenByDescending(i => i.Span)
            .ThenBy(i => i.Source.Event.Start)
            .ThenBy(i => i.Source.Event.Id, StringComparer.Ordinal)
            .ToList();

        // Greedy lanes: the lowest lane free on every day of the item.
        var occupied = new List<bool[]>();
        var lanes = new int[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            var lane = occupied.FindIndex(row => !row.AsSpan(it.Col, it.Span).Contains(true));
            if (lane < 0)
            {
                lane = occupied.Count;
                occupied.Add(new bool[DaysPerWeek]);
            }
            occupied[lane].AsSpan(it.Col, it.Span).Fill(true);
            lanes[i] = lane;
        }

        // Per day: how many lanes may be drawn (one line kept for "+N" when it overflows).
        var counts = new int[DaysPerWeek];
        foreach (var it in items)
            for (var c = it.Col; c < it.Col + it.Span; c++)
                counts[c]++;
        var limits = counts.Select(n => n <= maxLanes ? maxLanes : maxLanes - 1).ToArray();

        var bars = new List<MonthBar>();
        var hidden = new int[DaysPerWeek];
        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            var lane = lanes[i];
            var c = it.Col;
            while (c < it.Col + it.Span)
            {
                if (lane >= limits[c])
                {
                    hidden[c]++;
                    c++;
                    continue;
                }
                var segStart = c;
                while (c < it.Col + it.Span && lane < limits[c])
                    c++;
                bars.Add(new MonthBar(it.Source.Event, segStart, c - segStart, lane, it.Source.IsBlock,
                    ContinuesBefore: segStart > it.Col || it.Source.Start < first,
                    ContinuesAfter: c < it.Col + it.Span || it.Source.End > end));
            }
        }

        var byDay = new IReadOnlyList<CalendarEvent>[DaysPerWeek];
        for (var d = 0; d < DaysPerWeek; d++)
            byDay[d] = items.Where(it => it.Col <= d && d < it.Col + it.Span).Select(it => it.Source.Event).ToList();

        return new MonthWeek(weekStart, bars, hidden, byDay);
    }

    /// <summary>Days covered by the event in <paramref name="zone"/> as [Start, End) day numbers.</summary>
    private static Span ToSpan(CalendarEvent e, TimeZoneInfo zone)
    {
        int start, end;
        if (e.IsAllDay)
        {
            start = e.StartDate.DayNumber;
            end = Math.Max(e.EndDateExclusive.DayNumber, start + 1);
        }
        else
        {
            var localStart = TimeZoneInfo.ConvertTime(e.Start, zone).DateTime;
            var localEnd = TimeZoneInfo.ConvertTime(e.End, zone).DateTime;
            start = DateOnly.FromDateTime(localStart).DayNumber;
            // An event ending exactly at midnight does not touch the next day.
            end = DateOnly.FromDateTime(localEnd).DayNumber + (localEnd.TimeOfDay == TimeSpan.Zero ? 0 : 1);
            end = Math.Max(end, start + 1);
        }
        return new Span(e, start, end, IsBlock: e.IsAllDay || end - start > 1);
    }

    private sealed record Span(CalendarEvent Event, int Start, int End, bool IsBlock);

    private sealed record Item(Span Source, int Col, int Span);
}
