using Calendar.Core.Layout;
using Calendar.Core.Models;

namespace Calendar.Core.Tests.Layout;

public class MonthLayoutTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("UTC+7", TimeSpan.FromHours(7), "UTC+7", "UTC+7");
    private static readonly DateOnly Mon = new(2026, 11, 23);

    /// <summary>Timed event: day offset from Monday + "HH:mm", duration.</summary>
    private static CalendarEvent T(string id, int day, string at, TimeSpan duration)
    {
        var local = Mon.AddDays(day).ToDateTime(TimeOnly.Parse(at, System.Globalization.CultureInfo.InvariantCulture));
        var start = new DateTimeOffset(local, TimeSpan.FromHours(7));
        return new CalendarEvent { AccountId = "a", CalendarId = "c", Id = id, Title = id, Start = start, End = start + duration };
    }

    /// <summary>All-day event over day offsets first..last inclusive.</summary>
    private static CalendarEvent A(string id, int first, int last)
    {
        var (s, e) = CalendarEvent.AllDaySpan(Mon.AddDays(first), Mon.AddDays(last));
        return new CalendarEvent { AccountId = "a", CalendarId = "c", Id = id, Title = id, Start = s, End = e, IsAllDay = true };
    }

    private static IReadOnlyList<MonthWeek> Arrange(int weeks, int maxLanes, params CalendarEvent[] events) =>
        MonthLayout.Arrange(events, Mon, weeks, maxLanes, Zone);

    private static MonthBar Bar(MonthWeek week, string id) => Assert.Single(week.Bars, b => b.Event.Id == id);

    [Fact]
    public void Weeks_start_seven_days_apart()
    {
        var weeks = Arrange(3, 4);
        Assert.Equal([Mon, Mon.AddDays(7), Mon.AddDays(14)], weeks.Select(w => w.Start));
        Assert.All(weeks, w => Assert.Equal(7, w.HiddenCounts.Count));
    }

    [Fact]
    public void Timed_event_is_a_one_day_item()
    {
        var bar = Bar(Arrange(1, 4, T("standup", 2, "09:00", TimeSpan.FromMinutes(15)))[0], "standup");
        Assert.Equal((2, 1, 0, false), (bar.FirstColumn, bar.ColumnSpan, bar.Lane, bar.IsBlock));
    }

    [Fact]
    public void Timed_event_over_midnight_becomes_a_block_spanning_both_days()
    {
        var bar = Bar(Arrange(1, 4, T("late", 4, "22:00", TimeSpan.FromHours(3)))[0], "late");
        Assert.Equal((4, 2, true), (bar.FirstColumn, bar.ColumnSpan, bar.IsBlock));
    }

    [Fact]
    public void Timed_event_ending_exactly_at_midnight_stays_on_one_day()
    {
        var bar = Bar(Arrange(1, 4, T("evening", 1, "22:00", TimeSpan.FromHours(2)))[0], "evening");
        Assert.Equal((1, 1, false), (bar.FirstColumn, bar.ColumnSpan, bar.IsBlock));
    }

    [Fact]
    public void Times_are_read_in_the_given_zone()
    {
        // 18:00Z on Monday = 01:00 Tuesday in UTC+7.
        var utc = new DateTimeOffset(Mon.ToDateTime(new TimeOnly(18, 0)), TimeSpan.Zero);
        var ev = new CalendarEvent { AccountId = "a", CalendarId = "c", Id = "x", Title = "x", Start = utc, End = utc.AddHours(1) };
        Assert.Equal(1, Bar(Arrange(1, 4, ev)[0], "x").FirstColumn);
    }

    [Fact]
    public void All_day_event_across_weeks_is_split_with_continuation_flags()
    {
        var weeks = Arrange(2, 4, A("trip", 5, 8)); // Sat .. next Tue
        var first = Bar(weeks[0], "trip");
        var second = Bar(weeks[1], "trip");
        Assert.Equal((5, 2, false, true), (first.FirstColumn, first.ColumnSpan, first.ContinuesBefore, first.ContinuesAfter));
        Assert.Equal((0, 2, true, false), (second.FirstColumn, second.ColumnSpan, second.ContinuesBefore, second.ContinuesAfter));
        Assert.True(first.IsBlock);
    }

    [Fact]
    public void Blocks_take_upper_lanes_and_free_lanes_are_reused()
    {
        var week = Arrange(1, 4,
            T("meeting", 1, "09:00", TimeSpan.FromHours(1)),
            A("conf", 0, 2),
            A("holiday", 3, 3),
            T("call", 3, "10:00", TimeSpan.FromHours(1)))[0];

        Assert.Equal(0, Bar(week, "conf").Lane);
        Assert.Equal(1, Bar(week, "meeting").Lane);   // below the block on Tuesday
        Assert.Equal(0, Bar(week, "holiday").Lane);   // Thursday lane 0 is free
        Assert.Equal(1, Bar(week, "call").Lane);
    }

    [Fact]
    public void Timed_events_on_a_day_are_ordered_by_start()
    {
        var week = Arrange(1, 4,
            T("late", 0, "15:00", TimeSpan.FromHours(1)),
            T("early", 0, "08:00", TimeSpan.FromHours(1)))[0];
        Assert.Equal(0, Bar(week, "early").Lane);
        Assert.Equal(1, Bar(week, "late").Lane);
    }

    [Fact]
    public void Overflow_reserves_one_line_for_the_more_label()
    {
        var week = Arrange(1, 3,
            T("e1", 0, "08:00", TimeSpan.FromHours(1)),
            T("e2", 0, "09:00", TimeSpan.FromHours(1)),
            T("e3", 0, "10:00", TimeSpan.FromHours(1)),
            T("e4", 0, "11:00", TimeSpan.FromHours(1)))[0];

        Assert.Equal(["e1", "e2"], week.Bars.Select(b => b.Event.Id));
        Assert.Equal(2, week.HiddenCounts[0]);
        Assert.Equal(["e1", "e2", "e3", "e4"], week.EventsByDay[0].Select(e => e.Id));
    }

    [Fact]
    public void Exactly_max_lanes_shows_everything()
    {
        var week = Arrange(1, 3,
            T("e1", 0, "08:00", TimeSpan.FromHours(1)),
            T("e2", 0, "09:00", TimeSpan.FromHours(1)),
            T("e3", 0, "10:00", TimeSpan.FromHours(1)))[0];
        Assert.Equal(3, week.Bars.Count);
        Assert.Equal(0, week.HiddenCounts[0]);
    }

    [Fact]
    public void Block_is_cut_where_a_day_overflows()
    {
        // Lane 0: "a" Mon-Wed, lane 1: "wide" Mon-Wed; Tuesday also has two timed events → Tuesday overflows
        // with maxLanes 2, so "wide" (lane 1) is hidden on Tuesday only and drawn as two segments.
        var week = Arrange(1, 2,
            A("a", 0, 2),
            A("wide", 0, 2),
            T("t1", 1, "09:00", TimeSpan.FromHours(1)),
            T("t2", 1, "10:00", TimeSpan.FromHours(1)))[0];

        var wide = week.Bars.Where(b => b.Event.Id == "wide").OrderBy(b => b.FirstColumn).ToList();
        Assert.Equal([(0, 1), (2, 1)], wide.Select(b => (b.FirstColumn, b.ColumnSpan)));
        Assert.True(wide[0].ContinuesAfter);
        Assert.True(wide[1].ContinuesBefore);
        Assert.Equal([0, 3, 0, 0, 0, 0, 0], week.HiddenCounts);
    }

    [Fact]
    public void Events_outside_the_grid_are_ignored()
    {
        var weeks = Arrange(1, 4, T("before", -1, "09:00", TimeSpan.FromHours(1)), T("after", 7, "09:00", TimeSpan.FromHours(1)));
        Assert.Empty(weeks[0].Bars);
    }
}
