using Calendar.Core.Layout;
using Calendar.Core.Models;

namespace Calendar.Core.Tests.Layout;

public class AllDayLayoutTests
{
    private static readonly DateOnly Monday = new(2026, 11, 23);

    /// <summary>All-day event covering Monday+<paramref name="first"/> .. Monday+<paramref name="last"/> inclusive.</summary>
    private static CalendarEvent E(string id, int first, int last)
    {
        var (start, end) = CalendarEvent.AllDaySpan(Monday.AddDays(first), Monday.AddDays(last));
        return new CalendarEvent
        {
            AccountId = "a", CalendarId = "c", Id = id, Title = id, Start = start, End = end, IsAllDay = true,
        };
    }

    private static (Dictionary<string, AllDayLayoutItem> Items, int Rows) Arrange(params CalendarEvent[] events)
    {
        var result = AllDayLayout.Arrange(events, Monday, 7);
        return (result.Items.ToDictionary(i => i.Event.Id), result.RowCount);
    }

    [Fact]
    public void Empty_has_no_rows()
    {
        var (items, rows) = Arrange();
        Assert.Empty(items);
        Assert.Equal(0, rows);
    }

    [Fact]
    public void Single_day_event_takes_its_column()
    {
        var (items, rows) = Arrange(E("a", 2, 2));
        Assert.Equal(1, rows);
        Assert.Equal((2, 1, 0), (items["a"].FirstColumn, items["a"].ColumnSpan, items["a"].Row));
        Assert.False(items["a"].ContinuesBefore);
        Assert.False(items["a"].ContinuesAfter);
    }

    [Fact]
    public void Non_overlapping_events_share_a_row()
    {
        var (items, rows) = Arrange(E("a", 0, 1), E("b", 2, 4), E("c", 5, 6));
        Assert.Equal(1, rows);
        Assert.All(items.Values, i => Assert.Equal(0, i.Row));
    }

    [Fact]
    public void Overlapping_events_stack_and_free_rows_are_reused()
    {
        // a: Mon-Wed, b: Tue-Thu (overlaps a), c: Thu (overlaps b only) → c reuses row 0.
        var (items, rows) = Arrange(E("a", 0, 2), E("b", 1, 3), E("c", 3, 3));
        Assert.Equal(2, rows);
        Assert.Equal(0, items["a"].Row);
        Assert.Equal(1, items["b"].Row);
        Assert.Equal(0, items["c"].Row);
    }

    [Fact]
    public void Longer_event_gets_upper_row_when_starting_same_day()
    {
        var (items, _) = Arrange(E("short", 1, 1), E("long", 1, 4));
        Assert.Equal(0, items["long"].Row);
        Assert.Equal(1, items["short"].Row);
    }

    [Fact]
    public void Events_extending_outside_the_week_are_clipped_and_flagged()
    {
        var (items, _) = Arrange(E("from-last-week", -3, 1), E("into-next-week", 5, 9), E("whole", -1, 7));

        Assert.Equal((0, 2), (items["from-last-week"].FirstColumn, items["from-last-week"].ColumnSpan));
        Assert.True(items["from-last-week"].ContinuesBefore);
        Assert.False(items["from-last-week"].ContinuesAfter);

        Assert.Equal((5, 2), (items["into-next-week"].FirstColumn, items["into-next-week"].ColumnSpan));
        Assert.True(items["into-next-week"].ContinuesAfter);

        Assert.Equal((0, 7), (items["whole"].FirstColumn, items["whole"].ColumnSpan));
        Assert.True(items["whole"].ContinuesBefore);
        Assert.True(items["whole"].ContinuesAfter);
    }

    [Fact]
    public void Events_outside_the_range_and_timed_events_are_ignored()
    {
        var timed = new CalendarEvent
        {
            AccountId = "a", CalendarId = "c", Id = "timed", Title = "timed",
            Start = new DateTimeOffset(2026, 11, 24, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 11, 24, 10, 0, 0, TimeSpan.Zero),
        };
        var (items, _) = Arrange(E("last-sunday", -1, -1), E("next-monday", 7, 7), timed, E("in", 0, 0));
        Assert.Equal(["in"], items.Keys);
    }

    [Fact]
    public void Works_for_a_single_day_range()
    {
        var result = AllDayLayout.Arrange([E("a", 0, 3), E("b", 1, 1)], Monday.AddDays(1), 1);
        Assert.Equal(2, result.RowCount);
        Assert.All(result.Items, i => Assert.Equal((0, 1), (i.FirstColumn, i.ColumnSpan)));
    }
}
