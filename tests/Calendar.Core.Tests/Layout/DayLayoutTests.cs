using Calendar.Core.Layout;
using Calendar.Core.Models;

namespace Calendar.Core.Tests.Layout;

public class DayLayoutTests
{
    private static readonly TimeSpan Plus7 = TimeSpan.FromHours(7);
    private static readonly DateTimeOffset DayStart = new(2026, 11, 23, 0, 0, 0, Plus7);
    private static readonly DateRange Day = new(DayStart, DayStart.AddDays(1));
    private static readonly TimeSpan NoMin = TimeSpan.Zero;

    /// <summary>Event from <paramref name="from"/> to <paramref name="to"/>, given as "HH:mm" on the test day.</summary>
    private static CalendarEvent E(string id, string from, string to) => new()
    {
        AccountId = "a",
        CalendarId = "c",
        Id = id,
        Title = id,
        Start = DayStart + TimeSpan.Parse(from),
        End = DayStart + TimeSpan.Parse(to),
    };

    private static Dictionary<string, DayLayoutItem> Arrange(TimeSpan minDuration, params CalendarEvent[] events) =>
        DayLayout.Arrange(events, Day, minDuration).ToDictionary(i => i.Event.Id);

    private static void AssertSlot(DayLayoutItem item, int column, int span, int count)
    {
        Assert.Equal((column, span, count), (item.Column, item.ColumnSpan, item.ColumnCount));
    }

    [Fact]
    public void Single_event_takes_full_width()
    {
        var r = Arrange(NoMin, E("a", "09:00", "10:00"));
        AssertSlot(r["a"], 0, 1, 1);
        Assert.Equal(0, r["a"].Left);
        Assert.Equal(1, r["a"].Width);
    }

    [Fact]
    public void Adjacent_events_do_not_overlap()
    {
        var r = Arrange(NoMin, E("a", "09:00", "10:00"), E("b", "10:00", "11:00"));
        AssertSlot(r["a"], 0, 1, 1);
        AssertSlot(r["b"], 0, 1, 1);
    }

    [Fact]
    public void Two_overlapping_events_split_in_two_columns()
    {
        var r = Arrange(NoMin, E("a", "09:00", "10:30"), E("b", "10:00", "11:00"));
        AssertSlot(r["a"], 0, 1, 2);
        AssertSlot(r["b"], 1, 1, 2);
        Assert.Equal(0.5, r["b"].Left);
        Assert.Equal(0.5, r["b"].Width);
    }

    [Fact]
    public void Three_mutually_overlapping_events_use_three_columns()
    {
        var r = Arrange(NoMin, E("a", "09:00", "12:00"), E("b", "09:30", "11:00"), E("c", "10:00", "10:30"));
        AssertSlot(r["a"], 0, 1, 3);
        AssertSlot(r["b"], 1, 1, 3);
        AssertSlot(r["c"], 2, 1, 3);
    }

    [Fact]
    public void Nested_events_share_columns_with_free_slots_reused()
    {
        // a covers b and c; b and c do not overlap each other → 2 columns, c reuses b's column.
        var r = Arrange(NoMin, E("a", "09:00", "12:00"), E("b", "09:30", "10:00"), E("c", "11:00", "11:30"));
        AssertSlot(r["a"], 0, 1, 2);
        AssertSlot(r["b"], 1, 1, 2);
        AssertSlot(r["c"], 1, 1, 2);
    }

    [Fact]
    public void Chain_overlap_forms_one_cluster_and_events_expand_into_free_columns()
    {
        // a overlaps b, b overlaps c, a and c don't overlap.
        //   col0: a, c   col1: b
        var r = Arrange(NoMin, E("a", "09:00", "10:00"), E("b", "09:30", "10:30"), E("c", "10:00", "11:00"));
        AssertSlot(r["a"], 0, 1, 2);
        AssertSlot(r["b"], 1, 1, 2);
        AssertSlot(r["c"], 0, 1, 2);
    }

    [Fact]
    public void Event_expands_right_when_later_columns_are_free_during_its_time()
    {
        //   col0: a (9-12)   col1: b (9-10)   col2: c (9-10)   d (11-12) fits in col1 and may span col1..col2
        var r = Arrange(NoMin,
            E("a", "09:00", "12:00"), E("b", "09:00", "10:00"), E("c", "09:00", "10:00"), E("d", "11:00", "12:00"));
        AssertSlot(r["d"], 1, 2, 3);
        Assert.Equal(1.0 / 3, r["d"].Left, 6);
        Assert.Equal(2.0 / 3, r["d"].Width, 6);
    }

    [Fact]
    public void Separate_clusters_have_independent_column_counts()
    {
        var r = Arrange(NoMin,
            E("a", "09:00", "10:00"), E("b", "09:00", "10:00"),
            E("c", "14:00", "15:00"));
        AssertSlot(r["a"], 0, 1, 2);
        AssertSlot(r["b"], 1, 1, 2);
        AssertSlot(r["c"], 0, 1, 1);
    }

    [Fact]
    public void Same_start_longer_event_goes_first()
    {
        var r = Arrange(NoMin, E("short", "09:00", "09:30"), E("long", "09:00", "11:00"));
        AssertSlot(r["long"], 0, 1, 2);
        AssertSlot(r["short"], 1, 1, 2);
    }

    [Fact]
    public void Identical_events_are_ordered_by_id_for_stable_layout()
    {
        var r = Arrange(NoMin, E("b", "09:00", "10:00"), E("a", "09:00", "10:00"));
        AssertSlot(r["a"], 0, 1, 2);
        AssertSlot(r["b"], 1, 1, 2);
    }

    [Fact]
    public void Minimum_duration_makes_short_events_overlap_visually()
    {
        // 9:00-9:05 is drawn 30' tall, so it collides with 9:15-10:00.
        var r = Arrange(TimeSpan.FromMinutes(30), E("a", "09:00", "09:05"), E("b", "09:15", "10:00"));
        AssertSlot(r["a"], 0, 1, 2);
        AssertSlot(r["b"], 1, 1, 2);
        Assert.Equal(DayStart.AddHours(9).AddMinutes(30), r["a"].VisualEnd);
    }

    [Fact]
    public void Zero_length_event_is_kept_and_drawn_with_minimum_duration()
    {
        var r = Arrange(TimeSpan.FromMinutes(15), E("a", "09:00", "09:00"));
        Assert.Equal(TimeSpan.FromMinutes(15), r["a"].VisualEnd - r["a"].VisualStart);
    }

    [Fact]
    public void Events_crossing_midnight_are_clipped_to_the_day()
    {
        var before = E("before", "-02:00", "01:00");   // starts the previous day
        var after = E("after", "23:00", "1.02:00");     // ends the next day
        var r = Arrange(NoMin, before, after);

        Assert.Equal(DayStart, r["before"].VisualStart);
        Assert.Equal(DayStart.AddHours(1), r["before"].VisualEnd);
        Assert.Equal(DayStart.AddHours(23), r["after"].VisualStart);
        Assert.Equal(Day.End, r["after"].VisualEnd);
    }

    [Fact]
    public void Minimum_duration_near_end_of_day_shifts_up_instead_of_overflowing()
    {
        var r = Arrange(TimeSpan.FromMinutes(30), E("late", "23:50", "23:55"));
        Assert.Equal(Day.End, r["late"].VisualEnd);
        Assert.Equal(Day.End.AddMinutes(-30), r["late"].VisualStart);
    }

    [Fact]
    public void All_day_and_out_of_range_events_are_ignored()
    {
        var (s, e) = CalendarEvent.AllDaySpan(new DateOnly(2026, 11, 23), new DateOnly(2026, 11, 23));
        var allDay = E("allday", "00:00", "00:00") with { Start = s, End = e, IsAllDay = true };
        var r = Arrange(NoMin,
            allDay,
            E("yesterday", "-02:00", "-01:00"),
            E("ends-at-midnight", "-01:00", "00:00"),
            E("tomorrow", "1.00:00", "1.01:00"),
            E("today", "09:00", "10:00"));

        Assert.Equal(["today"], r.Keys);
    }

    [Fact]
    public void Times_are_compared_as_instants_across_offsets()
    {
        // 02:00Z == 09:00+07 → overlaps "a".
        var utc = E("utc", "09:30", "10:30") with
        {
            Start = new DateTimeOffset(2026, 11, 23, 2, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 11, 23, 3, 0, 0, TimeSpan.Zero),
        };
        var r = Arrange(NoMin, E("a", "09:30", "10:30"), utc);
        AssertSlot(r["utc"], 0, 1, 2);
        AssertSlot(r["a"], 1, 1, 2);
    }

    [Fact]
    public void Empty_input_returns_empty()
    {
        Assert.Empty(DayLayout.Arrange([], Day, NoMin));
    }
}
