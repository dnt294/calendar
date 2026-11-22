namespace Calendar.Core.Models;

/// <summary>Fields the user can set when creating or editing an event.</summary>
/// <remarks>For all-day drafts, Start/End follow the same floating convention as <see cref="CalendarEvent"/>.</remarks>
public sealed record EventDraft
{
    public required string Title { get; init; }
    public required DateTimeOffset Start { get; init; }
    public required DateTimeOffset End { get; init; }
    public bool IsAllDay { get; init; }
    public string? Location { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string> AttendeeEmails { get; init; } = [];
    public IReadOnlyList<Reminder> Reminders { get; init; } = [];

    public static EventDraft FromEvent(CalendarEvent ev) => new()
    {
        Title = ev.Title,
        Start = ev.Start,
        End = ev.End,
        IsAllDay = ev.IsAllDay,
        Location = ev.Location,
        Description = ev.Description,
        AttendeeEmails = ev.Attendees.Where(a => !a.IsSelf).Select(a => a.Email).ToList(),
        Reminders = ev.Reminders,
    };
}
