using Calendar.Core.Models;

namespace Calendar.Core.Layout;

/// <summary>Placement of one timed event inside a day column of the Day/Week grid.</summary>
/// <param name="VisualStart">Start as drawn: clipped to the day.</param>
/// <param name="VisualEnd">End as drawn: clipped to the day and stretched to the minimum duration.</param>
/// <param name="Column">0-based sub-column inside the event's overlap cluster.</param>
/// <param name="ColumnSpan">Number of sub-columns the event may cover (expands right into free space).</param>
/// <param name="ColumnCount">Number of sub-columns in the event's overlap cluster.</param>
public sealed record DayLayoutItem(
    CalendarEvent Event,
    DateTimeOffset VisualStart,
    DateTimeOffset VisualEnd,
    int Column,
    int ColumnSpan,
    int ColumnCount)
{
    /// <summary>Left edge as a fraction (0..1) of the day column width.</summary>
    public double Left => (double)Column / ColumnCount;

    /// <summary>Width as a fraction (0..1) of the day column width.</summary>
    public double Width => (double)ColumnSpan / ColumnCount;
}

/// <summary>
/// Lays out overlapping timed events of one day side by side.
/// Events that overlap (directly or through a chain) form a cluster; each event takes the first
/// sub-column that is free at its start, then expands right over sub-columns that stay free
/// for its whole duration.
/// </summary>
public static class DayLayout
{
    /// <param name="events">Events to place; all-day events and events outside <paramref name="day"/> are skipped.</param>
    /// <param name="day">The day being drawn (local midnight to next local midnight).</param>
    /// <param name="minDuration">Shortest height an event is drawn with; overlap is computed on drawn extents.</param>
    public static IReadOnlyList<DayLayoutItem> Arrange(IEnumerable<CalendarEvent> events, DateRange day, TimeSpan minDuration)
    {
        if (minDuration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(minDuration), "Must be >= 0.");

        var slots = events
            .Where(e => !e.IsAllDay && IsOnDay(e, day))
            .Select(e => ToSlot(e, day, minDuration))
            .OrderBy(s => s.Start)
            .ThenByDescending(s => s.End)
            .ThenBy(s => s.Event.Id, StringComparer.Ordinal)
            .ToList();

        var result = new List<DayLayoutItem>(slots.Count);
        var cluster = new List<Slot>();
        var clusterEnd = DateTimeOffset.MinValue;

        foreach (var slot in slots)
        {
            if (cluster.Count > 0 && slot.Start >= clusterEnd)
            {
                Flush(cluster, result);
                cluster.Clear();
            }
            cluster.Add(slot);
            if (slot.End > clusterEnd)
                clusterEnd = slot.End;
        }
        Flush(cluster, result);
        return result;
    }

    private static bool IsOnDay(CalendarEvent e, DateRange day) =>
        e.Start < day.End && (e.End > day.Start || (e.End == e.Start && e.Start >= day.Start));

    private static Slot ToSlot(CalendarEvent e, DateRange day, TimeSpan minDuration)
    {
        var start = e.Start < day.Start ? day.Start : e.Start;
        var end = e.End > day.End ? day.End : e.End;
        if (end - start < minDuration)
        {
            end = start + minDuration;
            if (end > day.End)
            {
                end = day.End;
                start = end - minDuration < day.Start ? day.Start : end - minDuration;
            }
        }
        return new Slot(e, start, end);
    }

    private static void Flush(List<Slot> cluster, List<DayLayoutItem> result)
    {
        if (cluster.Count == 0)
            return;

        // Greedy column assignment: first column whose last event has ended.
        var columns = new List<List<Slot>>();
        var columnOf = new int[cluster.Count];
        for (var i = 0; i < cluster.Count; i++)
        {
            var slot = cluster[i];
            var col = columns.FindIndex(c => c[^1].End <= slot.Start);
            if (col < 0)
            {
                col = columns.Count;
                columns.Add([]);
            }
            columns[col].Add(slot);
            columnOf[i] = col;
        }

        for (var i = 0; i < cluster.Count; i++)
        {
            var slot = cluster[i];
            var span = 1;
            for (var c = columnOf[i] + 1; c < columns.Count; c++)
            {
                if (columns[c].Any(other => other.Start < slot.End && slot.Start < other.End))
                    break;
                span++;
            }
            result.Add(new DayLayoutItem(slot.Event, slot.Start, slot.End, columnOf[i], span, columns.Count));
        }
    }

    private readonly record struct Slot(CalendarEvent Event, DateTimeOffset Start, DateTimeOffset End);
}
