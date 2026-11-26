using Calendar.Core.Models;
using Calendar.Core.Providers;
using Calendar.Core.Storage;
using Microsoft.Data.Sqlite;

namespace Calendar.Core.Tests.Storage;

public sealed class SqliteCalendarStoreTests : IAsyncLifetime
{
    private static readonly TimeZoneInfo Hanoi = TimeZoneInfo.CreateCustomTimeZone("UTC+7", TimeSpan.FromHours(7), "UTC+7", "UTC+7");
    private static readonly TimeZoneInfo MinusFive = TimeZoneInfo.CreateCustomTimeZone("UTC-5", TimeSpan.FromHours(-5), "UTC-5", "UTC-5");

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"calendar-test-{Guid.NewGuid():N}.db");
    private readonly SqliteCalendarStore _store;

    private static readonly Account Gmail = new("google:1", "google", "1", "me@gmail.com", "Me");
    private static readonly CalendarInfo Personal = new(Gmail.Id, "primary", "Personal", "#0078D4", IsPrimary: true, CanEdit: true);
    private static readonly CalendarInfo Holidays = new(Gmail.Id, "holidays", "Holidays", "#33AA33", IsPrimary: false, CanEdit: false);

    public SqliteCalendarStoreTests() => _store = new SqliteCalendarStore(_path);

    public async Task InitializeAsync()
    {
        await _store.InitializeAsync();
        await _store.UpsertAccountAsync(Gmail);
        await _store.ReplaceCalendarsAsync(Gmail.Id, [Personal, Holidays]);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            File.Delete(_path + suffix);
        return Task.CompletedTask;
    }

    private static readonly TimeSpan Plus7 = TimeSpan.FromHours(7);

    private static CalendarEvent Timed(string id, DateTimeOffset start, TimeSpan duration, CalendarInfo? cal = null) => new()
    {
        AccountId = Gmail.Id,
        CalendarId = (cal ?? Personal).Id,
        Id = id,
        Title = id,
        Start = start,
        End = start + duration,
    };

    private static CalendarEvent AllDay(string id, DateOnly first, DateOnly last, CalendarInfo? cal = null)
    {
        var (start, end) = CalendarEvent.AllDaySpan(first, last);
        return Timed(id, start, end - start, cal) with { IsAllDay = true };
    }

    private static SyncResult Full(params CalendarEvent[] events) => new(events, [], "token-1", IsFullSync: true);

    private Task<IReadOnlyList<CalendarEvent>> EventsOn(DateOnly day, TimeZoneInfo zone) =>
        _store.GetEventsAsync(DateRange.ForDays(day, 1, zone), zone);

    [Fact]
    public async Task Initialize_is_idempotent()
    {
        await _store.InitializeAsync();
        Assert.Single(await _store.GetAccountsAsync());
    }

    [Fact]
    public async Task Event_round_trips_all_fields()
    {
        var ev = Timed("e1", new DateTimeOffset(2026, 11, 23, 9, 0, 0, Plus7), TimeSpan.FromMinutes(30)) with
        {
            Title = "Standup",
            Location = "Room 1",
            Description = "Daily",
            SeriesId = "series-1",
            MeetingUrl = "https://meet.google.com/abc",
            OrganizerEmail = "boss@example.com",
            SelfRsvp = RsvpStatus.Tentative,
            Attendees =
            [
                new Attendee("me@gmail.com", "Me", RsvpStatus.Tentative, IsSelf: true),
                new Attendee("boss@example.com", "Boss", RsvpStatus.Accepted, IsOrganizer: true, IsOptional: false),
            ],
            Reminders = [new Reminder(5), new Reminder(30)],
            ETag = "\"etag\"",
            UsesDefaultReminders = true,
            UpdatedAt = new DateTimeOffset(2026, 11, 20, 1, 2, 3, TimeSpan.Zero),
        };
        await _store.ApplySyncResultAsync(Gmail.Id, Personal.Id, Full(ev));

        var loaded = Assert.Single(await EventsOn(new DateOnly(2026, 11, 23), Hanoi));

        Assert.Equal(ev.Start, loaded.Start);
        Assert.Equal(Plus7, loaded.Start.Offset);
        Assert.Equal(ev.Attendees, loaded.Attendees);
        Assert.Equal(ev.Reminders, loaded.Reminders);
        Assert.Equal(ev with { Attendees = loaded.Attendees, Reminders = loaded.Reminders }, loaded);
    }

    [Fact]
    public async Task Migration_from_v1_adds_column_and_forces_a_full_resync()
    {
        var t = new DateTimeOffset(2026, 11, 23, 9, 0, 0, Plus7);
        await _store.ApplySyncResultAsync(Gmail.Id, Personal.Id, Full(Timed("old", t, TimeSpan.FromHours(1))));

        // Turn the database back into a v1 file.
        await using (var conn = new SqliteConnection($"Data Source={_path}"))
        {
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "ALTER TABLE events DROP COLUMN uses_default_reminders; PRAGMA user_version = 1;";
            await cmd.ExecuteNonQueryAsync();
        }

        // Pooled connections still hold the old schema in memory; a real upgrade starts with fresh ones.
        SqliteConnection.ClearAllPools();
        await new SqliteCalendarStore(_path).InitializeAsync();

        var ev = Assert.Single(await EventsOn(new DateOnly(2026, 11, 23), Hanoi));
        Assert.False(ev.UsesDefaultReminders);
        Assert.Null(await _store.GetSyncStateAsync(Gmail.Id, Personal.Id));
        await _store.UpsertEventAsync(ev with { UsesDefaultReminders = true });
        Assert.True(Assert.Single(await EventsOn(new DateOnly(2026, 11, 23), Hanoi)).UsesDefaultReminders);
    }
    [Fact]
    public async Task Range_query_is_half_open_for_timed_events()
    {
        var day = new DateOnly(2026, 11, 23);
        var midnight = new DateTimeOffset(2026, 11, 23, 0, 0, 0, Plus7);
        await _store.ApplySyncResultAsync(Gmail.Id, Personal.Id, Full(
            Timed("ends-at-midnight", midnight.AddHours(-1), TimeSpan.FromHours(1)),
            Timed("crosses-midnight", midnight.AddHours(-1), TimeSpan.FromHours(2)),
            Timed("zero-length", midnight, TimeSpan.Zero),
            Timed("noon", midnight.AddHours(12), TimeSpan.FromHours(1)),
            Timed("next-day", midnight.AddDays(1), TimeSpan.FromHours(1))));

        var ids = (await EventsOn(day, Hanoi)).Select(e => e.Id).Order();

        Assert.Equal(["crosses-midnight", "noon", "zero-length"], ids);
    }

    [Theory]
    [InlineData("UTC+7")]
    [InlineData("UTC-5")]
    public async Task All_day_events_match_by_date_in_any_zone(string zoneName)
    {
        var zone = zoneName == "UTC+7" ? Hanoi : MinusFive;
        await _store.ApplySyncResultAsync(Gmail.Id, Holidays.Id, new SyncResult(
            [
                AllDay("22nd", new DateOnly(2026, 11, 22), new DateOnly(2026, 11, 22), Holidays),
                AllDay("23rd", new DateOnly(2026, 11, 23), new DateOnly(2026, 11, 23), Holidays),
                AllDay("23rd-25th", new DateOnly(2026, 11, 23), new DateOnly(2026, 11, 25), Holidays),
                AllDay("24th", new DateOnly(2026, 11, 24), new DateOnly(2026, 11, 24), Holidays),
            ], [], null, IsFullSync: true));

        var ids = (await EventsOn(new DateOnly(2026, 11, 23), zone)).Select(e => e.Id).Order();

        Assert.Equal(["23rd", "23rd-25th"], ids);
    }

    [Fact]
    public async Task Full_sync_replaces_events_and_incremental_applies_changes()
    {
        var t = new DateTimeOffset(2026, 11, 23, 9, 0, 0, Plus7);
        await _store.ApplySyncResultAsync(Gmail.Id, Personal.Id, Full(
            Timed("a", t, TimeSpan.FromHours(1)), Timed("b", t, TimeSpan.FromHours(1))));
        Assert.Equal("token-1", await _store.GetSyncStateAsync(Gmail.Id, Personal.Id));

        await _store.ApplySyncResultAsync(Gmail.Id, Personal.Id, new SyncResult(
            [Timed("b", t, TimeSpan.FromHours(1)) with { Title = "B renamed" }, Timed("c", t, TimeSpan.FromHours(1))],
            ["a"], "token-2", IsFullSync: false));

        var events = await EventsOn(new DateOnly(2026, 11, 23), Hanoi);
        Assert.Equal(["b", "c"], events.Select(e => e.Id).Order());
        Assert.Equal("B renamed", events.Single(e => e.Id == "b").Title);
        Assert.Equal("token-2", await _store.GetSyncStateAsync(Gmail.Id, Personal.Id));

        await _store.ApplySyncResultAsync(Gmail.Id, Personal.Id, Full(Timed("d", t, TimeSpan.FromHours(1))));
        Assert.Equal(["d"], (await EventsOn(new DateOnly(2026, 11, 23), Hanoi)).Select(e => e.Id));
    }

    [Fact]
    public async Task Sync_only_touches_its_own_calendar()
    {
        var t = new DateTimeOffset(2026, 11, 23, 9, 0, 0, Plus7);
        await _store.ApplySyncResultAsync(Gmail.Id, Holidays.Id,
            new SyncResult([Timed("h", t, TimeSpan.FromHours(1), Holidays)], [], "h-token", IsFullSync: true));
        await _store.ApplySyncResultAsync(Gmail.Id, Personal.Id, Full(Timed("p", t, TimeSpan.FromHours(1))));

        Assert.Equal(["h", "p"], (await EventsOn(new DateOnly(2026, 11, 23), Hanoi)).Select(e => e.Id).Order());
        Assert.Equal("h-token", await _store.GetSyncStateAsync(Gmail.Id, Holidays.Id));
    }

    [Fact]
    public async Task Sync_rejects_event_of_another_calendar_and_writes_nothing()
    {
        var t = new DateTimeOffset(2026, 11, 23, 9, 0, 0, Plus7);
        var result = Full(Timed("ok", t, TimeSpan.FromHours(1)), Timed("wrong", t, TimeSpan.FromHours(1), Holidays));

        await Assert.ThrowsAsync<ArgumentException>(() => _store.ApplySyncResultAsync(Gmail.Id, Personal.Id, result));

        Assert.Empty(await EventsOn(new DateOnly(2026, 11, 23), Hanoi));
        Assert.Null(await _store.GetSyncStateAsync(Gmail.Id, Personal.Id));
    }

    [Fact]
    public async Task Clear_sync_state()
    {
        await _store.ApplySyncResultAsync(Gmail.Id, Personal.Id, Full());
        await _store.ClearSyncStateAsync(Gmail.Id, Personal.Id);
        Assert.Null(await _store.GetSyncStateAsync(Gmail.Id, Personal.Id));
    }

    [Fact]
    public async Task Disabled_calendars_are_filtered_unless_asked()
    {
        var t = new DateTimeOffset(2026, 11, 23, 9, 0, 0, Plus7);
        await _store.ApplySyncResultAsync(Gmail.Id, Holidays.Id,
            new SyncResult([Timed("h", t, TimeSpan.FromHours(1), Holidays)], [], null, IsFullSync: true));
        await _store.SetCalendarPreferencesAsync(Gmail.Id, Holidays.Id, isEnabled: false, colorOverride: null);

        var range = DateRange.ForDays(new DateOnly(2026, 11, 23), 1, Hanoi);
        Assert.Empty(await _store.GetEventsAsync(range, Hanoi));
        Assert.Single(await _store.GetEventsAsync(range, Hanoi, enabledCalendarsOnly: false));
    }

    [Fact]
    public async Task Replace_calendars_keeps_local_preferences_and_removes_missing()
    {
        await _store.SetCalendarPreferencesAsync(Gmail.Id, Personal.Id, isEnabled: false, colorOverride: "#FF0000");
        var t = new DateTimeOffset(2026, 11, 23, 9, 0, 0, Plus7);
        await _store.ApplySyncResultAsync(Gmail.Id, Holidays.Id,
            new SyncResult([Timed("h", t, TimeSpan.FromHours(1), Holidays)], [], "h-token", IsFullSync: true));

        // Provider renamed "Personal" and no longer returns "Holidays".
        await _store.ReplaceCalendarsAsync(Gmail.Id, [Personal with { Name = "Me", Color = "#000000" }]);

        var cal = Assert.Single(await _store.GetCalendarsAsync());
        Assert.Equal("Me", cal.Name);
        Assert.Equal("#000000", cal.Color);
        Assert.False(cal.IsEnabled);
        Assert.Equal("#FF0000", cal.EffectiveColor);

        var range = DateRange.ForDays(new DateOnly(2026, 11, 23), 1, Hanoi);
        Assert.Empty(await _store.GetEventsAsync(range, Hanoi, enabledCalendarsOnly: false));
        Assert.Null(await _store.GetSyncStateAsync(Gmail.Id, Holidays.Id));
    }

    [Fact]
    public async Task Set_preferences_of_unknown_calendar_throws()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _store.SetCalendarPreferencesAsync(Gmail.Id, "nope", true, null));
    }

    [Fact]
    public async Task Delete_account_cascades()
    {
        var t = new DateTimeOffset(2026, 11, 23, 9, 0, 0, Plus7);
        await _store.ApplySyncResultAsync(Gmail.Id, Personal.Id, Full(Timed("p", t, TimeSpan.FromHours(1))));

        await _store.DeleteAccountAsync(Gmail.Id);

        Assert.Empty(await _store.GetAccountsAsync());
        Assert.Empty(await _store.GetCalendarsAsync());
        var range = DateRange.ForDays(new DateOnly(2026, 11, 23), 1, Hanoi);
        Assert.Empty(await _store.GetEventsAsync(range, Hanoi, enabledCalendarsOnly: false));
        Assert.Null(await _store.GetSyncStateAsync(Gmail.Id, Personal.Id));
    }

    [Fact]
    public async Task Upsert_and_delete_single_event()
    {
        var t = new DateTimeOffset(2026, 11, 23, 9, 0, 0, Plus7);
        await _store.UpsertEventAsync(Timed("x", t, TimeSpan.FromHours(1)));
        await _store.UpsertEventAsync(Timed("x", t, TimeSpan.FromHours(1)) with { Title = "X2" });
        Assert.Equal("X2", Assert.Single(await EventsOn(new DateOnly(2026, 11, 23), Hanoi)).Title);

        await _store.DeleteEventAsync(Gmail.Id, Personal.Id, "x");
        Assert.Empty(await EventsOn(new DateOnly(2026, 11, 23), Hanoi));
    }

    [Fact]
    public async Task Upsert_event_of_unknown_calendar_fails()
    {
        var ev = Timed("x", DateTimeOffset.UnixEpoch, TimeSpan.FromHours(1)) with { CalendarId = "nope" };
        await Assert.ThrowsAsync<SqliteException>(() => _store.UpsertEventAsync(ev));
    }

    [Fact]
    public async Task Resync_required_result_cannot_be_applied()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _store.ApplySyncResultAsync(Gmail.Id, Personal.Id, SyncResult.ResyncRequired()));
    }
}
