using Calendar.App.Helpers;
using Calendar.App.ViewModels;
using Calendar.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Calendar.App.Views;

/// <summary>
/// Settings page (Phase 9, ui.md "Cài đặt"): first day of week, working hours, default reminder and
/// calendar for new events, sync interval, start with Windows, theme. Accounts stay in the sidebar.
/// </summary>
internal sealed class SettingsDialog : ContentDialog
{
    private sealed record Choice<T>(string Label, T Value)
    {
        public override string ToString() => Label;
    }

    private static readonly CalendarChoice LastUsed = new(new Core.Models.CalendarInfo("", "", "Lịch dùng gần nhất", "#000000", false, true), "");

    private readonly ComboBox _firstDay = new() { Header = "Ngày đầu tuần", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TimePicker _workStart = new() { Header = "Giờ làm việc từ", MinuteIncrement = 30 };
    private readonly TimePicker _workEnd = new() { Header = "đến", MinuteIncrement = 30 };
    private readonly ComboBox _reminder = new() { Header = "Nhắc mặc định cho sự kiện mới", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _calendar = new() { Header = "Lịch mặc định khi tạo mới", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _sync = new() { Header = "Tự đồng bộ mỗi", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ToggleSwitch _startup = new() { Header = "Khởi động cùng Windows (chạy ẩn dưới khay hệ thống)" };
    private readonly ComboBox _theme = new() { Header = "Giao diện", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly AppSettings _initial;

    public SettingsDialog(XamlRoot root, AppSettings settings, IReadOnlyList<CalendarChoice> calendars, bool startupEnabled,
        bool startupAvailable)
    {
        XamlRoot = root;
        Title = "Cài đặt";
        PrimaryButtonText = "Lưu";
        CloseButtonText = "Huỷ";
        DefaultButton = ContentDialogButton.Primary;
        _initial = settings;

        var days = new List<Choice<DayOfWeek>> { new("Thứ Hai", DayOfWeek.Monday), new("Chủ Nhật", DayOfWeek.Sunday), new("Thứ Bảy", DayOfWeek.Saturday) };
        _firstDay.ItemsSource = days;
        _firstDay.SelectedItem = days.FirstOrDefault(d => d.Value == settings.FirstDayOfWeek) ?? days[0];

        _workStart.SelectedTime = settings.WorkDayStart.ToTimeSpan();
        _workEnd.SelectedTime = settings.WorkDayEnd.ToTimeSpan();

        var reminders = ReminderPresets.All();
        _reminder.ItemsSource = reminders;
        _reminder.SelectedItem = settings.UseCalendarDefaultReminders ? reminders[0]
            : reminders.FirstOrDefault(r => !r.UseDefault && r.Minutes == settings.DefaultReminderMinutes) ?? reminders[1];

        var calendarChoices = new List<CalendarChoice> { LastUsed };
        calendarChoices.AddRange(calendars);
        _calendar.ItemsSource = calendarChoices;
        _calendar.SelectedItem = settings.DefaultCalendar is { } d
            ? calendars.FirstOrDefault(c => c.Calendar.AccountId == d.AccountId && c.Calendar.Id == d.CalendarId) ?? LastUsed
            : LastUsed;
        _calendar.DisplayMemberPath = null;

        var intervals = new List<Choice<int>> { new("5 phút", 5), new("10 phút", 10), new("15 phút", 15), new("30 phút", 30), new("1 giờ", 60) };
        if (intervals.All(i => i.Value != settings.SyncIntervalMinutes))
            intervals.Add(new($"{settings.SyncIntervalMinutes} phút", settings.SyncIntervalMinutes));
        _sync.ItemsSource = intervals;
        _sync.SelectedItem = intervals.First(i => i.Value == settings.SyncIntervalMinutes);

        _startup.IsOn = startupEnabled;
        _startup.IsEnabled = startupAvailable;

        var themes = new List<Choice<AppTheme>> { new("Theo hệ thống", AppTheme.System), new("Sáng", AppTheme.Light), new("Tối", AppTheme.Dark) };
        _theme.ItemsSource = themes;
        _theme.SelectedItem = themes.First(t => t.Value == settings.Theme);

        _error.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];

        var hours = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        hours.Children.Add(_workStart);
        hours.Children.Add(_workEnd);

        var body = new StackPanel { Spacing = 14, MinWidth = 420 };
        body.Children.Add(Section("Lịch"));
        body.Children.Add(_firstDay);
        body.Children.Add(hours);
        body.Children.Add(Section("Sự kiện mới"));
        body.Children.Add(_calendar);
        body.Children.Add(_reminder);
        body.Children.Add(Section("Đồng bộ & khởi động"));
        body.Children.Add(_sync);
        body.Children.Add(_startup);
        body.Children.Add(Section("Giao diện"));
        body.Children.Add(_theme);
        body.Children.Add(new TextBlock
        {
            Text = "Đổi giao diện sẽ áp dụng sau khi mở lại app.",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });
        body.Children.Add(_error);
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 12, 0) };

        PrimaryButtonClick += (_, args) =>
        {
            var start = _workStart.SelectedTime ?? settings.WorkDayStart.ToTimeSpan();
            var end = _workEnd.SelectedTime ?? settings.WorkDayEnd.ToTimeSpan();
            if (end <= start)
            {
                args.Cancel = true;
                _error.Text = "Giờ kết thúc làm việc phải sau giờ bắt đầu.";
                _error.Visibility = Visibility.Visible;
                return;
            }

            var reminder = (ReminderOption)_reminder.SelectedItem;
            var calendar = _calendar.SelectedItem as CalendarChoice;
            Result = _initial with
            {
                FirstDayOfWeek = ((Choice<DayOfWeek>)_firstDay.SelectedItem).Value,
                WorkDayStart = TimeOnly.FromTimeSpan(start),
                WorkDayEnd = TimeOnly.FromTimeSpan(end),
                UseCalendarDefaultReminders = reminder.UseDefault,
                DefaultReminderMinutes = reminder.Minutes,
                DefaultCalendar = calendar is null || ReferenceEquals(calendar, LastUsed)
                    ? null
                    : new CalendarRef(calendar.Calendar.AccountId, calendar.Calendar.Id),
                SyncIntervalMinutes = ((Choice<int>)_sync.SelectedItem).Value,
                Theme = ((Choice<AppTheme>)_theme.SelectedItem).Value,
            };
            StartupEnabled = _startup.IsOn;
        };
    }

    /// <summary>The new settings when the user saved.</summary>
    public AppSettings? Result { get; private set; }

    public bool StartupEnabled { get; private set; }

    private static TextBlock Section(string text) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        Margin = new Thickness(0, 6, 0, -6),
    };
}
