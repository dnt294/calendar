using Calendar.Core.Models;
using Calendar.Providers.Google;
using Google.Apis.Calendar.v3.Data;

namespace Calendar.Providers.Tests.Google;

public class GoogleMapperTests
{
    private const string Account = "google:me@gmail.com";
    private static readonly DateTimeOffset Nine = new(2026, 11, 23, 9, 0, 0, TimeSpan.FromHours(7));

    private static Event Timed(string id = "e1") => new()
    {
        Id = id,
        Summary = "Standup",
        Status = "confirmed",
        Start = new EventDateTime { DateTimeDateTimeOffset = Nine },
        End = new EventDateTime { DateTimeDateTimeOffset = Nine.AddMinutes(15) },
    };

    private static CalendarEvent Map(Event e, IList<EventReminder>? defaults = null) =>
        GoogleMapper.ToEvent(e, Account, "primary", defaults) ?? throw new InvalidOperationException("null");

    [Fact]
    public void Timed_event_basic_fields()
    {
        var e = Timed();
        e.Location = "Room 1";
        e.Description = "Daily";
        e.ETag = "\"123\"";
        e.UpdatedDateTimeOffset = Nine.AddDays(-1);

        var ev = Map(e);

        Assert.Equal((Account, "primary", "e1", "Standup"), (ev.AccountId, ev.CalendarId, ev.Id, ev.Title));
        Assert.Equal(Nine, ev.Start);
        Assert.Equal(Nine.AddMinutes(15), ev.End);
        Assert.False(ev.IsAllDay);
        Assert.Equal("Room 1", ev.Location);
        Assert.Equal("Daily", ev.Description);
        Assert.Equal("\"123\"", ev.ETag);
        Assert.Equal(Nine.AddDays(-1), ev.UpdatedAt);
        Assert.Null(ev.SeriesId);
        Assert.Equal(RsvpStatus.None, ev.SelfRsvp);
    }

    [Fact]
    public void Cancelled_event_maps_to_null()
    {
        var e = Timed();
        e.Status = "cancelled";
        Assert.Null(GoogleMapper.ToEvent(e, Account, "primary", null));
    }

    [Fact]
    public void Missing_title_gets_placeholder_and_empty_strings_become_null()
    {
        var e = Timed();
        e.Summary = " ";
        e.Location = "";
        var ev = Map(e);
        Assert.Equal("(Không có tiêu đề)", ev.Title);
        Assert.Null(ev.Location);
    }

    [Fact]
    public void All_day_event_uses_exclusive_end_date()
    {
        var e = new Event
        {
            Id = "trip", Summary = "Trip",
            Start = new EventDateTime { Date = "2026-11-26" },
            End = new EventDateTime { Date = "2026-11-29" }, // Thu..Sat inclusive
        };

        var ev = Map(e);

        Assert.True(ev.IsAllDay);
        Assert.Equal(new DateOnly(2026, 11, 26), ev.StartDate);
        Assert.Equal(new DateOnly(2026, 11, 29), ev.EndDateExclusive);
        Assert.Equal(TimeSpan.Zero, ev.Start.Offset);
    }

    [Fact]
    public void All_day_event_with_bad_end_is_one_day()
    {
        var e = new Event { Id = "x", Summary = "x", Start = new EventDateTime { Date = "2026-11-26" } };
        var ev = Map(e);
        Assert.Equal(new DateOnly(2026, 11, 27), ev.EndDateExclusive);
    }

    [Fact]
    public void Recurring_occurrence_keeps_series_id()
    {
        var e = Timed("abc_20261123T020000Z");
        e.RecurringEventId = "abc";
        var ev = Map(e);
        Assert.Equal("abc", ev.SeriesId);
        Assert.True(ev.IsOccurrence);
    }

    [Fact]
    public void Invite_maps_attendees_and_self_rsvp()
    {
        var e = Timed();
        e.Organizer = new Event.OrganizerData { Email = "boss@example.com" };
        e.Attendees =
        [
            new EventAttendee { Email = "boss@example.com", Organizer = true, ResponseStatus = "accepted" },
            new EventAttendee { Email = "me@gmail.com", DisplayName = "Me", Self = true, ResponseStatus = "tentative" },
            new EventAttendee { Email = "opt@example.com", Optional = true, ResponseStatus = "needsAction" },
            new EventAttendee { Email = "room@resource.calendar.google.com", Resource = true, ResponseStatus = "accepted" },
        ];

        var ev = Map(e);

        Assert.Equal("boss@example.com", ev.OrganizerEmail);
        Assert.False(ev.IsOrganizer);
        Assert.Equal(RsvpStatus.Tentative, ev.SelfRsvp);
        Assert.Equal(
        [
            new Attendee("boss@example.com", null, RsvpStatus.Accepted, IsOrganizer: true),
            new Attendee("me@gmail.com", "Me", RsvpStatus.Tentative, IsSelf: true),
            new Attendee("opt@example.com", null, RsvpStatus.NeedsAction, IsOptional: true),
        ], ev.Attendees);
    }

    [Fact]
    public void Own_event_has_no_rsvp_even_with_attendees()
    {
        var e = Timed();
        e.Organizer = new Event.OrganizerData { Email = "me@gmail.com", Self = true };
        e.Attendees = [new EventAttendee { Email = "me@gmail.com", Self = true, Organizer = true, ResponseStatus = "accepted" }];

        var ev = Map(e);

        Assert.True(ev.IsOrganizer);
        Assert.Equal(RsvpStatus.None, ev.SelfRsvp);
    }

    [Theory]
    [InlineData("accepted", RsvpStatus.Accepted)]
    [InlineData("tentative", RsvpStatus.Tentative)]
    [InlineData("declined", RsvpStatus.Declined)]
    [InlineData("needsAction", RsvpStatus.NeedsAction)]
    [InlineData(null, RsvpStatus.None)]
    [InlineData("weird", RsvpStatus.None)]
    public void Rsvp_mapping(string? google, RsvpStatus expected) =>
        Assert.Equal(expected, GoogleMapper.ToRsvp(google));

    [Fact]
    public void Meeting_url_prefers_conference_video_entry_then_hangout_link()
    {
        var e = Timed();
        e.HangoutLink = "https://meet.google.com/old";
        Assert.Equal("https://meet.google.com/old", Map(e).MeetingUrl);

        e.ConferenceData = new ConferenceData
        {
            EntryPoints =
            [
                new EntryPoint { EntryPointType = "phone", Uri = "tel:+1" },
                new EntryPoint { EntryPointType = "video", Uri = "https://meet.google.com/abc-defg-hij" },
            ],
        };
        Assert.Equal("https://meet.google.com/abc-defg-hij", Map(e).MeetingUrl);
    }

    [Fact]
    public void Reminders_use_calendar_defaults_and_only_popups()
    {
        IList<EventReminder> defaults =
        [
            new EventReminder { Method = "popup", Minutes = 30 },
            new EventReminder { Method = "email", Minutes = 60 },
        ];

        var e = Timed();
        e.Reminders = new Event.RemindersData { UseDefault = true };
        Assert.Equal([new Reminder(30)], Map(e, defaults).Reminders);

        e.Reminders = null;
        Assert.Equal([new Reminder(30)], Map(e, defaults).Reminders);

        e.Reminders = new Event.RemindersData
        {
            UseDefault = false,
            Overrides =
            [
                new EventReminder { Method = "popup", Minutes = 10 },
                new EventReminder { Method = "popup", Minutes = 0 },
                new EventReminder { Method = "email", Minutes = 1440 },
            ],
        };
        Assert.Equal([new Reminder(0), new Reminder(10)], Map(e, defaults).Reminders);

        e.Reminders = new Event.RemindersData { UseDefault = false };
        Assert.Empty(Map(e, defaults).Reminders);
    }

    [Fact]
    public void Calendar_entry_mapping()
    {
        var entry = new CalendarListEntry
        {
            Id = "me@gmail.com", Summary = "me@gmail.com", SummaryOverride = "Cá nhân",
            BackgroundColor = "#9fe1e7", Primary = true, AccessRole = "owner",
        };
        Assert.Equal(new CalendarInfo(Account, "me@gmail.com", "Cá nhân", "#9FE1E7", IsPrimary: true, CanEdit: true),
            GoogleMapper.ToCalendar(entry, Account));

        var holidays = new CalendarListEntry
        {
            Id = "vi.vietnamese#holiday@group.v.calendar.google.com", Summary = "Ngày lễ ở Việt Nam",
            BackgroundColor = null, AccessRole = "reader",
        };
        var cal = GoogleMapper.ToCalendar(holidays, Account);
        Assert.Equal("Ngày lễ ở Việt Nam", cal.Name);
        Assert.False(cal.IsPrimary);
        Assert.False(cal.CanEdit);
        Assert.Matches("^#[0-9A-F]{6}$", cal.Color);
    }
}
