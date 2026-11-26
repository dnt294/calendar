using Calendar.Core.Models;
using Calendar.Core.Providers;
using Calendar.Core.Storage;

namespace Calendar.Core.Sync;

/// <summary>Outcome of syncing one account.</summary>
public sealed record AccountSyncResult(Account Account, Exception? Error)
{
    public bool Succeeded => Error is null;
}

/// <summary>
/// Pulls calendars and events from the providers into the local store.
/// TODO(4.2): periodic sync, sync on focus, no overlapping runs.
/// </summary>
public sealed class SyncEngine
{
    private readonly ICalendarStore _store;
    private readonly IReadOnlyDictionary<string, ICalendarProvider> _providers;

    public SyncEngine(ICalendarStore store, IEnumerable<ICalendarProvider> providers)
    {
        _store = store;
        _providers = providers.ToDictionary(p => p.ProviderId);
    }

    /// <summary>Time range kept in the cache: 90 days back, one year ahead.</summary>
    public static DateRange DefaultWindow(DateTimeOffset now) => new(now.AddDays(-90), now.AddDays(365));

    /// <summary>Interactive sign-in, then a first full sync of the new account.</summary>
    public async Task<Account> AddAccountAsync(string providerId, DateRange window, CancellationToken ct)
    {
        var account = await Provider(providerId).SignInAsync(ct);
        await _store.UpsertAccountAsync(account, ct);
        await SyncAccountAsync(account, window, ct);
        return account;
    }

    /// <summary>Refreshes the calendar list of the account, then syncs every calendar.</summary>
    public async Task SyncAccountAsync(Account account, DateRange window, CancellationToken ct)
    {
        var provider = Provider(account.ProviderId);

        var calendars = await provider.GetCalendarsAsync(account, ct);
        await _store.ReplaceCalendarsAsync(account.Id, calendars, ct);

        // Disabled calendars are synced too, so turning one on shows its events immediately.
        foreach (var calendar in calendars)
        {
            var state = await _store.GetSyncStateAsync(account.Id, calendar.Id, ct);
            var result = await provider.SyncEventsAsync(account, calendar, state, window, ct);
            if (result.FullResyncRequired)
            {
                await _store.ClearSyncStateAsync(account.Id, calendar.Id, ct);
                result = await provider.SyncEventsAsync(account, calendar, null, window, ct);
                if (result.FullResyncRequired)
                    throw new InvalidOperationException(
                        $"Provider '{provider.ProviderId}' asked for a full resync of '{calendar.Id}' twice in a row.");
            }
            await _store.ApplySyncResultAsync(account.Id, calendar.Id, result, ct);
        }
    }

    /// <summary>
    /// Signs the account out at the provider (revoke + forget token), then deletes its calendars,
    /// events and sync state from the cache. Local data is removed even when sign-out fails
    /// (offline, already revoked, provider no longer registered).
    /// </summary>
    /// <returns>The sign-out error, or null when the provider confirmed it.</returns>
    public async Task<Exception?> RemoveAccountAsync(Account account, CancellationToken ct)
    {
        Exception? signOutError = null;
        try
        {
            await Provider(account.ProviderId).SignOutAsync(account, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            signOutError = ex;
        }
        await _store.DeleteAccountAsync(account.Id, ct);
        return signOutError;
    }

    /// <summary>Syncs every stored account. A failing account does not stop the others.</summary>
    public async Task<IReadOnlyList<AccountSyncResult>> SyncAllAsync(DateRange window, CancellationToken ct)
    {
        var results = new List<AccountSyncResult>();
        foreach (var account in await _store.GetAccountsAsync(ct))
        {
            try
            {
                await SyncAccountAsync(account, window, ct);
                results.Add(new AccountSyncResult(account, null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(new AccountSyncResult(account, ex));
            }
        }
        return results;
    }

    private ICalendarProvider Provider(string providerId) =>
        _providers.TryGetValue(providerId, out var p)
            ? p
            : throw new InvalidOperationException($"No provider registered for '{providerId}'.");
}
