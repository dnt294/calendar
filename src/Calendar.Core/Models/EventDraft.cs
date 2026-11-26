using System.Net.Mail;

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

    /// <summary>Guests other than the account owner.</summary>
    public IReadOnlyList<string> AttendeeEmails { get; init; } = [];

    /// <summary>Use the calendar's default reminders; <see cref="Reminders"/> is ignored then.</summary>
    public bool UseDefaultReminders { get; init; } = true;

    /// <summary>Custom popup reminders (empty = no reminder) when <see cref="UseDefaultReminders"/> is false.</summary>
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
        UseDefaultReminders = ev.UsesDefaultReminders,
        Reminders = ev.Reminders,
    };

    /// <summary>Drag &amp; drop: the event's own fields, moved to start at <paramref name="newStart"/> with the same duration.</summary>
    public static EventDraft MovedTo(CalendarEvent ev, DateTimeOffset newStart) => FromEvent(ev) with
    {
        Start = newStart,
        End = newStart + (ev.End - ev.Start),
        // Keep the HTML description and every reminder exactly as they are.
        Description = ev.Description,
    };

    /// <summary>Problems that must be fixed before saving; empty when the draft is valid.</summary>
    public IReadOnlyList<DraftProblem> Validate()
    {
        var problems = new List<DraftProblem>();
        if (IsAllDay ? End.Date <= Start.Date : End < Start)
            problems.Add(new DraftProblem(DraftProblemKind.EndBeforeStart));
        foreach (var email in AttendeeEmails.Where(e => !EmailList.IsValid(e)))
            problems.Add(new DraftProblem(DraftProblemKind.InvalidAttendeeEmail, email));
        return problems;
    }
}

public enum DraftProblemKind
{
    EndBeforeStart,
    InvalidAttendeeEmail,
}

/// <param name="Detail">The offending value (e.g. the email), if any.</param>
public sealed record DraftProblem(DraftProblemKind Kind, string? Detail = null);

/// <summary>Parses the guests box: emails separated by commas, semicolons, spaces or new lines.</summary>
public static class EmailList
{
    private static readonly char[] Separators = [',', ';', ' ', '\t', '\r', '\n'];

    /// <summary>Distinct (case-insensitive) entries in typed order. Invalid entries are kept so they can be reported.</summary>
    public static IReadOnlyList<string> Parse(string? text) =>
        (text ?? "")
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.Trim('<', '>'))
            .Where(e => e.Length > 0)
            .DistinctBy(e => e, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static string Format(IEnumerable<string> emails) => string.Join(", ", emails);

    /// <summary>A bare address like <c>name@domain.tld</c> (no display name).</summary>
    public static bool IsValid(string email) =>
        MailAddress.TryCreate(email, out var parsed)
        && parsed.Address.Equals(email, StringComparison.OrdinalIgnoreCase)
        && parsed.Host.Contains('.', StringComparison.Ordinal)
        && !parsed.Host.EndsWith('.');
}
