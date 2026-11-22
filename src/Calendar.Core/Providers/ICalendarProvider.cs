using Calendar.Core.Models;

namespace Calendar.Core.Providers;

/// <summary>
/// One calendar backend (Google, Microsoft...). Implementations map provider models to Core models;
/// Core and UI never see provider SDK types.
/// </summary>
public interface ICalendarProvider
{
    /// <summary>"google", "microsoft".</summary>
    string ProviderId { get; }

    /// <summary>Interactive sign-in (browser / WAM). Returns the account that was signed in.</summary>
    Task<Account> SignInAsync(CancellationToken ct);

    /// <summary>Revokes/clears the stored token of <paramref name="account"/>.</summary>
    Task SignOutAsync(Account account, CancellationToken ct);

    Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(Account account, CancellationToken ct);

    /// <summary>
    /// Incremental sync. <paramref name="syncState"/> == null means full sync of <paramref name="window"/>.
    /// Returns the changes and the new state to persist.
    /// </summary>
    Task<SyncResult> SyncEventsAsync(
        Account account, CalendarInfo calendar, string? syncState, DateRange window, CancellationToken ct);

    Task<CalendarEvent> CreateEventAsync(Account account, CalendarInfo calendar, EventDraft draft, CancellationToken ct);

    /// <summary>Updates a single event, or only this occurrence when <see cref="CalendarEvent.IsOccurrence"/>.</summary>
    Task<CalendarEvent> UpdateEventAsync(Account account, CalendarEvent ev, EventDraft draft, CancellationToken ct);

    /// <summary>Deletes a single event, or only this occurrence when <see cref="CalendarEvent.IsOccurrence"/>.</summary>
    Task DeleteEventAsync(Account account, CalendarEvent ev, CancellationToken ct);

    Task RespondAsync(Account account, CalendarEvent ev, RsvpStatus status, CancellationToken ct);
}
