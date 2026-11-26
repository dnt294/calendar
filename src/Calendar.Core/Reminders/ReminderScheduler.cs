using Calendar.Core.Models;

namespace Calendar.Core.Reminders;

/// <summary>One popup reminder of one event occurrence.</summary>
/// <param name="Key">Stable id: same event, same start, same lead time → same key (used to never fire twice).</param>
public sealed record ReminderOccurrence(string Key, CalendarEvent Event, int MinutesBefore, DateTimeOffset FireAt);

public static class ReminderPlanner
{
    /// <summary>
    /// Reminders whose fire time is in [<paramref name="from"/>, <paramref name="to"/>). Declined
    /// invites do not remind. All-day reminders count back from local midnight of the first day
    /// (Google's convention, e.g. 900 minutes = the day before at 09:00).
    /// </summary>
    public static IReadOnlyList<ReminderOccurrence> Plan(IEnumerable<CalendarEvent> events, DateTimeOffset from,
        DateTimeOffset to, TimeZoneInfo zone)
    {
        var result = new List<ReminderOccurrence>();
        foreach (var ev in events)
        {
            if (ev.SelfRsvp == RsvpStatus.Declined)
                continue;
            var start = ev.IsAllDay ? DateRange.ForDays(ev.StartDate, 1, zone).Start : ev.Start;
            foreach (var minutes in ev.Reminders.Select(r => r.MinutesBefore).Distinct())
            {
                var fireAt = start.AddMinutes(-minutes);
                if (fireAt >= from && fireAt < to)
                    result.Add(new ReminderOccurrence(KeyOf(ev, minutes), ev, minutes, fireAt));
            }
        }
        return result.OrderBy(r => r.FireAt).ThenBy(r => r.Key, StringComparer.Ordinal).ToList();
    }

    /// <summary>Next timed event that has not started yet (for the tray menu); declined invites skipped.</summary>
    public static CalendarEvent? NextUpcoming(IEnumerable<CalendarEvent> events, DateTimeOffset now) =>
        events
            .Where(e => !e.IsAllDay && e.Start > now && e.SelfRsvp != RsvpStatus.Declined)
            .OrderBy(e => e.Start)
            .FirstOrDefault();

    private static string KeyOf(CalendarEvent ev, int minutes) =>
        $"{ev.AccountId}|{ev.CalendarId}|{ev.Id}|{ev.Start.ToUnixTimeSeconds()}|{minutes}";
}

/// <summary>
/// Fires <see cref="ReminderDue"/> at each reminder's time (task 8.2). Plans from the local cache for
/// the next 24 hours; <see cref="RefreshAsync"/> re-plans after a sync or a local edit, and a reminder
/// that already fired is never fired again (except when snoozed). Reminders missed by less than
/// <see cref="Grace"/> (PC asleep, app just started) still fire.
/// </summary>
public sealed class ReminderScheduler : IDisposable
{
    public static readonly TimeSpan LookAhead = TimeSpan.FromHours(24);
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(10);

    /// <summary>Longest lead time loaded: reminders up to a week before the event.</summary>
    private static readonly TimeSpan MaxLead = TimeSpan.FromDays(8);

    private readonly Func<DateRange, CancellationToken, Task<IReadOnlyList<CalendarEvent>>> _loadEvents;
    private readonly TimeProvider _time;
    private readonly TimeZoneInfo _zone;
    private readonly object _lock = new();
    private readonly HashSet<string> _fired = [];
    private readonly Dictionary<string, ReminderOccurrence> _firedByKey = new();
    private readonly Dictionary<string, ReminderOccurrence> _snoozed = new();
    private List<ReminderOccurrence> _planned = [];
    private ITimer? _timer;
    private bool _disposed;

    /// <param name="loadEvents">Events overlapping a range, from the cache (enabled calendars).</param>
    public ReminderScheduler(Func<DateRange, CancellationToken, Task<IReadOnlyList<CalendarEvent>>> loadEvents,
        TimeProvider time, TimeZoneInfo zone)
    {
        _loadEvents = loadEvents;
        _time = time;
        _zone = zone;
    }

    /// <summary>Raised on a timer thread when a reminder is due.</summary>
    public event EventHandler<ReminderOccurrence>? ReminderDue;

    /// <summary>Re-reads the cache and re-plans (call after a sync or a local change).</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();
        var events = await _loadEvents(new DateRange(now - Grace, now + LookAhead + MaxLead), ct);
        var plan = ReminderPlanner.Plan(events, now - Grace, now + LookAhead, _zone);
        lock (_lock)
        {
            _planned = plan.Where(r => !_fired.Contains(r.Key)).ToList();
            ScheduleNextLocked();
        }
    }

    /// <summary>Fires the reminder again after <paramref name="delay"/>. False when the key is unknown.</summary>
    public bool Snooze(string key, TimeSpan delay)
    {
        lock (_lock)
        {
            if (!_firedByKey.TryGetValue(key, out var fired))
                return false;
            _snoozed[key] = fired with { FireAt = _time.GetUtcNow() + delay };
            ScheduleNextLocked();
            return true;
        }
    }

    private void ScheduleNextLocked()
    {
        _timer?.Dispose();
        _timer = null;
        if (_disposed)
            return;
        var next = _planned.Concat(_snoozed.Values).Select(r => (DateTimeOffset?)r.FireAt).Min();
        if (next is null)
            return;
        var due = next.Value - _time.GetUtcNow();
        _timer = _time.CreateTimer(_ => OnTimer(), null, due < TimeSpan.Zero ? TimeSpan.Zero : due, Timeout.InfiniteTimeSpan);
    }

    private void OnTimer()
    {
        List<ReminderOccurrence> due;
        lock (_lock)
        {
            var now = _time.GetUtcNow();
            due = _planned.Where(r => r.FireAt <= now).ToList();
            _planned.RemoveAll(r => r.FireAt <= now);
            foreach (var snoozed in _snoozed.Values.Where(r => r.FireAt <= now).ToList())
            {
                _snoozed.Remove(snoozed.Key);
                due.Add(snoozed);
            }
            foreach (var r in due)
            {
                _fired.Add(r.Key);
                _firedByKey[r.Key] = r;
            }
            ScheduleNextLocked();
        }
        foreach (var r in due.OrderBy(r => r.FireAt))
            ReminderDue?.Invoke(this, r);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
