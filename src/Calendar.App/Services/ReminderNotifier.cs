using System.Security.Cryptography;
using System.Text;
using Calendar.App.Helpers;
using Calendar.Core.Reminders;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Calendar.App.Services;

/// <summary>What the user did with a reminder toast.</summary>
internal sealed record ReminderAction(string Action, string? Key, string? Url, TimeSpan? SnoozeFor);

/// <summary>
/// Reminder toasts (task 8.2, ui.md): title, time and how soon, calendar/account; buttons
/// Join (when there is a meeting link), Snooze with 5/10/15 minutes, Dismiss. Uses the Reminder
/// scenario, so the toast stays on screen until handled.
/// </summary>
internal static class ReminderNotifier
{
    private const string Group = "reminders";

    public static void Register() => AppNotificationManager.Default.Register();

    public static void Unregister() => AppNotificationManager.Default.Unregister();

    public static void Show(ReminderOccurrence reminder, string? calendarDisplay)
    {
        var ev = reminder.Event;
        var builder = new AppNotificationBuilder()
            .SetScenario(AppNotificationScenario.Reminder)
            .AddArgument("action", "open")
            .AddArgument("key", reminder.Key)
            .AddText(ev.Title)
            .AddText($"{EventText.When(ev)} · {HowSoon(reminder)}");
        if (calendarDisplay is not null)
            builder.SetAttributionText(calendarDisplay);

        if (Uri.TryCreate(ev.MeetingUrl, UriKind.Absolute, out var meeting))
            builder.AddButton(new AppNotificationButton("Tham gia")
                .AddArgument("action", "join")
                .AddArgument("url", meeting.ToString()));

        builder
            .AddComboBox(new AppNotificationComboBox("snooze")
                .AddItem("5", "5 phút")
                .AddItem("10", "10 phút")
                .AddItem("15", "15 phút")
                .SetSelectedItem("5"))
            .AddButton(new AppNotificationButton("Báo lại")
                .AddArgument("action", "snooze")
                .AddArgument("key", reminder.Key))
            .AddButton(new AppNotificationButton("Bỏ qua")
                .AddArgument("action", "dismiss"));

        var notification = builder.BuildNotification();
        notification.Tag = TagFor(reminder.Key);
        notification.Group = Group;
        AppNotificationManager.Default.Show(notification);
    }

    public static ReminderAction Parse(AppNotificationActivatedEventArgs args)
    {
        var a = args.Arguments;
        TimeSpan? snooze = args.UserInput.TryGetValue("snooze", out var minutes) && int.TryParse(minutes, out var m)
            ? TimeSpan.FromMinutes(m)
            : null;
        return new ReminderAction(
            a.TryGetValue("action", out var action) ? action : "open",
            a.TryGetValue("key", out var key) ? key : null,
            a.TryGetValue("url", out var url) ? url : null,
            snooze);
    }

    private static string HowSoon(ReminderOccurrence r)
    {
        var lead = r.Event.Start - DateTimeOffset.Now;
        if (r.Event.IsAllDay)
            return "Cả ngày";
        if (lead <= TimeSpan.FromMinutes(0.5))
            return lead > -(r.Event.End - r.Event.Start) ? "đang diễn ra" : "đã kết thúc";
        return lead < TimeSpan.FromHours(1)
            ? $"sau {Math.Ceiling(lead.TotalMinutes)} phút"
            : $"sau {Math.Round(lead.TotalHours, 1)} giờ";
    }

    /// <summary>Toast tags are limited to 64 characters: hash the reminder key.</summary>
    private static string TagFor(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32];
}
