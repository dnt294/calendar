using Calendar.App.Helpers;
using Calendar.App.ViewModels;
using Calendar.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Calendar.App.Views;

/// <summary>
/// Full event editor (task 6.3): title, calendar (with account email), all-day, start/end,
/// location, guests (typed emails), reminder, description. Validates before closing.
/// When editing, the calendar is fixed and fields the user did not touch are sent back unchanged
/// (HTML description, several custom reminders).
/// </summary>
internal sealed class EventEditorDialog : ContentDialog
{
    private readonly EventDraft _initial;
    private readonly string _initialDescriptionText;
    private readonly TextBox _title = new() { Header = "Tiêu đề", PlaceholderText = "Thêm tiêu đề" };
    private readonly ComboBox _calendar = new() { Header = "Lịch", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CheckBox _allDay = new() { Content = "Cả ngày" };
    private readonly CalendarDatePicker _startDate = new() { Header = "Bắt đầu", FirstDayOfWeek = Windows.Globalization.DayOfWeek.Monday };
    private readonly TimePicker _startTime = new() { MinuteIncrement = 5, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly CalendarDatePicker _endDate = new() { Header = "Kết thúc", FirstDayOfWeek = Windows.Globalization.DayOfWeek.Monday };
    private readonly TimePicker _endTime = new() { MinuteIncrement = 5, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly TextBox _location = new() { Header = "Địa điểm" };
    private readonly TextBox _guests = new() { Header = "Khách mời", PlaceholderText = "email, email…", TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _reminder = new() { Header = "Nhắc trước", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _description = new() { Header = "Mô tả", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 110 };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly ReminderOption _initialReminder;
    private DateTimeOffset _lastStart;

    public EventEditorDialog(XamlRoot root, string heading, EventDraft initial, IReadOnlyList<CalendarChoice> calendars,
        CalendarChoice? selected, bool calendarLocked, string? note, DayOfWeek firstDayOfWeek)
    {
        XamlRoot = root;
        _startDate.FirstDayOfWeek = (Windows.Globalization.DayOfWeek)(int)firstDayOfWeek;
        _endDate.FirstDayOfWeek = (Windows.Globalization.DayOfWeek)(int)firstDayOfWeek;
        Title = heading;
        PrimaryButtonText = "Lưu";
        CloseButtonText = "Huỷ";
        DefaultButton = ContentDialogButton.Primary;
        _initial = initial;
        _initialDescriptionText = HtmlText.ToPlainText(initial.Description);

        _title.Text = initial.Title;
        _calendar.ItemsSource = calendars;
        _calendar.SelectedItem = selected;
        _calendar.IsEnabled = !calendarLocked;
        _allDay.IsChecked = initial.IsAllDay;
        _location.Text = initial.Location ?? "";
        _guests.Text = EmailList.Format(initial.AttendeeEmails);
        _description.Text = _initialDescriptionText;
        _error.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];

        // Dates/times: all-day drafts are floating dates with an exclusive end; show the last day.
        if (initial.IsAllDay)
        {
            var first = DateOnly.FromDateTime(initial.Start.DateTime);
            var last = DateOnly.FromDateTime(initial.End.DateTime).AddDays(-1);
            SetDate(_startDate, first);
            SetDate(_endDate, last < first ? first : last);
            _startTime.SelectedTime = new TimeSpan(9, 0, 0);
            _endTime.SelectedTime = new TimeSpan(10, 0, 0);
        }
        else
        {
            var s = initial.Start.ToLocalTime();
            var e = initial.End.ToLocalTime();
            SetDate(_startDate, DateOnly.FromDateTime(s.DateTime));
            SetDate(_endDate, DateOnly.FromDateTime(e.DateTime));
            _startTime.SelectedTime = s.TimeOfDay;
            _endTime.SelectedTime = e.TimeOfDay;
        }
        _lastStart = ReadStart() ?? initial.Start;

        // Reminders: keep the draft's own choice selectable even if it is not a preset.
        var options = ReminderPresets.All();
        _initialReminder = initial.UseDefaultReminders ? options[0]
            : initial.Reminders.Count == 0 ? options[1]
            : options.FirstOrDefault(o => o.Minutes == initial.Reminders[0].MinutesBefore && initial.Reminders.Count == 1)
              ?? new ReminderOption(string.Join(", ", initial.Reminders.Select(r => $"{r.MinutesBefore} phút trước")), false, initial.Reminders[0].MinutesBefore);
        if (!options.Contains(_initialReminder))
            options.Insert(2, _initialReminder);
        _reminder.ItemsSource = options;
        _reminder.SelectedItem = _initialReminder;

        _allDay.Checked += (_, _) => UpdateTimeVisibility();
        _allDay.Unchecked += (_, _) => UpdateTimeVisibility();
        _startDate.DateChanged += (_, _) => KeepDuration();
        _startTime.SelectedTimeChanged += (_, _) => KeepDuration();
        UpdateTimeVisibility();

        var body = new StackPanel { Spacing = 12, MinWidth = 420 };
        if (note is not null)
            body.Children.Add(new InfoBar { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Informational, Message = note });
        body.Children.Add(_title);
        body.Children.Add(_calendar);
        body.Children.Add(_allDay);
        body.Children.Add(DateTimeRow(_startDate, _startTime));
        body.Children.Add(DateTimeRow(_endDate, _endTime));
        body.Children.Add(_location);
        body.Children.Add(_guests);
        body.Children.Add(_reminder);
        body.Children.Add(_description);
        body.Children.Add(_error);
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 12, 0) };

        PrimaryButtonClick += (_, args) =>
        {
            var (draft, problems) = Build();
            if (problems.Count > 0)
            {
                args.Cancel = true;
                _error.Text = string.Join("\n", problems);
                _error.Visibility = Visibility.Visible;
                return;
            }
            Result = draft;
            SelectedCalendar = _calendar.SelectedItem as CalendarChoice;
        };
        Opened += (_, _) => _title.Focus(FocusState.Programmatic);
    }

    /// <summary>The edited draft, set when the user saved.</summary>
    public EventDraft? Result { get; private set; }

    public CalendarChoice? SelectedCalendar { get; private set; }

    private (EventDraft? Draft, IReadOnlyList<string> Problems) Build()
    {
        var problems = new List<string>();
        if (_calendar.SelectedItem is null)
            problems.Add("Chọn một lịch.");
        if (_startDate.Date is null || _endDate.Date is null)
            return (null, problems.Append("Chọn ngày bắt đầu và kết thúc.").ToList());

        var allDay = _allDay.IsChecked == true;
        var startDay = DateOnly.FromDateTime(_startDate.Date.Value.Date);
        var endDay = DateOnly.FromDateTime(_endDate.Date.Value.Date);
        DateTimeOffset start, end;
        if (allDay)
        {
            if (endDay < startDay)
                return (null, problems.Append(EventText.Problem(new DraftProblem(DraftProblemKind.EndBeforeStart))).ToList());
            (start, end) = CalendarEvent.AllDaySpan(startDay, endDay);
        }
        else
        {
            start = LocalTime.At(startDay, _startTime.SelectedTime ?? TimeSpan.Zero);
            end = LocalTime.At(endDay, _endTime.SelectedTime ?? TimeSpan.Zero);
        }

        var reminder = _reminder.SelectedItem as ReminderOption ?? _initialReminder;
        var description = _description.Text == _initialDescriptionText ? _initial.Description : _description.Text;
        var draft = new EventDraft
        {
            Title = string.IsNullOrWhiteSpace(_title.Text) ? "(Không có tiêu đề)" : _title.Text.Trim(),
            Start = start,
            End = end,
            IsAllDay = allDay,
            Location = string.IsNullOrWhiteSpace(_location.Text) ? null : _location.Text.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description,
            AttendeeEmails = EmailList.Parse(_guests.Text),
            // Untouched reminder field: send back exactly what the event had (it may hold several).
            UseDefaultReminders = ReferenceEquals(reminder, _initialReminder) ? _initial.UseDefaultReminders : reminder.UseDefault,
            Reminders = ReferenceEquals(reminder, _initialReminder) ? _initial.Reminders
                : reminder.Minutes is { } m ? new[] { new Reminder(m) } : Array.Empty<Reminder>(),
        };
        problems.AddRange(draft.Validate().Select(EventText.Problem));
        return (draft, problems);
    }

    private DateTimeOffset? ReadStart() =>
        _startDate.Date is { } d ? LocalTime.At(DateOnly.FromDateTime(d.Date), _startTime.SelectedTime ?? TimeSpan.Zero) : null;

    /// <summary>Moving the start moves the end by the same amount, like Google/Outlook.</summary>
    private void KeepDuration()
    {
        if (ReadStart() is not { } start || _endDate.Date is not { } endDate)
            return;
        var end = LocalTime.At(DateOnly.FromDateTime(endDate.Date), _endTime.SelectedTime ?? TimeSpan.Zero);
        var shifted = end + (start - _lastStart);
        _lastStart = start;
        SetDate(_endDate, DateOnly.FromDateTime(shifted.DateTime));
        _endTime.SelectedTime = shifted.TimeOfDay;
    }

    private void UpdateTimeVisibility()
    {
        var visibility = _allDay.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
        _startTime.Visibility = visibility;
        _endTime.Visibility = visibility;
    }

    private static void SetDate(CalendarDatePicker picker, DateOnly day) =>
        picker.Date = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue));

    private static Grid DateTimeRow(CalendarDatePicker date, TimePicker time)
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        date.HorizontalAlignment = HorizontalAlignment.Stretch;
        row.Children.Add(date);
        Grid.SetColumn(time, 1);
        row.Children.Add(time);
        return row;
    }
}
