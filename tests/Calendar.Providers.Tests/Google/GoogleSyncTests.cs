using System.Net;
using System.Text;
using System.Web;
using Calendar.Core.Models;
using Calendar.Core.Providers;
using Calendar.Providers.Google;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Http;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Time.Testing;

namespace Calendar.Providers.Tests.Google;

public class GoogleSyncStateTests
{
    [Fact]
    public void Round_trip()
    {
        var state = new GoogleSyncState(
            new DateTimeOffset(2026, 11, 20, 1, 2, 3, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 26, 4, 5, 6, TimeSpan.Zero));
        Assert.Equal(state, GoogleSyncState.TryParse(state.Serialize()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("raw-google-token")]
    [InlineData("v1|100|token")]      // older format → full sync
    [InlineData("v2|")]
    [InlineData("v2|100")]
    [InlineData("v2|100|")]
    [InlineData("v2|abc|200")]
    [InlineData("v2|100|200|300")]
    [InlineData("v2|200|100")]        // last sync before last full sync
    public void Invalid_state_is_null(string? value) => Assert.Null(GoogleSyncState.TryParse(value));
}
/// <summary>Drives <see cref="GoogleCalendarProvider.SyncEventsAsync"/> against a fake HTTP server.</summary>
public sealed class GoogleSyncTests
{
    private static readonly Account Me = new("google:me@gmail.com", "google", "me@gmail.com", "me@gmail.com", "Me");
    private static readonly CalendarInfo Primary = new(Me.Id, "me@gmail.com", "Me", "#039BE5", true, true);
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 2, 0, 0, TimeSpan.Zero);
    private static readonly DateRange Window = new(Now.AddDays(-90), Now.AddDays(365));

    private readonly FakeTimeProvider _time = new(Now);
    private readonly FakeGoogle _server = new();
    private readonly GoogleCalendarProvider _provider;

    public GoogleSyncTests()
    {
        var tokens = new MemoryDataStore();
        tokens.StoreAsync(Me.ProviderAccountId, new TokenResponse
        {
            AccessToken = "access", RefreshToken = "refresh", ExpiresInSeconds = 3600, IssuedUtc = DateTime.UtcNow,
        }).Wait();
        _provider = new GoogleCalendarProvider(new GoogleOAuthOptions("id", "secret"), tokens, _time, _server);
    }

    private const string Timed = """{ "id": "e1", "status": "confirmed", "summary": "Standup", "start": { "dateTime": "2026-11-26T09:00:00+07:00" }, "end": { "dateTime": "2026-11-26T09:15:00+07:00" } }""";

    private static string Page(string items, string? nextPage = null) =>
        $$"""{ "items": [{{items}}] {{(nextPage is null ? "" : $", \"nextPageToken\": \"{nextPage}\"")}} }""";

    [Fact]
    public async Task First_sync_is_full_over_the_window_and_returns_state()
    {
        _server.Respond(_ => Page(Timed));

        var result = await _provider.SyncEventsAsync(Me, Primary, null, Window, CancellationToken.None);

        Assert.True(result.IsFullSync);
        Assert.Equal(["e1"], result.Upserted.Select(e => e.Id));
        Assert.Equal(new GoogleSyncState(Now, Now), GoogleSyncState.TryParse(result.NewSyncState));

        var q = _server.Requests.Single();
        Assert.Equal("true", q["singleEvents"]);
        Assert.Equal("false", q["showDeleted"]);
        Assert.NotNull(q["timeMin"]);
        Assert.NotNull(q["timeMax"]);
        Assert.Null(q["updatedMin"]);
    }

    [Fact]
    public async Task Full_sync_follows_pages()
    {
        _server.Respond(q => q["pageToken"] is null
            ? Page(Timed, nextPage: "p2")
            : Page(Timed.Replace("\"e1\"", "\"e2\"")));

        var result = await _provider.SyncEventsAsync(Me, Primary, null, Window, CancellationToken.None);

        Assert.Equal(["e1", "e2"], result.Upserted.Select(e => e.Id));
        Assert.Equal(2, _server.Requests.Count);
        Assert.Equal("p2", _server.Requests[1]["pageToken"]);
    }

    [Fact]
    public async Task Next_sync_is_incremental_inside_the_window_and_reports_deletions()
    {
        var lastFull = Now.AddDays(-1);
        var lastSync = Now.AddMinutes(-5);
        var state = new GoogleSyncState(lastFull, lastSync).Serialize();
        _server.Respond(_ => Page(Timed + """, { "id": "gone", "status": "cancelled" }"""));

        var result = await _provider.SyncEventsAsync(Me, Primary, state, Window, CancellationToken.None);

        Assert.False(result.IsFullSync);
        Assert.Equal(["e1"], result.Upserted.Select(e => e.Id));
        Assert.Equal(["gone"], result.DeletedEventIds);
        // Keeps the time of the last full sync, moves the last sync to now.
        Assert.Equal(new GoogleSyncState(lastFull, Now), GoogleSyncState.TryParse(result.NewSyncState));

        var q = _server.Requests.Single();
        Assert.Equal("true", q["singleEvents"]);
        Assert.Equal("true", q["showDeleted"]);
        Assert.NotNull(q["timeMin"]);
        Assert.NotNull(q["timeMax"]);
        Assert.Equal(lastSync - GoogleCalendarProvider.UpdatedMinOverlap,
            DateTimeOffset.Parse(q["updatedMin"]!, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Gone_410_asks_for_full_resync()
    {
        var state = new GoogleSyncState(Now.AddDays(-1), Now.AddMinutes(-5)).Serialize();
        _server.Respond(_ => throw new GoneException());

        var result = await _provider.SyncEventsAsync(Me, Primary, state, Window, CancellationToken.None);

        Assert.True(result.FullResyncRequired);
    }

    [Fact]
    public async Task Old_full_sync_forces_a_new_full_sync_to_move_the_window()
    {
        var state = new GoogleSyncState(Now - GoogleCalendarProvider.FullSyncInterval, Now.AddMinutes(-5)).Serialize();
        _server.Respond(_ => Page(Timed));

        var result = await _provider.SyncEventsAsync(Me, Primary, state, Window, CancellationToken.None);

        Assert.True(result.IsFullSync);
        Assert.Null(_server.Requests.Single()["updatedMin"]);
        Assert.Equal(new GoogleSyncState(Now, Now), GoogleSyncState.TryParse(result.NewSyncState));
    }
    [Fact]
    public async Task Corrupt_state_falls_back_to_full_sync()
    {
        _server.Respond(_ => Page(Timed));
        var result = await _provider.SyncEventsAsync(Me, Primary, "garbage", Window, CancellationToken.None);
        Assert.True(result.IsFullSync);
    }

    [Fact]
    public async Task Unauthorized_maps_to_reauthentication()
    {
        _server.Respond(_ => throw new UnauthorizedException());
        await Assert.ThrowsAsync<ReauthenticationRequiredException>(() =>
            _provider.SyncEventsAsync(Me, Primary, null, Window, CancellationToken.None));
    }

    [Fact]
    public async Task Missing_token_maps_to_reauthentication_without_network()
    {
        var provider = new GoogleCalendarProvider(new GoogleOAuthOptions("id", "secret"), new MemoryDataStore(), _time, _server);
        await Assert.ThrowsAsync<ReauthenticationRequiredException>(() =>
            provider.SyncEventsAsync(Me, Primary, null, Window, CancellationToken.None));
        Assert.Empty(_server.Requests);
    }

    // ---- Fakes ------------------------------------------------------------------------------

    private sealed class GoneException : Exception;
    private sealed class UnauthorizedException : Exception;

    /// <summary>Answers Events.list calls; records each request's query string.</summary>
    private sealed class FakeGoogle : HttpClientFactory
    {
        private Func<System.Collections.Specialized.NameValueCollection, string> _respond = _ => Page("");
        public List<System.Collections.Specialized.NameValueCollection> Requests { get; } = [];

        public void Respond(Func<System.Collections.Specialized.NameValueCollection, string> respond) => _respond = respond;

        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => new Handler(this);

        private sealed class Handler(FakeGoogle owner) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                var query = HttpUtility.ParseQueryString(request.RequestUri!.Query);
                owner.Requests.Add(query);
                try
                {
                    return Task.FromResult(Json(HttpStatusCode.OK, owner._respond(query)));
                }
                catch (GoneException)
                {
                    return Task.FromResult(Json(HttpStatusCode.Gone,
                        """{ "error": { "code": 410, "message": "Sync token is no longer valid, a full sync is required.", "errors": [ { "reason": "fullSyncRequired" } ] } }"""));
                }
                catch (UnauthorizedException)
                {
                    return Task.FromResult(Json(HttpStatusCode.Unauthorized,
                        """{ "error": { "code": 401, "message": "Invalid Credentials" } }"""));
                }
            }

            private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
                new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class MemoryDataStore : IDataStore
    {
        private readonly Dictionary<string, object?> _items = new();
        public Task StoreAsync<T>(string key, T value) { _items[typeof(T).Name + key] = value; return Task.CompletedTask; }
        public Task DeleteAsync<T>(string key) { _items.Remove(typeof(T).Name + key); return Task.CompletedTask; }
        public Task<T> GetAsync<T>(string key) => Task.FromResult(_items.TryGetValue(typeof(T).Name + key, out var v) ? (T)v! : default!);
        public Task ClearAsync() { _items.Clear(); return Task.CompletedTask; }
    }
}
