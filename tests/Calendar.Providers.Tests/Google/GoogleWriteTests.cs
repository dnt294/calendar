using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using Calendar.Core.Models;
using Calendar.Providers.Google;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Http;
using Google.Apis.Util.Store;

namespace Calendar.Providers.Tests.Google;

public class GoogleDraftMappingTests
{
    private static readonly DateTimeOffset Nine = new(2026, 11, 27, 9, 0, 0, TimeSpan.FromHours(7));

    private static EventDraft Draft() => new() { Title = "Review", Start = Nine, End = Nine.AddHours(1) };

    [Fact]
    public void Timed_draft()
    {
        var e = GoogleMapper.ToGoogleEvent(Draft() with { Location = "Room 1" });
        Assert.Equal("Review", e.Summary);
        Assert.Equal("Room 1", e.Location);
        Assert.Equal("", e.Description); // empty clears on patch
        Assert.Equal(Nine, e.Start.DateTimeDateTimeOffset);
        Assert.Equal(Nine.AddHours(1), e.End.DateTimeDateTimeOffset);
        Assert.Null(e.Start.Date);
        Assert.True(e.Reminders.UseDefault);
        Assert.Null(e.Attendees); // plain event: no guest list sent
    }

    [Fact]
    public void All_day_draft_uses_dates_with_exclusive_end()
    {
        var (s, en) = CalendarEvent.AllDaySpan(new DateOnly(2026, 11, 27), new DateOnly(2026, 11, 28));
        var e = GoogleMapper.ToGoogleEvent(Draft() with { Start = s, End = en, IsAllDay = true });
        Assert.Equal("2026-11-27", e.Start.Date);
        Assert.Equal("2026-11-29", e.End.Date);
        Assert.Null(e.Start.DateTimeDateTimeOffset);
    }

    [Fact]
    public void Custom_reminders_become_popup_overrides()
    {
        var e = GoogleMapper.ToGoogleEvent(Draft() with { UseDefaultReminders = false, Reminders = [new Reminder(10), new Reminder(60)] });
        Assert.False(e.Reminders.UseDefault);
        Assert.Equal([("popup", 10), ("popup", 60)], e.Reminders.Overrides.Select(r => (r.Method, r.Minutes!.Value)));

        var none = GoogleMapper.ToGoogleEvent(Draft() with { UseDefaultReminders = false, Reminders = [] });
        Assert.False(none.Reminders.UseDefault);
        Assert.Empty(none.Reminders.Overrides);
    }

    [Fact]
    public void Guests_keep_their_responses_and_the_owner_entry_is_kept()
    {
        var existing = new CalendarEvent
        {
            AccountId = "a", CalendarId = "c", Id = "e", Title = "t", Start = Nine, End = Nine,
            Attendees =
            [
                new Attendee("me@gmail.com", "Me", RsvpStatus.Accepted, IsSelf: true, IsOrganizer: true),
                new Attendee("kept@x.com", null, RsvpStatus.Tentative, IsOptional: true),
                new Attendee("removed@x.com", null, RsvpStatus.Accepted),
            ],
        };
        var e = GoogleMapper.ToGoogleEvent(Draft() with { AttendeeEmails = ["KEPT@x.com", "new@x.com"] }, existing);

        Assert.Equal(
            [("me@gmail.com", "accepted"), ("kept@x.com", "tentative"), ("new@x.com", "needsAction")],
            e.Attendees.Select(a => (a.Email, a.ResponseStatus)));
        Assert.True(e.Attendees[1].Optional);
    }

    [Fact]
    public void Removing_all_guests_sends_an_empty_list()
    {
        var existing = new CalendarEvent
        {
            AccountId = "a", CalendarId = "c", Id = "e", Title = "t", Start = Nine, End = Nine,
            Attendees = [new Attendee("me@gmail.com", null, RsvpStatus.Accepted, IsSelf: true), new Attendee("x@x.com", null, RsvpStatus.Accepted)],
        };
        Assert.Empty(GoogleMapper.ToGoogleEvent(Draft(), existing).Attendees);
    }

    [Fact]
    public void Rooms_and_live_responses_come_from_the_current_guest_list()
    {
        var cached = new CalendarEvent
        {
            AccountId = "a", CalendarId = "c", Id = "e", Title = "t", Start = Nine, End = Nine,
            Attendees = [new Attendee("me@gmail.com", null, RsvpStatus.NeedsAction, IsSelf: true), new Attendee("x@x.com", null, RsvpStatus.NeedsAction)],
        };
        IList<EventAttendee> live =
        [
            new() { Email = "me@gmail.com", Self = true, ResponseStatus = "accepted" },
            new() { Email = "x@x.com", ResponseStatus = "declined" },
            new() { Email = "room-1@resource.calendar.google.com", Resource = true, ResponseStatus = "accepted" },
        ];

        var e = GoogleMapper.ToGoogleEvent(Draft() with { AttendeeEmails = ["x@x.com", "y@y.com"] }, cached, live);

        Assert.Equal(
            [("me@gmail.com", "accepted"), ("x@x.com", "declined"), ("y@y.com", "needsAction"), ("room-1@resource.calendar.google.com", "accepted")],
            e.Attendees.Select(a => (a.Email, a.ResponseStatus)));
        Assert.True(e.Attendees[^1].Resource);

        // Removing every guest keeps the room booked.
        var alone = GoogleMapper.ToGoogleEvent(Draft(), cached, live);
        Assert.Equal(["room-1@resource.calendar.google.com"], alone.Attendees.Select(a => a.Email));
    }

    [Fact]
    public void WithSelfResponse_changes_only_the_owner()
    {
        IList<EventAttendee> live =
        [
            new() { Email = "boss@x.com", Organizer = true, ResponseStatus = "accepted" },
            new() { Email = "me@gmail.com", Self = true, ResponseStatus = "needsAction", Comment = "maybe" },
            new() { Email = "room@resource.calendar.google.com", Resource = true, ResponseStatus = "accepted" },
        ];
        var result = GoogleMapper.WithSelfResponse(live, RsvpStatus.Declined)!;
        Assert.Equal(["accepted", "declined", "accepted"], result.Select(a => a.ResponseStatus));
        Assert.Equal("maybe", result[1].Comment);
        Assert.Same(live[0], result[0]);
        Assert.Null(GoogleMapper.WithSelfResponse([new EventAttendee { Email = "x@x.com" }], RsvpStatus.Accepted));
        Assert.Null(GoogleMapper.WithSelfResponse(null, RsvpStatus.Accepted));
    }

    [Fact]
    public void Send_updates_only_when_there_are_guests()
    {
        var withGuest = new CalendarEvent
        {
            AccountId = "a", CalendarId = "c", Id = "e", Title = "t", Start = Nine, End = Nine,
            Attendees = [new Attendee("x@x.com", null, RsvpStatus.Accepted)],
        };
        Assert.Equal("none", GoogleMapper.SendUpdatesFor(Draft(), null));
        Assert.Equal("all", GoogleMapper.SendUpdatesFor(Draft() with { AttendeeEmails = ["x@x.com"] }, null));
        Assert.Equal("all", GoogleMapper.SendUpdatesFor(Draft(), withGuest)); // guest removed → tell them
        Assert.Equal("all", GoogleMapper.SendUpdatesFor(null, withGuest));    // delete → cancellation email
    }

    [Fact]
    public void Mapper_reads_default_reminder_flag()
    {
        var e = new Event { Id = "x", Summary = "x", Start = new EventDateTime { DateTimeDateTimeOffset = Nine }, End = new EventDateTime { DateTimeDateTimeOffset = Nine } };
        Assert.True(GoogleMapper.ToEvent(e, "a", "c", null)!.UsesDefaultReminders);
        e.Reminders = new Event.RemindersData { UseDefault = false };
        Assert.False(GoogleMapper.ToEvent(e, "a", "c", null)!.UsesDefaultReminders);
    }
}

/// <summary>Create / update / delete against a fake Google server.</summary>
public sealed class GoogleWriteTests
{
    private static readonly Account Me = new("google:me@gmail.com", "google", "me@gmail.com", "me@gmail.com", "Me");
    private static readonly CalendarInfo Primary = new(Me.Id, "me@gmail.com", "Me", "#039BE5", true, true);
    private static readonly DateTimeOffset Nine = new(2026, 11, 27, 9, 0, 0, TimeSpan.FromHours(7));

    private readonly FakeServer _server = new();
    private readonly GoogleCalendarProvider _provider;

    public GoogleWriteTests()
    {
        var tokens = new MemoryStore();
        tokens.StoreAsync(Me.ProviderAccountId, new TokenResponse
        {
            AccessToken = "access", RefreshToken = "refresh", ExpiresInSeconds = 3600, IssuedUtc = DateTime.UtcNow,
        }).Wait();
        _provider = new GoogleCalendarProvider(new GoogleOAuthOptions("id", "secret"), tokens, null, _server);
    }

    private const string EventJson = """{ "id": "new-1", "status": "confirmed", "summary": "Review", "start": { "dateTime": "2026-11-27T09:00:00+07:00" }, "end": { "dateTime": "2026-11-27T10:00:00+07:00" }, "reminders": { "useDefault": true } }""";
    private const string CalendarJson = """{ "id": "me@gmail.com", "defaultReminders": [ { "method": "popup", "minutes": 30 } ] }""";

    private static EventDraft Draft() => new() { Title = "Review", Start = Nine, End = Nine.AddHours(1) };

    private static CalendarEvent Existing(string id = "e1", string? seriesId = null) => new()
    {
        AccountId = Me.Id, CalendarId = Primary.Id, Id = id, Title = "Old", Start = Nine, End = Nine.AddHours(1), SeriesId = seriesId,
    };

    [Fact]
    public async Task Create_posts_to_the_calendar_and_resolves_default_reminders()
    {
        _server.Respond = r => r.Method == "GET" ? CalendarJson : EventJson;

        var created = await _provider.CreateEventAsync(Me, Primary, Draft(), CancellationToken.None);

        var post = _server.Requests.First(r => r.Method == "POST");
        Assert.EndsWith("/calendars/me%40gmail.com/events", post.Path);
        Assert.Equal("none", post.Query["sendUpdates"]);
        Assert.Equal("Review", post.Body!.RootElement.GetProperty("summary").GetString());
        Assert.Equal(("new-1", Me.Id, Primary.Id), (created.Id, created.AccountId, created.CalendarId));
        Assert.True(created.UsesDefaultReminders);
        Assert.Equal([new Reminder(30)], created.Reminders);
    }

    [Fact]
    public async Task Create_with_guests_emails_them()
    {
        _server.Respond = r => r.Method == "GET" ? CalendarJson : EventJson;
        await _provider.CreateEventAsync(Me, Primary, Draft() with { AttendeeEmails = ["guest@x.com"] }, CancellationToken.None);
        Assert.Equal("all", _server.Requests.First(r => r.Method == "POST").Query["sendUpdates"]);
    }

    [Fact]
    public async Task Update_patches_only_the_given_event_id()
    {
        _server.Respond = r => r.Method == "GET" ? CalendarJson : EventJson.Replace("new-1", "series_20261127T020000Z");

        var occurrence = Existing("series_20261127T020000Z", seriesId: "series");
        var updated = await _provider.UpdateEventAsync(Me, occurrence, Draft(), CancellationToken.None);

        var patch = _server.Requests.Single(r => r.Method == "PATCH");
        Assert.EndsWith("/calendars/me%40gmail.com/events/series_20261127T020000Z", patch.Path);
        Assert.False(patch.Body!.RootElement.TryGetProperty("recurrence", out _));
        Assert.Equal("series_20261127T020000Z", updated.Id);
    }

    [Fact]
    public async Task Custom_reminders_need_no_calendar_lookup()
    {
        _server.Respond = _ => EventJson.Replace("\"useDefault\": true", "\"useDefault\": false, \"overrides\": [ { \"method\": \"popup\", \"minutes\": 5 } ]");
        var updated = await _provider.UpdateEventAsync(Me, Existing(), Draft() with { UseDefaultReminders = false, Reminders = [new Reminder(5)] }, CancellationToken.None);
        Assert.DoesNotContain(_server.Requests, r => r.Method == "GET" && r.Path.Contains("calendarList", StringComparison.Ordinal));
        Assert.Equal([new Reminder(5)], updated.Reminders);
    }

    [Fact]
    public async Task Delete_sends_delete_for_that_id()
    {
        _server.Respond = _ => "";
        await _provider.DeleteEventAsync(Me, Existing("e9"), CancellationToken.None);
        var del = _server.Requests.Single();
        Assert.Equal("DELETE", del.Method);
        Assert.EndsWith("/events/e9", del.Path);
        Assert.Equal("none", del.Query["sendUpdates"]);
    }

    [Fact]
    public async Task Deleting_an_already_deleted_event_succeeds()
    {
        _server.Status = HttpStatusCode.Gone;
        _server.Respond = _ => """{ "error": { "code": 410, "message": "Resource has been deleted" } }""";
        await _provider.DeleteEventAsync(Me, Existing(), CancellationToken.None);
    }

    private const string InviteJson = """{ "id": "inv", "status": "confirmed", "summary": "Planning", "start": { "dateTime": "2026-11-27T09:00:00+07:00" }, "end": { "dateTime": "2026-11-27T10:00:00+07:00" }, "reminders": { "useDefault": false }, "organizer": { "email": "boss@x.com" }, "attendees": [ { "email": "boss@x.com", "organizer": true, "responseStatus": "accepted" }, { "email": "me@gmail.com", "self": true, "responseStatus": "needsAction" }, { "email": "room@resource.calendar.google.com", "resource": true, "responseStatus": "accepted" } ] }""";

    [Fact]
    public async Task Respond_patches_the_live_guest_list_with_the_new_response()
    {
        _server.Respond = r => r.Method == "PATCH" ? InviteJson.Replace("\"self\": true, \"responseStatus\": \"needsAction\"", "\"self\": true, \"responseStatus\": \"tentative\"") : InviteJson;
        var invite = Existing("inv") with { SelfRsvp = RsvpStatus.NeedsAction };

        var updated = await _provider.RespondAsync(Me, invite, RsvpStatus.Tentative, CancellationToken.None);

        var patch = _server.Requests.Single(r => r.Method == "PATCH");
        Assert.EndsWith("/events/inv", patch.Path);
        Assert.Equal("none", patch.Query["sendUpdates"]);
        var sent = patch.Body!.RootElement.GetProperty("attendees").EnumerateArray()
            .Select(a => (a.GetProperty("email").GetString(), a.GetProperty("responseStatus").GetString())).ToList();
        Assert.Equal([("boss@x.com", "accepted"), ("me@gmail.com", "tentative"), ("room@resource.calendar.google.com", "accepted")], sent);
        Assert.False(patch.Body.RootElement.TryGetProperty("summary", out _)); // only attendees are patched
        Assert.Equal(RsvpStatus.Tentative, updated.SelfRsvp);
    }

    [Fact]
    public async Task Respond_without_own_guest_entry_fails_without_patching()
    {
        _server.Respond = _ => EventJson;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _provider.RespondAsync(Me, Existing("new-1") with { SelfRsvp = RsvpStatus.NeedsAction }, RsvpStatus.Accepted, CancellationToken.None));
        Assert.DoesNotContain(_server.Requests, r => r.Method == "PATCH");
    }

    [Fact]
    public async Task Update_reads_the_current_event_first_to_keep_rooms()
    {
        _server.Respond = _ => InviteJson;
        var cached = Existing("inv") with
        {
            Attendees = [new Attendee("me@gmail.com", null, RsvpStatus.NeedsAction, IsSelf: true), new Attendee("boss@x.com", null, RsvpStatus.Accepted, IsOrganizer: true)],
        };

        await _provider.UpdateEventAsync(Me, cached, Draft() with { AttendeeEmails = ["boss@x.com"] }, CancellationToken.None);

        Assert.Equal("GET", _server.Requests[0].Method);
        var patch = _server.Requests.Single(r => r.Method == "PATCH");
        Assert.Contains(patch.Body!.RootElement.GetProperty("attendees").EnumerateArray(),
            a => a.GetProperty("email").GetString() == "room@resource.calendar.google.com");
    }

    // ---- Fakes ------------------------------------------------------------------------------

    private sealed record Recorded(string Method, string Path, System.Collections.Specialized.NameValueCollection Query, JsonDocument? Body);

    private sealed class FakeServer : HttpClientFactory
    {
        public Func<Recorded, string> Respond { get; set; } = _ => "{}";
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public List<Recorded> Requests { get; } = [];

        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => new Handler(this);

        private sealed class Handler(FakeServer owner) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                string? body = null;
                if (request.Content is not null)
                {
                    // The Google client gzips request bodies.
                    var bytes = await request.Content.ReadAsByteArrayAsync(ct);
                    if (bytes is [0x1F, 0x8B, ..])
                    {
                        using var gzip = new System.IO.Compression.GZipStream(new MemoryStream(bytes), System.IO.Compression.CompressionMode.Decompress);
                        using var reader = new StreamReader(gzip, Encoding.UTF8);
                        body = await reader.ReadToEndAsync(ct);
                    }
                    else
                    {
                        body = Encoding.UTF8.GetString(bytes);
                    }
                }
                var recorded = new Recorded(request.Method.Method, request.RequestUri!.AbsolutePath,
                    HttpUtility.ParseQueryString(request.RequestUri.Query),
                    string.IsNullOrEmpty(body) ? null : JsonDocument.Parse(body));
                owner.Requests.Add(recorded);
                var status = request.Method == HttpMethod.Delete && owner.Status == HttpStatusCode.OK ? HttpStatusCode.NoContent : owner.Status;
                return new HttpResponseMessage(status)
                {
                    Content = new StringContent(owner.Respond(recorded), Encoding.UTF8, "application/json"),
                };
            }
        }
    }

    private sealed class MemoryStore : IDataStore
    {
        private readonly Dictionary<string, object?> _items = new();
        public Task StoreAsync<T>(string key, T value) { _items[typeof(T).Name + key] = value; return Task.CompletedTask; }
        public Task DeleteAsync<T>(string key) { _items.Remove(typeof(T).Name + key); return Task.CompletedTask; }
        public Task<T> GetAsync<T>(string key) => Task.FromResult(_items.TryGetValue(typeof(T).Name + key, out var v) ? (T)v! : default!);
        public Task ClearAsync() { _items.Clear(); return Task.CompletedTask; }
    }
}
