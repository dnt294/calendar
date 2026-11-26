using Calendar.Core.Models;
using Calendar.Core.Providers;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;

namespace Calendar.Providers.Google;

/// <summary>OAuth "Desktop app" client from Google Cloud Console.</summary>
public sealed record GoogleOAuthOptions(string ClientId, string ClientSecret);

/// <summary>Google Calendar API v3 provider. Tokens live in <see cref="IDataStore"/> keyed by the account email.</summary>
public sealed class GoogleCalendarProvider : ICalendarProvider
{
    public const string Id = "google";
    private const string ApplicationName = "personal-calendar";
    private static readonly string[] Scopes = [CalendarService.Scope.Calendar];

    private readonly GoogleOAuthOptions _options;
    private readonly IDataStore _tokens;

    public GoogleCalendarProvider(GoogleOAuthOptions options, IDataStore tokens)
    {
        _options = options;
        _tokens = tokens;
    }

    public string ProviderId => Id;

    // ---- Auth -------------------------------------------------------------------------------

    /// <summary>Opens the browser (loopback redirect). The email is learned from the primary calendar id.</summary>
    public async Task<Account> SignInAsync(CancellationToken ct)
    {
        var pendingKey = "pending-" + Guid.NewGuid().ToString("N");
        try
        {
            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                Secrets, Scopes, pendingKey, ct, _tokens);

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

    /// <summary>
    /// Full sync of <paramref name="window"/> with recurring events expanded (<c>singleEvents=true</c>).
    /// TODO(4.1): incremental sync with syncToken; <paramref name="syncState"/> is ignored for now.
    /// </summary>
    public Task<SyncResult> SyncEventsAsync(Account account, CalendarInfo calendar, string? syncState,
        DateRange window, CancellationToken ct) =>
        WithService(account, ct, async service =>
        {
            var events = new List<CalendarEvent>();
            string? pageToken = null;
            do
            {
                var request = service.Events.List(calendar.Id);
                request.SingleEvents = true;
                request.ShowDeleted = false;
                request.TimeMinDateTimeOffset = window.Start;
                request.TimeMaxDateTimeOffset = window.End;
                request.MaxResults = 2500;
                request.PageToken = pageToken;
                var page = await request.ExecuteAsync(ct);
                events.AddRange((page.Items ?? [])
                    .Select(e => GoogleMapper.ToEvent(e, account.Id, calendar.Id, page.DefaultReminders))
                    .OfType<CalendarEvent>());
                pageToken = page.NextPageToken;
            } while (pageToken is not null);

            return new SyncResult(events, [], NewSyncState: null, IsFullSync: true);
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

    private GoogleAuthorizationCodeFlow CreateFlow() => new(new GoogleAuthorizationCodeFlow.Initializer
    {
        ClientSecrets = Secrets,
        Scopes = Scopes,
        DataStore = _tokens,
    });

    private static CalendarService CreateService(UserCredential credential) => new(new BaseClientService.Initializer
    {
        HttpClientInitializer = credential,
        ApplicationName = ApplicationName,
    });

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
        catch (GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            throw new ReauthenticationRequiredException(account.Id, inner: ex);
        }
    }
}
