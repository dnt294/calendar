using System.Net;
using Calendar.Core.Models;
using Calendar.Core.Providers;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using HttpClientFactory = Google.Apis.Http.IHttpClientFactory;

namespace Calendar.Providers.Google;

/// <summary>OAuth "Desktop app" client from Google Cloud Console.</summary>
public sealed record GoogleOAuthOptions(string ClientId, string ClientSecret);

/// <summary>Google Calendar API v3 provider. Tokens live in <see cref="IDataStore"/> keyed by the account email.</summary>
public sealed class GoogleCalendarProvider : ICalendarProvider
{
    public const string Id = "google";

    /// <summary>Incremental syncs never move the cache window; a full sync this often re-centers it.</summary>
    public static readonly TimeSpan FullSyncInterval = TimeSpan.FromDays(7);

    private const string ApplicationName = "personal-calendar";
    private static readonly string[] Scopes = [CalendarService.Scope.Calendar];

    private readonly GoogleOAuthOptions _options;
    private readonly IDataStore _tokens;
    private readonly TimeProvider _time;
    private readonly HttpClientFactory? _http;

    /// <param name="time">Clock for the full-sync interval (tests pass a fake).</param>
    /// <param name="httpClientFactory">HTTP override for tests; null = real network.</param>
    public GoogleCalendarProvider(GoogleOAuthOptions options, IDataStore tokens, TimeProvider? time = null,
        HttpClientFactory? httpClientFactory = null)
    {
        _options = options;
        _tokens = tokens;
        _time = time ?? TimeProvider.System;
        _http = httpClientFactory;
    }

    public string ProviderId => Id;

    // ---- Auth -------------------------------------------------------------------------------

    /// <summary>
    /// Opens the browser (loopback redirect). Always shows the account chooser, so a second Google
    /// account can be added, and always asks for consent, so Google returns a refresh token even for
    /// an account that was signed in before. The email is learned from the primary calendar id.
    /// </summary>
    public async Task<Account> SignInAsync(string? loginHint, CancellationToken ct)
    {
        var pendingKey = "pending-" + Guid.NewGuid().ToString("N");
        try
        {
            var flow = new HintedFlow(Initializer("select_account consent"), loginHint);
            var credential = await new AuthorizationCodeInstalledApp(flow, new LocalServerCodeReceiver())
                .AuthorizeAsync(pendingKey, ct);

            using var service = CreateService(credential);
            var primary = await service.CalendarList.Get("primary").ExecuteAsync(ct);
            var email = primary.Id;

            var token = await _tokens.GetAsync<TokenResponse>(pendingKey);
            await _tokens.StoreAsync(email, token);

            return new Account(Account.CreateId(Id, email), Id, email, email,
                primary.SummaryOverride ?? primary.Summary ?? email);
        }
        finally
        {
            await _tokens.DeleteAsync<TokenResponse>(pendingKey);
        }
    }

    public async Task SignOutAsync(Account account, CancellationToken ct)
    {
        var flow = CreateFlow();
        var token = await flow.LoadTokenAsync(account.ProviderAccountId, ct);
        if (token is not null)
        {
            try
            {
                await flow.RevokeTokenAsync(account.ProviderAccountId, token.RefreshToken ?? token.AccessToken, ct);
            }
            catch (Exception ex) when (ex is TokenResponseException or HttpRequestException)
            {
                // Already revoked or offline: still forget it locally.
            }
        }
        await _tokens.DeleteAsync<TokenResponse>(account.ProviderAccountId);
    }

    // ---- Read -------------------------------------------------------------------------------

    public Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(Account account, CancellationToken ct) =>
        WithService(account, ct, async service =>
        {
            var result = new List<CalendarInfo>();
            string? pageToken = null;
            do
            {
                var request = service.CalendarList.List();
                request.PageToken = pageToken;
                var page = await request.ExecuteAsync(ct);
                result.AddRange((page.Items ?? []).Where(c => c.Deleted != true && c.Hidden != true)
                    .Select(c => GoogleMapper.ToCalendar(c, account.Id)));
                pageToken = page.NextPageToken;
            } while (pageToken is not null);
            return (IReadOnlyList<CalendarInfo>)result;
        });

    /// <summary>Overlap for <c>updatedMin</c>, so clock skew between this PC and Google never loses a change.</summary>
    public static readonly TimeSpan UpdatedMinOverlap = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Recurring events are expanded (<c>singleEvents=true</c>), always inside <paramref name="window"/>.
    /// Without a usable state, or when the last full sync is older than <see cref="FullSyncInterval"/>,
    /// this is a full sync; otherwise it fetches only events changed or deleted since the last sync
    /// (<c>updatedMin</c>, see <see cref="GoogleSyncState"/>). HTTP 410 yields
    /// <see cref="SyncResult.FullResyncRequired"/>.
    /// </summary>
    public Task<SyncResult> SyncEventsAsync(Account account, CalendarInfo calendar, string? syncState,
        DateRange window, CancellationToken ct) =>
        WithService(account, ct, async service =>
        {
            var now = _time.GetUtcNow();
            var state = GoogleSyncState.TryParse(syncState);
            var incremental = state is not null && now - state.LastFullSync < FullSyncInterval;

            var upserts = new List<CalendarEvent>();
            var deleted = new List<string>();
            string? pageToken = null;
            do
            {
                var request = service.Events.List(calendar.Id);
                request.SingleEvents = true;
                request.MaxResults = 2500;
                request.PageToken = pageToken;
                request.TimeMinDateTimeOffset = window.Start;
                request.TimeMaxDateTimeOffset = window.End;
                if (incremental)
                {
                    // Deleted events since updatedMin are always included, as status "cancelled".
                    request.UpdatedMinDateTimeOffset = state!.LastSync - UpdatedMinOverlap;
                    request.ShowDeleted = true;
                }
                else
                {
                    request.ShowDeleted = false;
                }

                Events page;
                try
                {
                    page = await request.ExecuteAsync(ct);
                }
                catch (GoogleApiException ex) when (incremental && ex.HttpStatusCode == HttpStatusCode.Gone)
                {
                    return SyncResult.ResyncRequired();
                }

                foreach (var item in page.Items ?? [])
                {
                    if (GoogleMapper.ToEvent(item, account.Id, calendar.Id, page.DefaultReminders) is { } ev)
                        upserts.Add(ev);
                    else if (incremental)
                        deleted.Add(item.Id);
                }
                pageToken = page.NextPageToken;
            } while (pageToken is not null);

            var newState = new GoogleSyncState(incremental ? state!.LastFullSync : now, now).Serialize();
            return new SyncResult(upserts, deleted, newState, IsFullSync: !incremental);
        });

    // ---- Write (Phase 6 / 7) ----------------------------------------------------------------

    public Task<CalendarEvent> CreateEventAsync(Account account, CalendarInfo calendar, EventDraft draft,
        CancellationToken ct) => throw new NotSupportedException("Tạo event: task 6.2.");

    public Task<CalendarEvent> UpdateEventAsync(Account account, CalendarEvent ev, EventDraft draft,
        CancellationToken ct) => throw new NotSupportedException("Sửa event: task 6.4.");

    public Task DeleteEventAsync(Account account, CalendarEvent ev, CancellationToken ct) =>
        throw new NotSupportedException("Xoá event: task 6.4.");

    public Task RespondAsync(Account account, CalendarEvent ev, RsvpStatus status, CancellationToken ct) =>
        throw new NotSupportedException("RSVP: task 7.1.");

    // ---- Helpers ----------------------------------------------------------------------------

    private ClientSecrets Secrets => new() { ClientId = _options.ClientId, ClientSecret = _options.ClientSecret };

    private GoogleAuthorizationCodeFlow.Initializer Initializer(string? prompt = null)
    {
        var init = new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = Secrets,
            Scopes = Scopes,
            DataStore = _tokens,
            Prompt = prompt,
        };
        if (_http is not null)
            init.HttpClientFactory = _http;
        return init;
    }

    private GoogleAuthorizationCodeFlow CreateFlow() => new(Initializer());

    private CalendarService CreateService(UserCredential credential)
    {
        var init = new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = ApplicationName,
        };
        if (_http is not null)
            init.HttpClientFactory = _http;
        return new CalendarService(init);
    }

    /// <summary>Loads the stored token (never opens a browser) and maps auth failures.</summary>
    private async Task<T> WithService<T>(Account account, CancellationToken ct, Func<CalendarService, Task<T>> action)
    {
        var flow = CreateFlow();
        var token = await flow.LoadTokenAsync(account.ProviderAccountId, ct)
                    ?? throw new ReauthenticationRequiredException(account.Id);
        using var service = CreateService(new UserCredential(flow, account.ProviderAccountId, token));
        try
        {
            return await action(service);
        }
        catch (TokenResponseException ex)
        {
            throw new ReauthenticationRequiredException(account.Id, inner: ex);
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.Unauthorized)
        {
            throw new ReauthenticationRequiredException(account.Id, inner: ex);
        }
    }

    /// <summary>Pre-fills the email on Google's sign-in page when re-authenticating an account.</summary>
    private sealed class HintedFlow(GoogleAuthorizationCodeFlow.Initializer init, string? loginHint)
        : GoogleAuthorizationCodeFlow(init)
    {
        public override AuthorizationCodeRequestUrl CreateAuthorizationCodeRequest(string redirectUri)
        {
            var request = base.CreateAuthorizationCodeRequest(redirectUri);
            if (loginHint is not null && request is GoogleAuthorizationCodeRequestUrl google)
                google.LoginHint = loginHint;
            return request;
        }
    }
}
