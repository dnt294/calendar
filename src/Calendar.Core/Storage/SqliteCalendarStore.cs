using System.Text.Json;
using Calendar.Core.Models;
using Calendar.Core.Providers;
using Microsoft.Data.Sqlite;

namespace Calendar.Core.Storage;

/// <summary>SQLite implementation of <see cref="ICalendarStore"/>. One connection per operation.</summary>
public sealed class SqliteCalendarStore : ICalendarStore
{
    private const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _connectionString;

    /// <param name="databasePath">File path of the database; created if missing.</param>
    public SqliteCalendarStore(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        var version = Convert.ToInt32(await ScalarAsync(conn, null, "PRAGMA user_version;", ct));
        if (version > SchemaVersion)
            throw new InvalidOperationException($"Database schema v{version} is newer than this app (v{SchemaVersion}).");
        if (version == SchemaVersion)
            return;

        // journal_mode cannot be changed inside a transaction; it is persisted in the file.
        await ExecAsync(conn, null, "PRAGMA journal_mode = WAL;", ct);

        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);
        await ExecAsync(conn, tx, $"""
            CREATE TABLE accounts (
                id                  TEXT PRIMARY KEY,
                provider_id         TEXT NOT NULL,
                provider_account_id TEXT NOT NULL,
                email               TEXT NOT NULL,
                display_name        TEXT NOT NULL
            );

            CREATE TABLE calendars (
                account_id     TEXT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
                id             TEXT NOT NULL,
                name           TEXT NOT NULL,
                color          TEXT NOT NULL,
                is_primary     INTEGER NOT NULL,
                can_edit       INTEGER NOT NULL,
                is_enabled     INTEGER NOT NULL DEFAULT 1,
                color_override TEXT,
                PRIMARY KEY (account_id, id)
            );

            CREATE TABLE events (
                account_id       TEXT NOT NULL,
                calendar_id      TEXT NOT NULL,
                id               TEXT NOT NULL,
                title            TEXT NOT NULL,
                start_ms         INTEGER NOT NULL,
                start_offset_min INTEGER NOT NULL,
                end_ms           INTEGER NOT NULL,
                end_offset_min   INTEGER NOT NULL,
                is_all_day       INTEGER NOT NULL,
                location         TEXT,
                description      TEXT,
                series_id        TEXT,
                meeting_url      TEXT,
                organizer_email  TEXT,
                is_organizer     INTEGER NOT NULL,
                self_rsvp        INTEGER NOT NULL,
                attendees_json   TEXT NOT NULL,
                reminders_json   TEXT NOT NULL,
                etag             TEXT,
                updated_ms       INTEGER,
                PRIMARY KEY (account_id, calendar_id, id),
                FOREIGN KEY (account_id, calendar_id) REFERENCES calendars(account_id, id) ON DELETE CASCADE
            );
            CREATE INDEX ix_events_start ON events(start_ms);

            CREATE TABLE sync_state (
                account_id  TEXT NOT NULL,
                calendar_id TEXT NOT NULL,
                state       TEXT NOT NULL,
                PRIMARY KEY (account_id, calendar_id),
                FOREIGN KEY (account_id, calendar_id) REFERENCES calendars(account_id, id) ON DELETE CASCADE
            );

            PRAGMA user_version = {SchemaVersion};
            """, ct);
        await tx.CommitAsync(ct);
    }

    // ---- Accounts ---------------------------------------------------------------------------

    public async Task UpsertAccountAsync(Account account, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await ExecAsync(conn, null, """
            INSERT INTO accounts (id, provider_id, provider_account_id, email, display_name)
            VALUES ($id, $provider, $providerAccount, $email, $name)
            ON CONFLICT(id) DO UPDATE SET
                provider_id = excluded.provider_id,
                provider_account_id = excluded.provider_account_id,
                email = excluded.email,
                display_name = excluded.display_name;
            """, ct,
            ("$id", account.Id), ("$provider", account.ProviderId), ("$providerAccount", account.ProviderAccountId),
            ("$email", account.Email), ("$name", account.DisplayName));
    }

    public async Task<IReadOnlyList<Account>> GetAccountsAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = Command(conn, null,
            "SELECT id, provider_id, provider_account_id, email, display_name FROM accounts ORDER BY email;");
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<Account>();
        while (await r.ReadAsync(ct))
            list.Add(new Account(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4)));
        return list;
    }

    public async Task DeleteAccountAsync(string accountId, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await ExecAsync(conn, null, "DELETE FROM accounts WHERE id = $id;", ct, ("$id", accountId));
    }

    // ---- Calendars --------------------------------------------------------------------------

    public async Task ReplaceCalendarsAsync(string accountId, IReadOnlyList<CalendarInfo> calendars,
        CancellationToken ct = default)
    {
        if (calendars.Any(c => c.AccountId != accountId))
            throw new ArgumentException("All calendars must belong to the given account.", nameof(calendars));

        await using var conn = await OpenAsync(ct);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

        var keep = calendars.Select(c => c.Id).ToHashSet();
        var existing = new List<string>();
        await using (var cmd = Command(conn, tx, "SELECT id FROM calendars WHERE account_id = $a;", ("$a", accountId)))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                existing.Add(r.GetString(0));

        foreach (var id in existing.Where(id => !keep.Contains(id)))
            await ExecAsync(conn, tx, "DELETE FROM calendars WHERE account_id = $a AND id = $id;", ct,
                ("$a", accountId), ("$id", id));

        // Provider fields are refreshed; local preferences (is_enabled, color_override) are left untouched.
        foreach (var c in calendars)
            await ExecAsync(conn, tx, """
                INSERT INTO calendars (account_id, id, name, color, is_primary, can_edit, is_enabled, color_override)
                VALUES ($a, $id, $name, $color, $primary, $canEdit, $enabled, $override)
                ON CONFLICT(account_id, id) DO UPDATE SET
                    name = excluded.name,
                    color = excluded.color,
                    is_primary = excluded.is_primary,
                    can_edit = excluded.can_edit;
                """, ct,
                ("$a", accountId), ("$id", c.Id), ("$name", c.Name), ("$color", c.Color),
                ("$primary", c.IsPrimary), ("$canEdit", c.CanEdit), ("$enabled", c.IsEnabled),
                ("$override", c.ColorOverride));

        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = Command(conn, null, """
            SELECT account_id, id, name, color, is_primary, can_edit, is_enabled, color_override
            FROM calendars ORDER BY account_id, is_primary DESC, name;
            """);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<CalendarInfo>();
        while (await r.ReadAsync(ct))
            list.Add(new CalendarInfo(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                r.GetBoolean(4), r.GetBoolean(5))
            {
                IsEnabled = r.GetBoolean(6),
                ColorOverride = r.IsDBNull(7) ? null : r.GetString(7),
            });
        return list;
    }

    public async Task SetCalendarPreferencesAsync(string accountId, string calendarId, bool isEnabled,
        string? colorOverride, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        var rows = await ExecAsync(conn, null, """
            UPDATE calendars SET is_enabled = $enabled, color_override = $override
            WHERE account_id = $a AND id = $id;
            """, ct, ("$enabled", isEnabled), ("$override", colorOverride), ("$a", accountId), ("$id", calendarId));
        if (rows == 0)
            throw new KeyNotFoundException($"Calendar '{calendarId}' of account '{accountId}' not found.");
    }

    // ---- Sync -------------------------------------------------------------------------------

    public async Task ApplySyncResultAsync(string accountId, string calendarId, SyncResult result,
        CancellationToken ct = default)
    {
        if (result.FullResyncRequired)
            throw new ArgumentException("A result that requires a full resync carries no data to apply.", nameof(result));

        await using var conn = await OpenAsync(ct);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

        if (result.IsFullSync)
            await ExecAsync(conn, tx, "DELETE FROM events WHERE account_id = $a AND calendar_id = $c;", ct,
                ("$a", accountId), ("$c", calendarId));

        foreach (var id in result.DeletedEventIds)
            await ExecAsync(conn, tx, "DELETE FROM events WHERE account_id = $a AND calendar_id = $c AND id = $id;", ct,
                ("$a", accountId), ("$c", calendarId), ("$id", id));

        foreach (var ev in result.Upserted)
        {
            if (ev.AccountId != accountId || ev.CalendarId != calendarId)
                throw new ArgumentException($"Event '{ev.Id}' does not belong to calendar '{calendarId}'.", nameof(result));
            await UpsertEventAsync(conn, tx, ev, ct);
        }

        if (result.NewSyncState is null)
            await ExecAsync(conn, tx, "DELETE FROM sync_state WHERE account_id = $a AND calendar_id = $c;", ct,
                ("$a", accountId), ("$c", calendarId));
        else
            await ExecAsync(conn, tx, """
                INSERT INTO sync_state (account_id, calendar_id, state) VALUES ($a, $c, $s)
                ON CONFLICT(account_id, calendar_id) DO UPDATE SET state = excluded.state;
                """, ct, ("$a", accountId), ("$c", calendarId), ("$s", result.NewSyncState));

        await tx.CommitAsync(ct);
    }

    public async Task<string?> GetSyncStateAsync(string accountId, string calendarId, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        return (string?)await ScalarAsync(conn, null,
            "SELECT state FROM sync_state WHERE account_id = $a AND calendar_id = $c;", ct,
            ("$a", accountId), ("$c", calendarId));
    }

    public async Task ClearSyncStateAsync(string accountId, string calendarId, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await ExecAsync(conn, null, "DELETE FROM sync_state WHERE account_id = $a AND calendar_id = $c;", ct,
            ("$a", accountId), ("$c", calendarId));
    }

    // ---- Events -----------------------------------------------------------------------------

    public async Task UpsertEventAsync(CalendarEvent ev, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await UpsertEventAsync(conn, null, ev, ct);
    }

    public async Task DeleteEventAsync(string accountId, string calendarId, string eventId,
        CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await ExecAsync(conn, null, "DELETE FROM events WHERE account_id = $a AND calendar_id = $c AND id = $id;", ct,
            ("$a", accountId), ("$c", calendarId), ("$id", eventId));
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(DateRange range, TimeZoneInfo zone,
        bool enabledCalendarsOnly = true, CancellationToken ct = default)
    {
        // All-day events are floating dates stored as UTC midnight: compare them with the range's
        // wall-clock time in the given zone. Zero-length timed events match the range that contains them.
        var floatingStart = ToFloatingMs(TimeZoneInfo.ConvertTime(range.Start, zone));
        var floatingEnd = ToFloatingMs(TimeZoneInfo.ConvertTime(range.End, zone));

        await using var conn = await OpenAsync(ct);
        await using var cmd = Command(conn, null, $"""
            SELECT e.account_id, e.calendar_id, e.id, e.title, e.start_ms, e.start_offset_min, e.end_ms,
                   e.end_offset_min, e.is_all_day, e.location, e.description, e.series_id, e.meeting_url,
                   e.organizer_email, e.is_organizer, e.self_rsvp, e.attendees_json, e.reminders_json,
                   e.etag, e.updated_ms
            FROM events e
            JOIN calendars c ON c.account_id = e.account_id AND c.id = e.calendar_id
            WHERE ((e.is_all_day = 0 AND e.start_ms < $end AND (e.end_ms > $start OR e.start_ms >= $start))
                OR (e.is_all_day = 1 AND e.start_ms < $fEnd AND e.end_ms > $fStart))
              {(enabledCalendarsOnly ? "AND c.is_enabled = 1" : "")}
            ORDER BY e.start_ms, e.end_ms DESC, e.id;
            """,
            ("$start", range.Start.ToUnixTimeMilliseconds()), ("$end", range.End.ToUnixTimeMilliseconds()),
            ("$fStart", floatingStart), ("$fEnd", floatingEnd));

        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<CalendarEvent>();
        while (await r.ReadAsync(ct))
        {
            list.Add(new CalendarEvent
            {
                AccountId = r.GetString(0),
                CalendarId = r.GetString(1),
                Id = r.GetString(2),
                Title = r.GetString(3),
                Start = FromMs(r.GetInt64(4), r.GetInt32(5)),
                End = FromMs(r.GetInt64(6), r.GetInt32(7)),
                IsAllDay = r.GetBoolean(8),
                Location = NullableString(r, 9),
                Description = NullableString(r, 10),
                SeriesId = NullableString(r, 11),
                MeetingUrl = NullableString(r, 12),
                OrganizerEmail = NullableString(r, 13),
                IsOrganizer = r.GetBoolean(14),
                SelfRsvp = (RsvpStatus)r.GetInt32(15),
                Attendees = JsonSerializer.Deserialize<List<Attendee>>(r.GetString(16), Json) ?? [],
                Reminders = JsonSerializer.Deserialize<List<Reminder>>(r.GetString(17), Json) ?? [],
                ETag = NullableString(r, 18),
                UpdatedAt = r.IsDBNull(19) ? null : DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(19)),
            });
        }
        return list;
    }

    // ---- Helpers ----------------------------------------------------------------------------

    private static Task<int> UpsertEventAsync(SqliteConnection conn, SqliteTransaction? tx, CalendarEvent ev,
        CancellationToken ct) =>
        ExecAsync(conn, tx, """
            INSERT OR REPLACE INTO events (
                account_id, calendar_id, id, title, start_ms, start_offset_min, end_ms, end_offset_min,
                is_all_day, location, description, series_id, meeting_url, organizer_email, is_organizer,
                self_rsvp, attendees_json, reminders_json, etag, updated_ms)
            VALUES ($a, $c, $id, $title, $startMs, $startOff, $endMs, $endOff, $allDay, $location, $description,
                $series, $meeting, $organizer, $isOrganizer, $rsvp, $attendees, $reminders, $etag, $updated);
            """, ct,
            ("$a", ev.AccountId), ("$c", ev.CalendarId), ("$id", ev.Id), ("$title", ev.Title),
            ("$startMs", ev.Start.ToUnixTimeMilliseconds()), ("$startOff", (int)ev.Start.Offset.TotalMinutes),
            ("$endMs", ev.End.ToUnixTimeMilliseconds()), ("$endOff", (int)ev.End.Offset.TotalMinutes),
            ("$allDay", ev.IsAllDay), ("$location", ev.Location), ("$description", ev.Description),
            ("$series", ev.SeriesId), ("$meeting", ev.MeetingUrl), ("$organizer", ev.OrganizerEmail),
            ("$isOrganizer", ev.IsOrganizer), ("$rsvp", (int)ev.SelfRsvp),
            ("$attendees", JsonSerializer.Serialize(ev.Attendees, Json)),
            ("$reminders", JsonSerializer.Serialize(ev.Reminders, Json)),
            ("$etag", ev.ETag), ("$updated", ev.UpdatedAt?.ToUnixTimeMilliseconds()));

    private static long ToFloatingMs(DateTimeOffset local) =>
        new DateTimeOffset(local.DateTime, TimeSpan.Zero).ToUnixTimeMilliseconds();

    private static DateTimeOffset FromMs(long ms, int offsetMinutes) =>
        DateTimeOffset.FromUnixTimeMilliseconds(ms).ToOffset(TimeSpan.FromMinutes(offsetMinutes));

    private static string? NullableString(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    private static SqliteCommand Command(SqliteConnection conn, SqliteTransaction? tx, string sql,
        params (string Name, object? Value)[] parameters)
    {
        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    private static async Task<int> ExecAsync(SqliteConnection conn, SqliteTransaction? tx, string sql,
        CancellationToken ct, params (string Name, object? Value)[] parameters)
    {
        await using var cmd = Command(conn, tx, sql, parameters);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<object?> ScalarAsync(SqliteConnection conn, SqliteTransaction? tx, string sql,
        CancellationToken ct, params (string Name, object? Value)[] parameters)
    {
        await using var cmd = Command(conn, tx, sql, parameters);
        var value = await cmd.ExecuteScalarAsync(ct);
        return value is DBNull ? null : value;
    }
}
