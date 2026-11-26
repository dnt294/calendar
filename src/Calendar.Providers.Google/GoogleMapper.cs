using System.Globalization;
using Calendar.Core.Models;
using Google.Apis.Calendar.v3.Data;

namespace Calendar.Providers.Google;

/// <summary>Maps Google Calendar API v3 resources to Core models. Pure functions, no I/O.</summary>
public static class GoogleMapper
{
    public static CalendarInfo ToCalendar(CalendarListEntry entry, string accountId) => new(
        accountId,
        entry.Id,
        entry.SummaryOverride ?? entry.Summary ?? entry.Id,
        NormalizeColor(entry.BackgroundColor),
        IsPrimary: entry.Primary == true,
        CanEdit: entry.AccessRole is "owner" or "writer");

    /// <summary>Null for cancelled events (they are deletions in a sync result).</summary>
    /// <param name="defaultReminders">The calendar's default reminders (Events.list <c>defaultReminders</c>).</param>
    public static CalendarEvent? ToEvent(Event e, string accountId, string calendarId,
        IList<EventReminder>? defaultReminders)
    {
        if (e.Status == "cancelled")
            return null;

        var isAllDay = e.Start?.Date is not null;
        DateTimeOffset start, end;
        if (isAllDay)
        {
            var first = ParseDate(e.Start!.Date);
            var endExclusive = e.End?.Date is { } d ? ParseDate(d) : first.AddDays(1);
            (start, end) = CalendarEvent.AllDaySpan(first, endExclusive > first ? endExclusive.AddDays(-1) : first);
        }
        else
        {
            start = e.Start?.DateTimeDateTimeOffset
                    ?? throw new FormatException($"Event '{e.Id}' has no start time.");
            end = e.End?.DateTimeDateTimeOffset ?? start;
            if (end < start)
                end = start;
        }

        var attendees = (e.Attendees ?? [])
            .Where(a => !string.IsNullOrEmpty(a.Email) && a.Resource != true)
            .Select(a => new Attendee(
                a.Email,
                a.DisplayName,
                ToRsvp(a.ResponseStatus),
                IsSelf: a.Self == true,
                IsOrganizer: a.Organizer == true,
                IsOptional: a.Optional == true))
            .ToList();

        var isOrganizer = e.Organizer?.Self == true;
        var self = attendees.FirstOrDefault(a => a.IsSelf);

        return new CalendarEvent
        {
            AccountId = accountId,
            CalendarId = calendarId,
            Id = e.Id,
            Title = string.IsNullOrWhiteSpace(e.Summary) ? "(Không có tiêu đề)" : e.Summary,
            Start = start,
            End = end,
            IsAllDay = isAllDay,
            Location = NullIfEmpty(e.Location),
            Description = NullIfEmpty(e.Description),
            SeriesId = NullIfEmpty(e.RecurringEventId),
            MeetingUrl = MeetingUrl(e),
            OrganizerEmail = NullIfEmpty(e.Organizer?.Email),
            IsOrganizer = isOrganizer,
            SelfRsvp = self is not null && !isOrganizer ? self.Status : RsvpStatus.None,
            Attendees = attendees,
            Reminders = Reminders(e.Reminders, defaultReminders),
            UsesDefaultReminders = e.Reminders is null || e.Reminders.UseDefault == true,
            ETag = e.ETag,
            UpdatedAt = e.UpdatedDateTimeOffset,
        };
    }

    /// <summary>
    /// Body for Events.insert / Events.patch: only the fields the app edits. Empty strings clear
    /// location/description on patch; the attendee list replaces the old one, keeping existing
    /// guests' responses and the owner's own attendee entry.
    /// </summary>
    /// <param name="current">The event's guest list as Google has it now (fetched just before a patch).
    /// Room/resource entries — which the cache does not keep — are preserved from it, and its responses
    /// win over cached ones.</param>
    public static Event ToGoogleEvent(EventDraft draft, CalendarEvent? existing = null, IList<EventAttendee>? current = null)
    {
        var e = new Event
        {
            Summary = draft.Title,
            Location = draft.Location ?? "",
            Description = draft.Description ?? "",
        };

        if (draft.IsAllDay)
        {
            var first = DateOnly.FromDateTime(draft.Start.DateTime);
            var endExclusive = DateOnly.FromDateTime(draft.End.DateTime);
            e.Start = new EventDateTime { Date = first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
            e.End = new EventDateTime { Date = endExclusive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        }
        else
        {
            e.Start = new EventDateTime { DateTimeDateTimeOffset = draft.Start };
            e.End = new EventDateTime { DateTimeDateTimeOffset = draft.End };
        }

        var known = (existing?.Attendees ?? []).ToDictionary(a => a.Email, StringComparer.OrdinalIgnoreCase);
        var live = (current ?? []).Where(a => !string.IsNullOrEmpty(a.Email))
            .GroupBy(a => a.Email, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var attendees = draft.AttendeeEmails
            .Select(email => live.TryGetValue(email, out var l)
                ? new EventAttendee { Email = l.Email, ResponseStatus = l.ResponseStatus, Optional = l.Optional, DisplayName = l.DisplayName }
                : known.TryGetValue(email, out var a)
                    ? new EventAttendee { Email = a.Email, ResponseStatus = FromRsvp(a.Status), Optional = a.IsOptional ? true : null }
                    : new EventAttendee { Email = email, ResponseStatus = "needsAction" })
            .ToList();
        var self = (current ?? []).FirstOrDefault(a => a.Self == true) is { } liveSelf
            ? new EventAttendee { Email = liveSelf.Email, ResponseStatus = liveSelf.ResponseStatus }
            : existing?.Attendees.FirstOrDefault(a => a.IsSelf) is { } cachedSelf
                ? new EventAttendee { Email = cachedSelf.Email, ResponseStatus = FromRsvp(cachedSelf.Status) }
                : null;
        if (attendees.Count > 0 && self is not null && attendees.All(a => !a.Email.Equals(self.Email, StringComparison.OrdinalIgnoreCase)))
            attendees.Insert(0, self);
        // Rooms are not editable in the app: keep them exactly as Google has them.
        var rooms = (current ?? []).Where(a => a.Resource == true).ToList();
        if (attendees.Count > 0)
            attendees.AddRange(rooms);
        // Only touch the guest list when there is or was one, so plain events are not given an empty list.
        if (attendees.Count > 0 || known.Count > 0 || live.Count > 0)
            e.Attendees = attendees.Count > 0 || rooms.Count == 0 ? attendees : rooms;

        e.Reminders = draft.UseDefaultReminders
            ? new Event.RemindersData { UseDefault = true }
            : new Event.RemindersData
            {
                UseDefault = false,
                Overrides = draft.Reminders
                    .Select(r => new EventReminder { Method = "popup", Minutes = r.MinutesBefore })
                    .ToList(),
            };
        return e;
    }

    /// <summary>Copy of <paramref name="attendees"/> with the owner's response changed; null when the owner is not a guest.</summary>
    public static List<EventAttendee>? WithSelfResponse(IList<EventAttendee>? attendees, RsvpStatus status)
    {
        if (attendees is null || !attendees.Any(a => a.Self == true))
            return null;
        return attendees
            .Select(a => a.Self == true
                ? new EventAttendee
                {
                    Email = a.Email, DisplayName = a.DisplayName, Optional = a.Optional, Comment = a.Comment,
                    AdditionalGuests = a.AdditionalGuests, ResponseStatus = FromRsvp(status),
                }
                : a)
            .ToList();
    }

    /// <summary>Guests are emailed when the event has (or had) guests other than the owner.</summary>
    public static string SendUpdatesFor(EventDraft? draft, CalendarEvent? existing) =>
        (draft?.AttendeeEmails.Count ?? 0) > 0 || (existing?.Attendees.Any(a => !a.IsSelf) ?? false) ? "all" : "none";

    public static string FromRsvp(RsvpStatus status) => status switch
    {
        RsvpStatus.Accepted => "accepted",
        RsvpStatus.Tentative => "tentative",
        RsvpStatus.Declined => "declined",
        _ => "needsAction",
    };

    public static RsvpStatus ToRsvp(string? responseStatus) => responseStatus switch
    {
        "accepted" => RsvpStatus.Accepted,
        "tentative" => RsvpStatus.Tentative,
        "declined" => RsvpStatus.Declined,
        "needsAction" => RsvpStatus.NeedsAction,
        _ => RsvpStatus.None,
    };

    private static string? MeetingUrl(Event e) =>
        NullIfEmpty(e.ConferenceData?.EntryPoints?.FirstOrDefault(p => p.EntryPointType == "video")?.Uri)
        ?? NullIfEmpty(e.HangoutLink);

    /// <summary>Only popup reminders become local notifications; email reminders are Google's business.</summary>
    private static IReadOnlyList<Reminder> Reminders(Event.RemindersData? data, IList<EventReminder>? defaults)
    {
        var source = data is null || data.UseDefault == true ? defaults : data.Overrides;
        return (source ?? [])
            .Where(r => r.Method == "popup" && r.Minutes is >= 0)
            .Select(r => r.Minutes!.Value)
            .Distinct()
            .Order()
            .Select(m => new Reminder(m))
            .ToList();
    }

    private static DateOnly ParseDate(string value) =>
        DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string NormalizeColor(string? hex) =>
        hex is { Length: 7 } && hex[0] == '#' ? hex.ToUpperInvariant() : "#039BE5";

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
