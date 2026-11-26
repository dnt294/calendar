using System.Net.Sockets;
using Calendar.Core.Providers;

namespace Calendar.Core.Sync;

public static class SyncErrors
{
    /// <summary>True when the failure means "no network / server unreachable", not a bug or an auth problem.</summary>
    public static bool IsNetworkError(Exception error)
    {
        for (var e = error; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                case ReauthenticationRequiredException:
                    return false;
                case HttpRequestException or SocketException:
                    return true;
                // HttpClient reports its own timeout as a TaskCanceledException.
                case TaskCanceledException tce when !tce.CancellationToken.IsCancellationRequested:
                    return true;
            }
        }
        return false;
    }
}

/// <summary>
/// Decides when to sync and makes sure syncs never overlap: periodic timer, "sync if stale" on window
/// focus, manual "sync now". Concurrent requests join the run in progress. Account changes (sign-in,
/// removal) go through <see cref="RunExclusiveAsync{T}"/> so they never interleave with a sync.
/// </summary>
public sealed class SyncCoordinator : IDisposable
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<AccountSyncResult>>> _syncAll;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _disposed = new();
    private readonly object _lock = new();
    private Task<IReadOnlyList<AccountSyncResult>>? _inFlight;
    private ITimer? _timer;

    /// <param name="syncAll">Syncs every account; usually <see cref="SyncEngine.SyncAllAsync"/>.</param>
    public SyncCoordinator(Func<CancellationToken, Task<IReadOnlyList<AccountSyncResult>>> syncAll, TimeProvider time)
    {
        _syncAll = syncAll;
        _time = time;
    }

    /// <summary>Raised (on any thread) when <see cref="IsSyncing"/> or the last results change.</summary>
    public event EventHandler? StateChanged;

    public bool IsSyncing { get; private set; }

    /// <summary>Last time a sync reached the server (at least one account succeeded, or there are none).</summary>
    public DateTimeOffset? LastSyncedAt { get; private set; }

    /// <summary>The last sync failed for every account because of the network.</summary>
    public bool IsOffline { get; private set; }

    public IReadOnlyList<AccountSyncResult> LastResults { get; private set; } = [];

    /// <summary>Unexpected failure of the whole run (not per account), e.g. the local database.</summary>
    public Exception? LastError { get; private set; }

    /// <summary>
    /// Number of finished runs. Listeners compare it with the last value they handled, because
    /// "started" and "finished" notifications may both be observed after a fast run has ended.
    /// </summary>
    public int CompletedRuns { get; private set; }

    /// <summary>Syncs periodically until disposed. The first tick is one interval from now.</summary>
    public void Start(TimeSpan interval)
    {
        _timer?.Dispose();
        _timer = _time.CreateTimer(_ => _ = SyncNowAsync(), null, interval, interval);
    }

    /// <summary>Starts a sync, or joins the one in progress. Never throws for sync failures.</summary>
    public Task<IReadOnlyList<AccountSyncResult>> SyncNowAsync()
    {
        lock (_lock)
        {
            if (_inFlight is { IsCompleted: false })
                return _inFlight;
            return _inFlight = RunAsync();
        }
    }

    /// <summary>Syncs only if the last successful sync is older than <paramref name="maxAge"/> (e.g. on window focus).</summary>
    public Task SyncIfStaleAsync(TimeSpan maxAge)
    {
        if (LastSyncedAt is { } last && _time.GetUtcNow() - last < maxAge)
            return Task.CompletedTask;
        return SyncNowAsync();
    }

    /// <summary>Completes when no sync is running (for tests and shutdown).</summary>
    public Task WhenIdleAsync()
    {
        lock (_lock)
            return _inFlight ?? Task.CompletedTask;
    }

    /// <summary>Runs an account operation while no sync is running; syncs requested meanwhile wait.</summary>
    public async Task<T> RunExclusiveAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return await operation(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<AccountSyncResult>> RunAsync()
    {
        await _gate.WaitAsync(_disposed.Token);
        try
        {
            IsSyncing = true;
            RaiseStateChanged();

            IReadOnlyList<AccountSyncResult> results;
            try
            {
                results = await _syncAll(_disposed.Token);
                LastError = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !_disposed.IsCancellationRequested)
            {
                results = [];
                LastError = ex;
            }

            LastResults = results;
            if (LastError is null)
            {
                IsOffline = results.Count > 0 && results.All(r => r.Error is not null && SyncErrors.IsNetworkError(r.Error));
                if (results.Count == 0 || results.Any(r => r.Succeeded))
                    LastSyncedAt = _time.GetUtcNow();
            }
            return results;
        }
        finally
        {
            IsSyncing = false;
            CompletedRuns++;
            _gate.Release();
            RaiseStateChanged();
        }
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        _timer?.Dispose();
        _disposed.Cancel();
    }
}
