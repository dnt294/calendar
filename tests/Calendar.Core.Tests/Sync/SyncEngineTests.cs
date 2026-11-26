using Calendar.Core.Models;
using Calendar.Core.Providers;
using Calendar.Core.Storage;
using Calendar.Core.Sync;
using Microsoft.Data.Sqlite;

namespace Calendar.Core.Tests.Sync;

public sealed class SyncEngineTests : IAsyncLifetime
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly DateTimeOffset T = new(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateRange Window = new(T.AddDays(-30), T.AddDays(30));
    private static readonly DateRange Day = DateRange.ForDays(new DateOnly(2026, 11, 23), 1, Utc);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"calendar-sync-{Guid.NewGuid():N}.db");
    private readonly SqliteCalendarStore _store;
    private readonly FakeProvider _provider = new();
    private readonly SyncEngine _engine;

    public SyncEngineTests()
    {
        _store = new SqliteCalendarStore(_path);
        _engine = new SyncEngine(_store, [_provider]);
    }

    public Task InitializeAsync() => _store.InitializeAsync();

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            File.Delete(_path + suffix);
        return Task.CompletedTask;
    }

    private static CalendarEvent Ev(string id, string calendarId = "primary") => new()
    {
        AccountId = FakeProvider.Account.Id, CalendarId = calendarId, Id = id, Title = id,
        Start = T, End = T.AddHours(1),
    };

    private async Task<string[]> CachedIds() =>
        (await _store.GetEventsAsync(Day, Utc, enabledCalendarsOnly: false)).Select(e => e.Id).Order().ToArray();

    [Fact]
    public async Task AddAccount_signs_in_stores_account_calendars_and_events()
    {
        _provider.Events["primary"] = [Ev("a"), Ev("b")];
        _provider.Events["work"] = [Ev("w", "work")];

        var account = await _engine.AddAccountAsync("fake", Window, CancellationToken.None);

        Assert.Equal(FakeProvider.Account, account);
        Assert.Equal([FakeProvider.Account], await _store.GetAccountsAsync());
        Assert.Equal(["primary", "work"], (await _store.GetCalendarsAsync()).Select(c => c.Id).Order());
        Assert.Equal(["a", "b", "w"], await CachedIds());
        Assert.Equal("state-primary-1", await _store.GetSyncStateAsync(account.Id, "primary"));
    }

    [Fact]
    public async Task Next_sync_passes_the_stored_state()
    {
        await _engine.AddAccountAsync("fake", Window, CancellationToken.None);
        await _engine.SyncAccountAsync(FakeProvider.Account, Window, CancellationToken.None);

        Assert.Equal([null, "state-primary-1"], _provider.StatesSeen["primary"]);
    }

    [Fact]
    public async Task Full_resync_required_clears_state_and_retries_once()
    {
        await _engine.AddAccountAsync("fake", Window, CancellationToken.None);
        _provider.Events["primary"] = [Ev("fresh")];
        _provider.RejectStateOnce = true;

        await _engine.SyncAccountAsync(FakeProvider.Account, Window, CancellationToken.None);

        Assert.Equal([null, "state-primary-1", null], _provider.StatesSeen["primary"]);
        Assert.Equal(["fresh"], await CachedIds());
    }

    [Fact]
    public async Task Calendars_removed_on_provider_disappear_with_their_events()
    {
        _provider.Events["work"] = [Ev("w", "work")];
        await _engine.AddAccountAsync("fake", Window, CancellationToken.None);

        _provider.Calendars.RemoveAll(c => c.Id == "work");
        await _engine.SyncAccountAsync(FakeProvider.Account, Window, CancellationToken.None);

        Assert.Equal(["primary"], (await _store.GetCalendarsAsync()).Select(c => c.Id));
        Assert.Empty(await CachedIds());
    }

    [Fact]
    public async Task SyncAll_reports_failures_per_account_without_stopping()
    {
        await _engine.AddAccountAsync("fake", Window, CancellationToken.None);
        var orphan = new Account("other:1", "other", "1", "x@example.com", "X");
        await _store.UpsertAccountAsync(orphan);
        _provider.Events["primary"] = [Ev("after")];

        var results = await _engine.SyncAllAsync(Window, CancellationToken.None);

        Assert.True(results.Single(r => r.Account.Id == FakeProvider.Account.Id).Succeeded);
        Assert.IsType<InvalidOperationException>(results.Single(r => r.Account.Id == orphan.Id).Error);
        Assert.Equal(["after"], await CachedIds());
    }

    [Fact]
    public async Task Failed_sign_in_stores_nothing()
    {
        _provider.SignInError = new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _engine.AddAccountAsync("fake", Window, CancellationToken.None));

        Assert.Empty(await _store.GetAccountsAsync());
    }

    private sealed class FakeProvider : ICalendarProvider
    {
        public static readonly Account Account = new("fake:me", "fake", "me", "me@example.com", "Me");

        public List<CalendarInfo> Calendars { get; } =
        [
            new(Account.Id, "primary", "Me", "#0078D4", IsPrimary: true, CanEdit: true),
            new(Account.Id, "work", "Work", "#C239B3", IsPrimary: false, CanEdit: true),
        ];

        public Dictionary<string, List<CalendarEvent>> Events { get; } = new();
        public Dictionary<string, List<string?>> StatesSeen { get; } = new();
        public bool RejectStateOnce { get; set; }
        public Exception? SignInError { get; set; }
        private readonly Dictionary<string, int> _counter = new();

        public string ProviderId => "fake";

        public Task<Account> SignInAsync(CancellationToken ct) =>
            SignInError is null ? Task.FromResult(Account) : Task.FromException<Account>(SignInError);

        public Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(Account a, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<CalendarInfo>>(Calendars.ToList());

        public Task<SyncResult> SyncEventsAsync(Account a, CalendarInfo c, string? syncState, DateRange window,
            CancellationToken ct)
        {
            if (!StatesSeen.TryGetValue(c.Id, out var seen))
                StatesSeen[c.Id] = seen = [];
            seen.Add(syncState);

            if (syncState is not null && RejectStateOnce)
            {
                RejectStateOnce = false;
                return Task.FromResult(SyncResult.ResyncRequired());
            }

            var n = _counter[c.Id] = _counter.GetValueOrDefault(c.Id) + 1;
            return Task.FromResult(new SyncResult(Events.GetValueOrDefault(c.Id, []), [], $"state-{c.Id}-{n}",
                IsFullSync: true));
        }

        public Task SignOutAsync(Account account, CancellationToken ct) => throw new NotSupportedException();
        public Task<CalendarEvent> CreateEventAsync(Account a, CalendarInfo c, EventDraft d, CancellationToken ct) => throw new NotSupportedException();
        public Task<CalendarEvent> UpdateEventAsync(Account a, CalendarEvent e, EventDraft d, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteEventAsync(Account a, CalendarEvent e, CancellationToken ct) => throw new NotSupportedException();
        public Task RespondAsync(Account a, CalendarEvent e, RsvpStatus s, CancellationToken ct) => throw new NotSupportedException();
    }
}
