using Calendar.Core.Models;
using Calendar.Core.Reminders;
using Microsoft.Extensions.Time.Testing;

namespace Calendar.Core.Tests.Reminders;

public class ReminderPlannerTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("UTC+7", TimeSpan.FromHours(7), "UTC+7", "UTC+7");
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 8, 0, 0, TimeSpan.FromHours(7));

    internal static CalendarEvent Ev(string id, DateTimeOffset start, params int[] minutes) => new()
    {
        AccountId = "a", CalendarId = "c", Id = id, Title = id, Start = start, End = start.AddHours(1),
        Reminders = minutes.Select(m => new Reminder(m)).ToList(),
    };

    [Fact]
    public void Each_reminder_of_an_event_is_planned_at_start_minus_minutes()
    {
        var plan = ReminderPlanner.Plan([Ev("standup", Now.AddHours(1), 5, 30)], Now, Now.AddDays(1), Zone);
        Assert.Equal([Now.AddMinutes(30), Now.AddMinutes(55)], plan.Select(p => p.FireAt));
        Assert.All(plan, p => Assert.Equal("standup", p.Event.Id));
    }

    [Fact]
    public void Only_reminders_inside_the_window_are_planned()
    {
        var plan = ReminderPlanner.Plan(
        [
            Ev("past", Now.AddMinutes(-30), 10),         // fired 40' ago
            Ev("soon", Now.AddMinutes(10), 10),          // fires now (window start is inclusive)
            Ev("tomorrow", Now.AddDays(2), 10),          // outside the window
        ], Now, Now.AddDays(1), Zone);
        Assert.Equal(["soon"], plan.Select(p => p.Event.Id));
    }

    [Fact]
    public void Declined_events_and_events_without_reminders_are_skipped()
    {
        var declined = Ev("declined", Now.AddHours(1), 10) with { SelfRsvp = RsvpStatus.Declined };
        var plan = ReminderPlanner.Plan([declined, Ev("silent", Now.AddHours(1))], Now, Now.AddDays(1), Zone);
        Assert.Empty(plan);
    }

    [Fact]
    public void All_day_reminders_count_back_from_local_midnight()
    {
        // Google: "1 day before at 9:00" on an all-day event = 900 minutes before its start (midnight).
        var (s, e) = CalendarEvent.AllDaySpan(new DateOnly(2026, 11, 28), new DateOnly(2026, 11, 28));
        var allDay = Ev("trip", s, 900) with { End = e, IsAllDay = true };
        var occurrence = Assert.Single(ReminderPlanner.Plan([allDay], Now, Now.AddDays(1), Zone));
        Assert.Equal(new DateTimeOffset(2026, 11, 27, 9, 0, 0, TimeSpan.FromHours(7)), occurrence.FireAt);
    }

    [Fact]
    public void Key_identifies_event_start_and_minutes()
    {
        var a = ReminderPlanner.Plan([Ev("x", Now.AddHours(1), 10)], Now, Now.AddDays(1), Zone).Single();
        var moved = ReminderPlanner.Plan([Ev("x", Now.AddHours(2), 10)], Now, Now.AddDays(1), Zone).Single();
        Assert.NotEqual(a.Key, moved.Key); // moving the event gives a new reminder
        Assert.Equal(a.Key, ReminderPlanner.Plan([Ev("x", Now.AddHours(1), 10) with { Title = "renamed" }], Now, Now.AddDays(1), Zone).Single().Key);
    }

    [Fact]
    public void Next_upcoming_skips_started_all_day_and_declined()
    {
        var (s, e) = CalendarEvent.AllDaySpan(new DateOnly(2026, 11, 27), new DateOnly(2026, 11, 27));
        var events = new[]
        {
            Ev("started", Now.AddMinutes(-10)),
            Ev("allday", s) with { End = e, IsAllDay = true },
            Ev("declined", Now.AddMinutes(20)) with { SelfRsvp = RsvpStatus.Declined },
            Ev("later", Now.AddHours(3)),
            Ev("next", Now.AddMinutes(30)),
        };
        Assert.Equal("next", ReminderPlanner.NextUpcoming(events, Now)?.Id);
        Assert.Null(ReminderPlanner.NextUpcoming([], Now));
    }
}

public sealed class ReminderSchedulerTests : IDisposable
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("UTC+7", TimeSpan.FromHours(7), "UTC+7", "UTC+7");
    private static readonly DateTimeOffset Start = new(2026, 11, 27, 8, 0, 0, TimeSpan.FromHours(7));

    private readonly FakeTimeProvider _time = new(Start);
    private readonly List<CalendarEvent> _events = [];
    private readonly List<ReminderOccurrence> _fired = [];
    private readonly ReminderScheduler _scheduler;

    public ReminderSchedulerTests()
    {
        _scheduler = new ReminderScheduler((_, _) => Task.FromResult<IReadOnlyList<CalendarEvent>>(_events.ToList()), _time, Zone);
        _scheduler.ReminderDue += (_, r) => _fired.Add(r);
    }

    public void Dispose() => _scheduler.Dispose();

    private static CalendarEvent Ev(string id, TimeSpan startsIn, params int[] minutes) =>
        ReminderPlannerTests.Ev(id, Start + startsIn, minutes);

    [Fact]
    public async Task Fires_at_the_right_time_once()
    {
        _events.Add(Ev("standup", TimeSpan.FromMinutes(60), 5));
        await _scheduler.RefreshAsync();

        _time.Advance(TimeSpan.FromMinutes(54));
        Assert.Empty(_fired);
        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(["standup"], _fired.Select(f => f.Event.Id));

        // A sync reloads the same event: no second toast.
        await _scheduler.RefreshAsync();
        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.Single(_fired);
    }

    [Fact]
    public async Task Fires_soon_after_start_for_a_reminder_missed_within_the_grace_period()
    {
        _events.Add(Ev("missed", TimeSpan.FromMinutes(3), 5)); // was due 2 minutes ago
        await _scheduler.RefreshAsync();
        _time.Advance(TimeSpan.Zero);
        Assert.Single(_fired);
    }

    [Fact]
    public async Task Moved_event_reminds_at_its_new_time()
    {
        _events.Add(Ev("x", TimeSpan.FromMinutes(60), 10));
        await _scheduler.RefreshAsync();

        _events[0] = Ev("x", TimeSpan.FromMinutes(120), 10);
        await _scheduler.RefreshAsync();

        _time.Advance(TimeSpan.FromMinutes(60));
        Assert.Empty(_fired);
        _time.Advance(TimeSpan.FromMinutes(50));
        Assert.Single(_fired);
    }

    [Fact]
    public async Task Deleted_event_does_not_fire()
    {
        _events.Add(Ev("x", TimeSpan.FromMinutes(60), 10));
        await _scheduler.RefreshAsync();
        _events.Clear();
        await _scheduler.RefreshAsync();
        _time.Advance(TimeSpan.FromHours(2));
        Assert.Empty(_fired);
    }

    [Fact]
    public async Task Snooze_fires_again_after_the_delay()
    {
        _events.Add(Ev("x", TimeSpan.FromMinutes(15), 10));
        await _scheduler.RefreshAsync();
        _time.Advance(TimeSpan.FromMinutes(5));
        var first = Assert.Single(_fired);

        Assert.True(_scheduler.Snooze(first.Key, TimeSpan.FromMinutes(5)));
        await _scheduler.RefreshAsync(); // a sync in between must not drop the snooze
        _time.Advance(TimeSpan.FromMinutes(4));
        Assert.Single(_fired);
        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(2, _fired.Count);
        Assert.Equal(first.Key, _fired[1].Key);
    }

    [Fact]
    public void Snooze_of_unknown_reminder_is_ignored()
    {
        Assert.False(_scheduler.Snooze("nope", TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public async Task Several_reminders_due_together_all_fire()
    {
        _events.Add(Ev("a", TimeSpan.FromMinutes(30), 10));
        _events.Add(Ev("b", TimeSpan.FromMinutes(30), 10));
        await _scheduler.RefreshAsync();
        _time.Advance(TimeSpan.FromMinutes(20));
        Assert.Equal(["a", "b"], _fired.Select(f => f.Event.Id).Order());
    }

    [Fact]
    public async Task Later_reminders_are_picked_up_after_the_first_fires()
    {
        _events.Add(Ev("a", TimeSpan.FromMinutes(30), 10));
        _events.Add(Ev("b", TimeSpan.FromHours(3), 10));
        await _scheduler.RefreshAsync();
        _time.Advance(TimeSpan.FromMinutes(20));
        _time.Advance(TimeSpan.FromHours(3));
        Assert.Equal(["a", "b"], _fired.Select(f => f.Event.Id));
    }
}
