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
            ETag = e.ETag,
            UpdatedAt = e.UpdatedDateTimeOffset,
        };
    }

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
