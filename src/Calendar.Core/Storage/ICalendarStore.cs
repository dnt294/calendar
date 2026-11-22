using Calendar.Core.Models;
using Calendar.Core.Providers;

namespace Calendar.Core.Storage;

/// <summary>Local offline cache: accounts, calendars (+ local preferences), events and sync state.</summary>
public interface ICalendarStore
{
    /// <summary>Creates / migrates the schema. Call once at startup.</summary>
    Task InitializeAsync(CancellationToken ct = default);

    Task UpsertAccountAsync(Account account, CancellationToken ct = default);
    Task<IReadOnlyList<Account>> GetAccountsAsync(CancellationToken ct = default);

    /// <summary>Removes the account with all its calendars, events and sync state.</summary>
    Task DeleteAccountAsync(string accountId, CancellationToken ct = default);

    /// <summary>
    /// Replaces the calendar list of an account with what the provider returned.
    /// Local preferences (enabled, color override) of calendars that still exist are kept;
    /// calendars no longer returned are removed together with their events.
    /// </summary>
    Task ReplaceCalendarsAsync(string accountId, IReadOnlyList<CalendarInfo> calendars, CancellationToken ct = default);

    Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(CancellationToken ct = default);

    Task SetCalendarPreferencesAsync(string accountId, string calendarId, bool isEnabled, string? colorOverride,
        CancellationToken ct = default);

    /// <summary>
    /// Applies a sync result atomically: events and the new sync state are written in one transaction.
    /// A full sync replaces all cached events of the calendar.
    /// </summary>
    Task ApplySyncResultAsync(string accountId, string calendarId, SyncResult result, CancellationToken ct = default);

    Task<string?> GetSyncStateAsync(string accountId, string calendarId, CancellationToken ct = default);

    /// <summary>Forgets the sync state so the next sync is a full one.</summary>
    Task ClearSyncStateAsync(string accountId, string calendarId, CancellationToken ct = default);

    /// <summary>Inserts or replaces one event (after a successful create/update on the provider).</summary>
    Task UpsertEventAsync(CalendarEvent ev, CancellationToken ct = default);

    Task DeleteEventAsync(string accountId, string calendarId, string eventId, CancellationToken ct = default);

    /// <summary>
    /// Events overlapping <paramref name="range"/>. All-day events are matched by date in
    /// <paramref name="zone"/>, timed events by instant.
    /// </summary>
    Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(DateRange range, TimeZoneInfo zone, bool enabledCalendarsOnly = true,
        CancellationToken ct = default);
}
