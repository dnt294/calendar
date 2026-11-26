using Calendar.Core.Editing;
using Calendar.Core.Models;
using Calendar.Core.Providers;
using Calendar.Core.Storage;
using Microsoft.Data.Sqlite;

namespace Calendar.Core.Tests.Editing;

public sealed class EventEditorTests : IAsyncLifetime
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly DateTimeOffset T = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateRange Day = DateRange.ForDays(new DateOnly(2026, 11, 27), 1, Utc);
    private static readonly Account Me = new("fake:me", "fake", "me", "me@example.com", "Me");
    private static readonly CalendarInfo Primary = new(Me.Id, "primary", "Me", "#039BE5", true, CanEdit: true);
    private static readonly CalendarInfo Holidays = new(Me.Id, "holidays", "Holidays", "#0B8043", false, CanEdit: false);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"calendar-editor-{Guid.NewGuid():N}.db");
    private readonly SqliteCalendarStore _store;
    private readonly FakeProvider _provider = new();
    private readonly EventEditor _editor;

    public EventEditorTests()
    {
        _store = new SqliteCalendarStore(_path);
        _editor = new EventEditor(_store, [_provider]);
    }

    public async Task InitializeAsync()
    {
        await _store.InitializeAsync();
        await _store.UpsertAccountAsync(Me);
        await _store.ReplaceCalendarsAsync(Me.Id, [Primary, Holidays]);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            File.Delete(_path + suffix);
        return Task.CompletedTask;
    }

    private static EventDraft Draft(string title = "New") => new() { Title = title, Start = T, End = T.AddHours(1) };

    private async Task<IReadOnlyList<CalendarEvent>> Cached() => await _store.GetEventsAsync(Day, Utc, enabledCalendarsOnly: false);

    [Fact]
    public async Task Create_calls_provider_then_caches_its_result()
    {
        var created = await _editor.CreateAsync(Primary, Draft(), CancellationToken.None);

        Assert.Equal("server-1", created.Id);
        Assert.Equal(["create primary New"], _provider.Calls);
        Assert.Equal("server-1", Assert.Single(await Cached()).Id);
    }

    [Fact]
    public async Task Invalid_draft_is_rejected_before_any_call()
    {
        var bad = Draft() with { End = T.AddHours(-1) };
        var ex = await Assert.ThrowsAsync<InvalidDraftException>(() => _editor.CreateAsync(Primary, bad, CancellationToken.None));
        Assert.Equal(DraftProblemKind.EndBeforeStart, Assert.Single(ex.Problems).Kind);
        Assert.Empty(_provider.Calls);
    }

    [Fact]
    public async Task Read_only_calendar_is_rejected()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _editor.CreateAsync(Holidays, Draft(), CancellationToken.None));
        Assert.Empty(_provider.Calls);
    }

    [Fact]
    public async Task Update_replaces_the_cached_event()
    {
        var ev = await _editor.CreateAsync(Primary, Draft(), CancellationToken.None);

        await _editor.UpdateAsync(ev, Draft("Renamed"), CancellationToken.None);

        Assert.Equal("Renamed", Assert.Single(await Cached()).Title);
        Assert.Equal("update server-1 Renamed", _provider.Calls[^1]);
    }

    [Fact]
    public async Task Delete_removes_from_provider_and_cache()
    {
        var ev = await _editor.CreateAsync(Primary, Draft(), CancellationToken.None);

        await _editor.DeleteAsync(ev, CancellationToken.None);

        Assert.Empty(await Cached());
        Assert.Equal("delete server-1", _provider.Calls[^1]);
    }

    [Fact]
    public async Task Provider_failure_leaves_the_cache_untouched()
    {
        var ev = await _editor.CreateAsync(Primary, Draft(), CancellationToken.None);
        _provider.Fail = new HttpRequestException("offline");

        await Assert.ThrowsAsync<HttpRequestException>(() => _editor.UpdateAsync(ev, Draft("Renamed"), CancellationToken.None));
        await Assert.ThrowsAsync<HttpRequestException>(() => _editor.DeleteAsync(ev, CancellationToken.None));

        Assert.Equal("New", Assert.Single(await Cached()).Title);
    }

    [Fact]
    public async Task Occurrence_is_passed_as_is_so_only_that_instance_changes()
    {
        var occurrence = new CalendarEvent
        {
            AccountId = Me.Id, CalendarId = Primary.Id, Id = "series_20261127", Title = "Standup",
            Start = T, End = T.AddMinutes(15), SeriesId = "series",
        };
        await _store.UpsertEventAsync(occurrence);

        await _editor.DeleteAsync(occurrence, CancellationToken.None);

        Assert.Equal("delete series_20261127", _provider.Calls[^1]);
        Assert.Empty(await Cached());
    }

    [Fact]
    public async Task Unknown_account_is_rejected()
    {
        var stray = new CalendarEvent { AccountId = "x:1", CalendarId = "c", Id = "e", Title = "t", Start = T, End = T };
        await Assert.ThrowsAsync<InvalidOperationException>(() => _editor.DeleteAsync(stray, CancellationToken.None));
    }

    private sealed class FakeProvider : ICalendarProvider
    {
        private int _next;
        public List<string> Calls { get; } = [];
        public Exception? Fail { get; set; }
        public string ProviderId => "fake";

        private CalendarEvent Ev(string id, string calendarId, EventDraft d) => new()
        {
            AccountId = Me.Id, CalendarId = calendarId, Id = id, Title = d.Title, Start = d.Start, End = d.End,
            IsAllDay = d.IsAllDay,
        };

        public Task<CalendarEvent> CreateEventAsync(Account a, CalendarInfo c, EventDraft d, CancellationToken ct)
        {
            if (Fail is not null) return Task.FromException<CalendarEvent>(Fail);
            Calls.Add($"create {c.Id} {d.Title}");
            return Task.FromResult(Ev($"server-{++_next}", c.Id, d));
        }

        public Task<CalendarEvent> UpdateEventAsync(Account a, CalendarEvent e, EventDraft d, CancellationToken ct)
        {
            if (Fail is not null) return Task.FromException<CalendarEvent>(Fail);
            Calls.Add($"update {e.Id} {d.Title}");
            return Task.FromResult(Ev(e.Id, e.CalendarId, d));
        }

        public Task DeleteEventAsync(Account a, CalendarEvent e, CancellationToken ct)
        {
            if (Fail is not null) return Task.FromException(Fail);
            Calls.Add($"delete {e.Id}");
            return Task.CompletedTask;
        }

        public Task<Account> SignInAsync(string? loginHint, CancellationToken ct) => throw new NotSupportedException();
        public Task SignOutAsync(Account account, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(Account a, CancellationToken ct) => throw new NotSupportedException();
        public Task<SyncResult> SyncEventsAsync(Account a, CalendarInfo c, string? s, DateRange w, CancellationToken ct) => throw new NotSupportedException();
        public Task RespondAsync(Account a, CalendarEvent e, RsvpStatus s, CancellationToken ct) => throw new NotSupportedException();
    }
}
