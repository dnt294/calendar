using Calendar.Core.Models;

namespace Calendar.Core.Providers;

/// <summary>Outcome of one <see cref="ICalendarProvider.SyncEventsAsync"/> call for one calendar.</summary>
/// <param name="IsFullSync">True when <see cref="Upserted"/> is the complete set for the window (cache should be replaced).</param>
/// <param name="NewSyncState">Opaque token to pass to the next sync (Google syncToken / Graph deltaLink).</param>
/// <param name="FullResyncRequired">The previous state was rejected (Google 410, expired delta); caller must retry with state = null.</param>
public sealed record SyncResult(
    IReadOnlyList<CalendarEvent> Upserted,
    IReadOnlyList<string> DeletedEventIds,
    string? NewSyncState,
    bool IsFullSync,
    bool FullResyncRequired = false)
{
    public static SyncResult ResyncRequired() => new([], [], null, IsFullSync: false, FullResyncRequired: true);
}
