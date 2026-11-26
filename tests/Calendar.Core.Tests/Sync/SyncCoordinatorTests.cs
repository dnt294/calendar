using System.Net.Sockets;
using Calendar.Core.Models;
using Calendar.Core.Providers;
using Calendar.Core.Sync;
using Microsoft.Extensions.Time.Testing;

namespace Calendar.Core.Tests.Sync;

public class SyncCoordinatorTests
{
    private static readonly Account A = new("g:a", "g", "a", "a@x.com", "A");
    private static readonly Account B = new("g:b", "g", "b", "b@x.com", "B");

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 26, 9, 0, 0, TimeSpan.Zero));
    private int _calls;
    private Func<IReadOnlyList<AccountSyncResult>> _results = () => [new AccountSyncResult(A, null)];
    private TaskCompletionSource? _hold;

    private SyncCoordinator Create() => new(async ct =>
    {
        Interlocked.Increment(ref _calls);
        if (_hold is not null)
            await _hold.Task.WaitAsync(ct);
        return _results();
    }, _time);

    [Fact]
    public async Task Concurrent_requests_share_one_run()
    {
        using var sync = Create();
        _hold = new TaskCompletionSource();

        var first = sync.SyncNowAsync();
        var second = sync.SyncNowAsync();
        Assert.True(sync.IsSyncing);
        _hold.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, _calls);
        Assert.Same(await first, await second);
        Assert.False(sync.IsSyncing);
        Assert.Equal(1, sync.CompletedRuns);
    }

    [Fact]
    public async Task Completed_runs_counts_every_run_including_failures()
    {
        using var sync = Create();
        await sync.SyncNowAsync();
        _results = () => throw new InvalidOperationException("boom");
        await sync.SyncNowAsync();
        Assert.Equal(2, sync.CompletedRuns);
    }

    [Fact]
    public async Task A_new_request_after_completion_runs_again()
    {
        using var sync = Create();
        await sync.SyncNowAsync();
        await sync.SyncNowAsync();
        Assert.Equal(2, _calls);
    }

    [Fact]
    public async Task Success_sets_last_synced_and_online()
    {
        using var sync = Create();
        var changes = 0;
        sync.StateChanged += (_, _) => changes++;

        await sync.SyncNowAsync();

        Assert.Equal(_time.GetUtcNow(), sync.LastSyncedAt);
        Assert.False(sync.IsOffline);
        Assert.True(changes >= 2); // started + finished
    }

    [Fact]
    public async Task All_network_failures_mean_offline_and_keep_last_synced()
    {
        using var sync = Create();
        await sync.SyncNowAsync();
        var lastOk = sync.LastSyncedAt;

        _time.Advance(TimeSpan.FromMinutes(5));
        _results = () =>
        [
            new AccountSyncResult(A, new HttpRequestException("no route")),
            new AccountSyncResult(B, new HttpRequestException("wrap", new SocketException())),
        ];
        await sync.SyncNowAsync();

        Assert.True(sync.IsOffline);
        Assert.Equal(lastOk, sync.LastSyncedAt);

        _results = () => [new AccountSyncResult(A, null)];
        await sync.SyncNowAsync();
        Assert.False(sync.IsOffline);
        Assert.Equal(_time.GetUtcNow(), sync.LastSyncedAt);
    }

    [Fact]
    public async Task Auth_failure_is_not_offline()
    {
        using var sync = Create();
        _results = () => [new AccountSyncResult(A, new ReauthenticationRequiredException(A.Id))];

        var results = await sync.SyncNowAsync();

        Assert.False(sync.IsOffline);
        Assert.Same(results, sync.LastResults);
    }

    [Fact]
    public async Task No_accounts_counts_as_synced()
    {
        using var sync = Create();
        _results = () => [];
        await sync.SyncNowAsync();
        Assert.Equal(_time.GetUtcNow(), sync.LastSyncedAt);
        Assert.False(sync.IsOffline);
    }

    [Fact]
    public async Task Periodic_timer_triggers_sync()
    {
        using var sync = Create();
        sync.Start(TimeSpan.FromMinutes(5));

        _time.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(0, _calls);
        _time.Advance(TimeSpan.FromMinutes(1));
        await sync.WhenIdleAsync();
        Assert.Equal(1, _calls);
        _time.Advance(TimeSpan.FromMinutes(5));
        await sync.WhenIdleAsync();
        Assert.Equal(2, _calls);
    }

    [Fact]
    public async Task SyncIfStale_only_runs_when_last_sync_is_old_enough()
    {
        using var sync = Create();
        await sync.SyncIfStaleAsync(TimeSpan.FromMinutes(1)); // never synced → runs
        Assert.Equal(1, _calls);

        _time.Advance(TimeSpan.FromSeconds(30));
        await sync.SyncIfStaleAsync(TimeSpan.FromMinutes(1));
        Assert.Equal(1, _calls);

        _time.Advance(TimeSpan.FromSeconds(31));
        await sync.SyncIfStaleAsync(TimeSpan.FromMinutes(1));
        Assert.Equal(2, _calls);
    }

    [Fact]
    public async Task Exclusive_operation_waits_for_running_sync_and_blocks_new_ones()
    {
        using var sync = Create();
        _hold = new TaskCompletionSource();
        var running = sync.SyncNowAsync();

        var order = new List<string>();
        var exclusive = sync.RunExclusiveAsync(async _ =>
        {
            order.Add("exclusive-start");
            await Task.Yield();
            order.Add("exclusive-end");
            return 42;
        }, CancellationToken.None);

        Assert.Empty(order);           // waits for the sync
        _hold.SetResult();
        await running;
        Assert.Equal(42, await exclusive);
        Assert.Equal(["exclusive-start", "exclusive-end"], order);
    }

    [Fact]
    public async Task Sync_exception_is_reported_not_thrown_and_state_recovers()
    {
        using var sync = new SyncCoordinator(_ => throw new InvalidOperationException("db locked"), _time);

        var results = await sync.SyncNowAsync();

        Assert.Empty(results);
        Assert.IsType<InvalidOperationException>(sync.LastError);
        Assert.False(sync.IsSyncing);
    }

    [Theory]
    [MemberData(nameof(NetworkCases))]
    public void Network_error_classification(Exception error, bool expected) =>
        Assert.Equal(expected, SyncErrors.IsNetworkError(error));

    public static TheoryData<Exception, bool> NetworkCases() => new()
    {
        { new HttpRequestException("x"), true },
        { new InvalidOperationException("wrap", new HttpRequestException("x")), true },
        { new SocketException(), true },
        { new TaskCanceledException("HttpClient timeout"), true },
        { new ReauthenticationRequiredException("a"), false },
        { new InvalidOperationException("bug"), false },
    };
}
