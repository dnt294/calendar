using Calendar.Core.Models;

namespace Calendar.Core.Tests.Models;

public class EventDraftTests
{
    private static readonly DateTimeOffset Nine = new(2026, 11, 27, 9, 0, 0, TimeSpan.FromHours(7));

    private static EventDraft Draft(DateTimeOffset start, DateTimeOffset end, bool allDay = false, params string[] guests) => new()
    {
        Title = "x", Start = start, End = end, IsAllDay = allDay, AttendeeEmails = guests,
    };

    [Fact]
    public void Valid_timed_draft_has_no_problems()
    {
        Assert.Empty(Draft(Nine, Nine.AddHours(1)).Validate());
        Assert.Empty(Draft(Nine, Nine).Validate()); // zero length is allowed (Google allows it)
    }

    [Fact]
    public void End_before_start_is_reported()
    {
        Assert.Equal([new DraftProblem(DraftProblemKind.EndBeforeStart)], Draft(Nine, Nine.AddMinutes(-1)).Validate());
    }

    [Fact]
    public void All_day_needs_at_least_one_day()
    {
        var (s, e) = CalendarEvent.AllDaySpan(new DateOnly(2026, 11, 27), new DateOnly(2026, 11, 27));
        Assert.Empty(Draft(s, e, allDay: true).Validate());
        Assert.Single(Draft(s, s, allDay: true).Validate());
    }

    [Fact]
    public void Invalid_guest_emails_are_reported_one_by_one()
    {
        var problems = Draft(Nine, Nine.AddHours(1), false, "ok@example.com", "nope", "a@b").Validate();
        Assert.Equal(
        [
            new DraftProblem(DraftProblemKind.InvalidAttendeeEmail, "nope"),
            new DraftProblem(DraftProblemKind.InvalidAttendeeEmail, "a@b"),
        ], problems);
    }

    [Fact]
    public void FromEvent_keeps_default_reminder_choice()
    {
        var ev = new CalendarEvent
        {
            AccountId = "a", CalendarId = "c", Id = "e", Title = "t", Start = Nine, End = Nine.AddHours(1),
            Reminders = [new Reminder(10)], UsesDefaultReminders = true,
        };
        var draft = EventDraft.FromEvent(ev);
        Assert.True(draft.UseDefaultReminders);
        Assert.False(EventDraft.FromEvent(ev with { UsesDefaultReminders = false }).UseDefaultReminders);
    }

    [Theory]
    [InlineData("", new string[0])]
    [InlineData("a@x.com", new[] { "a@x.com" })]
    [InlineData(" a@x.com, b@y.org;c@z.vn\nd@w.io ", new[] { "a@x.com", "b@y.org", "c@z.vn", "d@w.io" })]
    [InlineData("a@x.com A@X.com <b@y.org>", new[] { "a@x.com", "b@y.org" })]
    [InlineData("a@x.com,,; ,", new[] { "a@x.com" })]
    public void EmailList_parse(string text, string[] expected) => Assert.Equal(expected, EmailList.Parse(text));

    [Theory]
    [InlineData("dat@gmail.com", true)]
    [InlineData("first.last+tag@sub.example.co.uk", true)]
    [InlineData("nope", false)]
    [InlineData("a@b", false)]
    [InlineData("a@b.", false)]
    [InlineData("Name <a@b.com>", false)]
    [InlineData("@example.com", false)]
    public void EmailList_validity(string email, bool expected) => Assert.Equal(expected, EmailList.IsValid(email));
}

public class HtmlTextTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    [InlineData("Plain text\nsecond line", "Plain text\nsecond line")]
    [InlineData("Tom &amp; Jerry", "Tom & Jerry")]
    [InlineData("Line 1<br>Line 2<br/>Line 3", "Line 1\nLine 2\nLine 3")]
    [InlineData("<p>One</p><p>Two</p>", "One\nTwo")]
    [InlineData("<b>Bold</b> and <i>italic</i>", "Bold and italic")]
    [InlineData("<ul><li>a</li><li>b</li></ul>", "• a\n• b")]
    [InlineData("Join: <a href=\"https://meet.google.com/abc\">https://meet.google.com/abc</a>", "Join: https://meet.google.com/abc")]
    [InlineData("<a href=\"https://x.com/doc\">Agenda</a>", "Agenda (https://x.com/doc)")]
    [InlineData("a<br><br><br><br>b", "a\n\nb")]
    [InlineData("x&nbsp;y", "x y")]
    public void ToPlainText(string? html, string expected) => Assert.Equal(expected, HtmlText.ToPlainText(html));
}
