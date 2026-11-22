using Calendar.Core.Models;

namespace Calendar.Core.Tests.Models;

public class CalendarEventTests
{
    private static readonly TimeZoneInfo Hanoi = TimeZoneInfo.CreateCustomTimeZone("UTC+7", TimeSpan.FromHours(7), "UTC+7", "UTC+7");

    [Fact]
    public void AllDaySpan_is_floating_and_end_exclusive()
    {
        var (start, end) = CalendarEvent.AllDaySpan(new DateOnly(2026, 11, 23), new DateOnly(2026, 11, 24));
        Assert.Equal(new DateTimeOffset(2026, 11, 23, 0, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2026, 11, 25, 0, 0, 0, TimeSpan.Zero), end);
    }

    [Fact]
    public void AllDaySpan_rejects_reversed_days()
    {
        Assert.Throws<ArgumentException>(() =>
            CalendarEvent.AllDaySpan(new DateOnly(2026, 11, 24), new DateOnly(2026, 11, 23)));
    }

    [Fact]
    public void AllDay_dates_and_range_in_zone()
    {
        var (start, end) = CalendarEvent.AllDaySpan(new DateOnly(2026, 11, 23), new DateOnly(2026, 11, 23));
        var ev = new CalendarEvent
        {
            AccountId = "a", CalendarId = "c", Id = "e", Title = "Holiday",
            Start = start, End = end, IsAllDay = true,
        };

        Assert.Equal(new DateOnly(2026, 11, 23), ev.StartDate);
        Assert.Equal(new DateOnly(2026, 11, 24), ev.EndDateExclusive);
        var range = ev.GetRange(Hanoi);
        Assert.Equal(new DateTimeOffset(2026, 11, 23, 0, 0, 0, TimeSpan.FromHours(7)), range.Start);
        Assert.Equal(TimeSpan.FromDays(1), range.Duration);
    }

    [Fact]
    public void Timed_range_ignores_zone()
    {
        var start = new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.FromHours(7));
        var ev = new CalendarEvent
        {
            AccountId = "a", CalendarId = "c", Id = "e", Title = "Standup",
            Start = start, End = start.AddMinutes(15),
        };
        Assert.Equal(new DateRange(start, start.AddMinutes(15)), ev.GetRange(TimeZoneInfo.Utc));
        Assert.False(ev.IsOccurrence);
        Assert.True((ev with { SeriesId = "s" }).IsOccurrence);
    }

    [Fact]
    public void Reminder_rejects_negative_minutes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Reminder(-1));
        Assert.Equal(10, new Reminder(10).MinutesBefore);
    }

    [Fact]
    public void EventDraft_FromEvent_excludes_self_attendee()
    {
        var start = DateTimeOffset.UnixEpoch;
        var ev = new CalendarEvent
        {
            AccountId = "a", CalendarId = "c", Id = "e", Title = "1:1", Start = start, End = start.AddHours(1),
            Attendees =
            [
                new Attendee("me@example.com", "Me", RsvpStatus.Accepted, IsSelf: true),
                new Attendee("you@example.com", null, RsvpStatus.NeedsAction),
            ],
            Reminders = [new Reminder(10)],
        };

        var draft = EventDraft.FromEvent(ev);

        Assert.Equal(["you@example.com"], draft.AttendeeEmails);
        Assert.Equal(ev.Reminders, draft.Reminders);
        Assert.Equal(ev.Start, draft.Start);
    }
}
