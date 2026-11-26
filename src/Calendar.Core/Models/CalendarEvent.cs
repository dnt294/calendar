namespace Calendar.Core.Models;

/// <summary>
/// One event as shown in the views. Recurring events are stored per occurrence
/// (providers expand them), linked by <see cref="SeriesId"/>.
/// </summary>
/// <remarks>
/// All-day events are floating dates: <see cref="Start"/>/<see cref="End"/> hold midnight of the
/// start date / the day after the last date, with offset zero. Use <see cref="StartDate"/> and
/// <see cref="EndDateExclusive"/> instead of comparing them as instants.
/// </remarks>
public sealed record CalendarEvent
{
    public required string AccountId { get; init; }
    public required string CalendarId { get; init; }

    /// <summary>Provider event id; for an occurrence, the id of that occurrence.</summary>
    public required string Id { get; init; }

    public required string Title { get; init; }
    public required DateTimeOffset Start { get; init; }
    public required DateTimeOffset End { get; init; }
    public bool IsAllDay { get; init; }

    public string? Location { get; init; }
    public string? Description { get; init; }

    /// <summary>Id of the recurring series this occurrence belongs to; null for single events.</summary>
    public string? SeriesId { get; init; }
    public bool IsOccurrence => SeriesId is not null;

    /// <summary>Join link (Meet, Teams, Zoom...) if the provider exposes one.</summary>
    public string? MeetingUrl { get; init; }

    public string? OrganizerEmail { get; init; }
    public bool IsOrganizer { get; init; }

    /// <summary>The account owner's response; <see cref="RsvpStatus.None"/> when not an invite.</summary>
    public RsvpStatus SelfRsvp { get; init; }

    public IReadOnlyList<Attendee> Attendees { get; init; } = [];
    public IReadOnlyList<Reminder> Reminders { get; init; } = [];

    /// <summary>
    /// The event follows its calendar's default reminders; <see cref="Reminders"/> then holds those
    /// defaults as resolved at sync time.
    /// </summary>
    public bool UsesDefaultReminders { get; init; }

    /// <summary>Provider concurrency token (Google etag / Graph changeKey).</summary>
    public string? ETag { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }

    public DateOnly StartDate => DateOnly.FromDateTime(Start.DateTime);
    public DateOnly EndDateExclusive => DateOnly.FromDateTime(End.DateTime);

    /// <summary>Builds the Start/End pair for an all-day event covering <paramref name="first"/>..<paramref name="last"/> inclusive.</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) AllDaySpan(DateOnly first, DateOnly last)
    {
        if (last < first)
            throw new ArgumentException("Last day must not be before first day.", nameof(last));
        return (ToFloating(first), ToFloating(last.AddDays(1)));
    }

    /// <summary>Time range of the event as seen in <paramref name="zone"/>.</summary>
    public DateRange GetRange(TimeZoneInfo zone) => IsAllDay
        ? DateRange.ForDays(StartDate, EndDateExclusive.DayNumber - StartDate.DayNumber, zone)
        : new DateRange(Start, End);

    private static DateTimeOffset ToFloating(DateOnly day) =>
        new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
