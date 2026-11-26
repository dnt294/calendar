namespace Calendar.App.Helpers;

/// <summary>A reminder choice in a picker: the calendar default, none, or minutes before.</summary>
internal sealed record ReminderOption(string Label, bool UseDefault, int? Minutes)
{
    public override string ToString() => Label;
}

/// <summary>The reminder choices shared by the event editor and the settings page.</summary>
internal static class ReminderPresets
{
    public static ReminderOption CalendarDefault { get; } = new("Mặc định của lịch", true, null);
    public static ReminderOption None { get; } = new("Không nhắc", false, null);

    public static List<ReminderOption> All() =>
    [
        CalendarDefault,
        None,
        new("Lúc bắt đầu", false, 0),
        new("5 phút trước", false, 5),
        new("10 phút trước", false, 10),
        new("15 phút trước", false, 15),
        new("30 phút trước", false, 30),
        new("1 giờ trước", false, 60),
        new("1 ngày trước", false, 1440),
    ];
}
