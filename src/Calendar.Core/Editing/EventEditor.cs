using Calendar.Core.Models;
using Calendar.Core.Providers;
using Calendar.Core.Storage;

namespace Calendar.Core.Editing;

/// <summary>The draft has problems the user must fix; nothing was sent.</summary>
public sealed class InvalidDraftException(IReadOnlyList<DraftProblem> problems)
    : Exception("The event draft is not valid.")
{
    public IReadOnlyList<DraftProblem> Problems { get; } = problems;
}

/// <summary>
/// Online-only writes (architecture.md, data flow 3): call the provider first; only when it succeeds
/// is the local cache updated. On failure the cache is left untouched and the error propagates.
/// Occurrences of recurring events are changed one at a time (the provider receives the occurrence).
/// </summary>
public sealed class EventEditor
{
    private readonly ICalendarStore _store;
    private readonly IReadOnlyDictionary<string, ICalendarProvider> _providers;

    public EventEditor(ICalendarStore store, IEnumerable<ICalendarProvider> providers)
    {
        _store = store;
        _providers = providers.ToDictionary(p => p.ProviderId);
    }

    public async Task<CalendarEvent> CreateAsync(CalendarInfo calendar, EventDraft draft, CancellationToken ct)
    {
        Validate(draft);
        if (!calendar.CanEdit)
            throw new InvalidOperationException($"Calendar '{calendar.Name}' is read-only.");
        var (account, provider) = await ResolveAsync(calendar.AccountId, ct);

        var created = await provider.CreateEventAsync(account, calendar, draft, ct);
        await _store.UpsertEventAsync(created, ct);
        return created;
    }

    public async Task<CalendarEvent> UpdateAsync(CalendarEvent ev, EventDraft draft, CancellationToken ct)
    {
        Validate(draft);
        var (account, provider) = await ResolveAsync(ev.AccountId, ct);

        var updated = await provider.UpdateEventAsync(account, ev, draft, ct);
        await _store.UpsertEventAsync(updated, ct);
        return updated;
    }

    public async Task DeleteAsync(CalendarEvent ev, CancellationToken ct)
    {
        var (account, provider) = await ResolveAsync(ev.AccountId, ct);

        await provider.DeleteEventAsync(account, ev, ct);
        await _store.DeleteEventAsync(ev.AccountId, ev.CalendarId, ev.Id, ct);
    }

    /// <summary>Accept / Tentative / Decline an invite (task 7.1); the cache gets the provider's result.</summary>
    public async Task<CalendarEvent> RespondAsync(CalendarEvent ev, RsvpStatus status, CancellationToken ct)
    {
        if (status is not (RsvpStatus.Accepted or RsvpStatus.Tentative or RsvpStatus.Declined))
            throw new ArgumentOutOfRangeException(nameof(status), status, "Only Accepted, Tentative or Declined can be sent.");
        if (ev.SelfRsvp == RsvpStatus.None)
            throw new InvalidOperationException($"'{ev.Title}' is not an invite to this account.");
        var (account, provider) = await ResolveAsync(ev.AccountId, ct);

        var updated = await provider.RespondAsync(account, ev, status, ct);
        await _store.UpsertEventAsync(updated, ct);
        return updated;
    }

    private static void Validate(EventDraft draft)
    {
        var problems = draft.Validate();
        if (problems.Count > 0)
            throw new InvalidDraftException(problems);
    }

    private async Task<(Account, ICalendarProvider)> ResolveAsync(string accountId, CancellationToken ct)
    {
        var account = (await _store.GetAccountsAsync(ct)).FirstOrDefault(a => a.Id == accountId)
                      ?? throw new InvalidOperationException($"Account '{accountId}' is not signed in.");
        var provider = _providers.TryGetValue(account.ProviderId, out var p)
            ? p
            : throw new InvalidOperationException($"No provider registered for '{account.ProviderId}'.");
        return (account, provider);
    }
}
