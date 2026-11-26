using Calendar.App.Controls;
using Calendar.App.Helpers;
using Calendar.App.Services;
using Calendar.App.ViewModels;
using Calendar.App.Views;
using Calendar.Core.Layout;
using Calendar.Core.Models;
using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Calendar.App;

public sealed partial class MainWindow : Window
{
    public ShellViewModel ViewModel { get; }

    public MainWindow(ShellViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // Sync when the user comes back to the window (if the last sync is not fresh).
        Activated += (_, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated)
                ViewModel.OnWindowActivated();
        };

        // Keep the view selector and the mini month in step with the view model.
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.ViewMode))
                ShowViewMode(ViewModel.ViewMode);
            else if (e.PropertyName == nameof(ShellViewModel.SelectedDate))
                ShowSelectedDate(ViewModel.SelectedDate);
            else if (e.PropertyName == nameof(ShellViewModel.FirstDayOfWeek))
                MiniCalendar.FirstDayOfWeek = (Windows.Globalization.DayOfWeek)(int)ViewModel.FirstDayOfWeek;
        };
        MiniCalendar.FirstDayOfWeek = (Windows.Globalization.DayOfWeek)(int)ViewModel.FirstDayOfWeek;
        ShowSelectedDate(ViewModel.SelectedDate);

        AddShortcuts();
        CreateTray();
    }

    private void OnPaneToggleRequested(TitleBar sender, object args) => Shell.IsPaneOpen = !Shell.IsPaneOpen;

    // ---- Tray (task 8.1): closing the window hides it; "Thoát" in the tray menu exits ---------

    private TaskbarIcon? _tray;
    private MenuFlyoutItem? _nextEventItem;
    private ToggleMenuFlyoutItem? _startupItem;
    private bool _exiting;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _minuteTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();

    /// <summary>Shows the window again (tray click, toast, second launch) and brings it to the front.</summary>
    public void ShowFromTray()
    {
        AppWindow.Show();
        Activate();
    }

    private void CreateTray()
    {
        _nextEventItem = new MenuFlyoutItem { Text = ViewModel.NextEventText, IsEnabled = false };
        _startupItem = new ToggleMenuFlyoutItem
        {
            Text = "Khởi động cùng Windows",
            Command = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(ToggleStartupAsync),
        };
        var menu = new MenuFlyout();
        menu.Items.Add(_nextEventItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem { Text = "Mở Calendar", Command = new CommunityToolkit.Mvvm.Input.RelayCommand(ShowFromTray) });
        menu.Items.Add(new MenuFlyoutItem { Text = "Đồng bộ ngay", Command = ViewModel.SyncNowCommand });
        menu.Items.Add(_startupItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem { Text = "Thoát", Command = new CommunityToolkit.Mvvm.Input.RelayCommand(Exit) });

        _tray = new TaskbarIcon
        {
            ToolTipText = "Calendar",
            IconSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/TrayIcon.ico")),
            ContextMenuMode = ContextMenuMode.SecondWindow,
            ContextFlyout = menu,
            NoLeftClickDelay = true,
            LeftClickCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(ShowFromTray),
        };
        _tray.ForceCreate();

        // Closing the window keeps the app (and its reminders) running in the tray.
        AppWindow.Closing += (_, e) =>
        {
            if (_exiting)
                return;
            e.Cancel = true;
            AppWindow.Hide();
        };

        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.NextEventText))
                UpdateNextEvent();
        };
        // The "next event" changes as time passes even without a sync.
        _minuteTimer.Interval = TimeSpan.FromMinutes(1);
        _minuteTimer.Tick += (_, _) => _ = ViewModel.RefreshUpcomingAsync();
        _minuteTimer.Start();

        _ = LoadStartupStateAsync();
    }

    private void UpdateNextEvent()
    {
        if (_nextEventItem is not null)
            _nextEventItem.Text = ViewModel.NextEventText;
        if (_tray is not null)
            _tray.ToolTipText = $"Calendar — {ViewModel.NextEventText}";
    }

    private async Task LoadStartupStateAsync()
    {
        try
        {
            _startupItem!.IsChecked = await StartupService.IsEnabledAsync();
        }
        catch (Exception)
        {
            _startupItem!.IsEnabled = false; // not packaged / not available
        }
    }

    private async Task ToggleStartupAsync()
    {
        // The toggle already flipped IsChecked; apply it and show what Windows actually allowed.
        var (enabled, problem) = await StartupService.SetEnabledAsync(_startupItem!.IsChecked);
        _startupItem.IsChecked = enabled;
        if (problem is not null)
            ViewModel.ShowInfo(problem);
    }

    private void Exit()
    {
        _exiting = true;
        _minuteTimer.Stop();
        _tray?.Dispose();
        ((App)Application.Current).ExitApp();
    }

    // ---- View mode & date -------------------------------------------------------------------

    private bool _updatingControls;

    private void OnViewSelectorChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_updatingControls || sender.SelectedItem?.Tag is not string tag)
            return;
        ViewModel.ViewMode = Enum.Parse<CalendarViewMode>(tag);
    }

    private void ShowViewMode(CalendarViewMode mode)
    {
        _updatingControls = true;
        ViewSelector.SelectedItem = mode switch
        {
            CalendarViewMode.Day => DayItem,
            CalendarViewMode.Month => MonthItem,
            _ => WeekItem,
        };
        _updatingControls = false;
    }

    private void OnMiniCalendarSelectedDatesChanged(CalendarView sender, CalendarViewSelectedDatesChangedEventArgs args)
    {
        if (_updatingControls || args.AddedDates.Count == 0)
            return;
        ViewModel.SelectedDate = DateOnly.FromDateTime(args.AddedDates[0].DateTime);
    }

    private void ShowSelectedDate(DateOnly date)
    {
        _updatingControls = true;
        var value = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue));
        MiniCalendar.SelectedDates.Clear();
        MiniCalendar.SelectedDates.Add(value);
        MiniCalendar.SetDisplayDate(value);
        _updatingControls = false;
    }

    private void OnMonthDayRequested(object? sender, DateOnly day) => ViewModel.OpenDay(day);

    private void OnMonthScrolled(object? sender, DateOnly month) => ViewModel.OnMonthScrolled(month);

    // ---- Events: details, quick create, editor, delete (Phase 6) ----------------------------

    private bool _dialogOpen;

    private void OnEventClicked(object? sender, EventClickedEventArgs e)
    {
        var ev = e.Event;
        var flyout = new Flyout();
        var panel = new EventDetailsPanel(ev, ViewModel.CalendarOf(ev), ViewModel.CanWrite, ViewModel.WriteDisabledReason,
            onEdit: () => { flyout.Hide(); _ = EditAsync(ev); },
            onDelete: () => { flyout.Hide(); _ = ConfirmDeleteAsync(ev); },
            onRespond: status => { flyout.Hide(); _ = ViewModel.RespondAsync(ev, status); });
        // Delete key deletes the event whose details are open (ui.md shortcuts).
        panel.KeyDown += (_, k) =>
        {
            if (k.Key == VirtualKey.Delete && ViewModel.CanWrite && ViewModel.CalendarOf(ev)?.Calendar.CanEdit == true)
            {
                k.Handled = true;
                flyout.Hide();
                _ = ConfirmDeleteAsync(ev);
            }
        };
        flyout.Content = panel;
        flyout.ShowAt(e.Anchor, new FlyoutShowOptions { Placement = FlyoutPlacementMode.Auto });
    }

    private void OnSlotSelected(object? sender, SlotSelectedEventArgs e)
    {
        if (!ViewModel.CanWrite)
        {
            TimeGrid.ClearSelection();
            ViewModel.ExplainWriteDisabled();
            return;
        }

        var flyout = new Flyout();
        EventDraft Draft(string title) => ViewModel.NewDraft(title, e.Start, e.End, e.IsAllDay);
        flyout.Content = new QuickCreatePanel(e.Start, e.End, e.IsAllDay, ViewModel.WritableCalendars, ViewModel.DefaultCalendar,
            onSave: (title, calendar) =>
            {
                flyout.Hide();
                _ = ViewModel.CreateEventAsync(calendar, Draft(title));
            },
            onMoreDetails: (title, calendar) =>
            {
                flyout.Hide();
                _ = CreateWithEditorAsync(Draft(title), calendar);
            });
        flyout.Closed += (_, _) => TimeGrid.ClearSelection();
        flyout.ShowAt(e.Anchor, new FlyoutShowOptions { Placement = FlyoutPlacementMode.Auto });
    }

    /// <summary>Drag &amp; drop (Phase 9): same duration at the new time; only this occurrence for a recurring event.</summary>
    private async void OnEventMoved(object? sender, EventMovedEventArgs e)
    {
        var ev = e.Event;
        if (!ViewModel.CanWrite)
        {
            ViewModel.ExplainWriteDisabled();
            return;
        }
        if (ViewModel.CalendarOf(ev)?.Calendar.CanEdit != true)
        {
            ViewModel.ShowInfo("Lịch này chỉ đọc, không dời được sự kiện.");
            return;
        }
        if (ev.Attendees.Any(a => !a.IsSelf))
        {
            var confirm = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "Dời sự kiện có khách mời?",
                Content = $"Dời \"{ev.Title}\" sang {EventText.When(e.NewStart, e.NewStart + (ev.End - ev.Start), false)}.\nKhách mời sẽ nhận email cập nhật.",
                PrimaryButtonText = "Dời",
                CloseButtonText = "Huỷ",
                DefaultButton = ContentDialogButton.Primary,
            };
            if (!await ShowDialogAsync(confirm))
                return;
        }
        await ViewModel.UpdateEventAsync(ev, EventDraft.MovedTo(ev, e.NewStart));
    }

    private void OnNewEventClick(object sender, RoutedEventArgs e) => _ = NewEventAsync();

    /// <summary>"+ Mới" / N: on the selected day, at the next half hour if that day is today, else 9:00.</summary>
    private Task NewEventAsync()
    {
        if (!ViewModel.CanWrite)
        {
            ViewModel.ExplainWriteDisabled();
            return Task.CompletedTask;
        }
        var next = LocalTime.NextHalfHour();
        var start = ViewModel.SelectedDate == DateOnly.FromDateTime(next.DateTime)
            ? next
            : LocalTime.At(ViewModel.SelectedDate, TimeSpan.FromHours(9));
        return CreateWithEditorAsync(ViewModel.NewDraft("", start, start.AddHours(1), false), ViewModel.DefaultCalendar);
    }

    private async Task CreateWithEditorAsync(EventDraft draft, CalendarChoice? calendar)
    {
        var dialog = new EventEditorDialog(Content.XamlRoot, "Sự kiện mới", draft, ViewModel.WritableCalendars,
            calendar ?? ViewModel.DefaultCalendar, calendarLocked: false, note: null, ViewModel.FirstDayOfWeek);
        if (await ShowDialogAsync(dialog) && dialog.Result is { } result && dialog.SelectedCalendar is { } chosen)
            await ViewModel.CreateEventAsync(chosen, result);
    }

    private async Task EditAsync(CalendarEvent ev)
    {
        var calendar = ViewModel.CalendarOf(ev);
        var note = ev.IsOccurrence
            ? $"Sự kiện lặp: chỉ sửa lần này ({ev.Start.ToLocalTime().ToString("D", System.Globalization.CultureInfo.CurrentCulture)})."
            : null;
        var dialog = new EventEditorDialog(Content.XamlRoot, "Sửa sự kiện", EventDraft.FromEvent(ev),
            calendar is null ? Array.Empty<CalendarChoice>() : new[] { calendar }, calendar, calendarLocked: true, note,
            ViewModel.FirstDayOfWeek);
        if (await ShowDialogAsync(dialog) && dialog.Result is { } result)
            await ViewModel.UpdateEventAsync(ev, result);
    }

    private async Task ConfirmDeleteAsync(CalendarEvent ev)
    {
        var when = EventText.When(ev);
        var text = ev.IsOccurrence
            ? $"Chỉ xoá lần này ({when}).\nCác lần khác của sự kiện lặp vẫn giữ nguyên."
            : $"Xoá \"{ev.Title}\" ({when})?";
        if (ev.Attendees.Any(a => !a.IsSelf))
            text += "\n\nKhách mời sẽ nhận email thông báo huỷ.";

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = ev.IsOccurrence ? "Xoá lần này?" : "Xoá sự kiện?",
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "Xoá",
            CloseButtonText = "Huỷ",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await ShowDialogAsync(dialog))
            await ViewModel.DeleteEventAsync(ev);
    }

    // ---- Settings (Phase 9) ----------------------------------------------------------------

    private void OnSettingsClick(object sender, RoutedEventArgs e) => _ = OpenSettingsAsync();

    private async Task OpenSettingsAsync()
    {
        bool startupEnabled = false, startupAvailable = true;
        try
        {
            startupEnabled = await StartupService.IsEnabledAsync();
        }
        catch (Exception)
        {
            startupAvailable = false;
        }

        var before = ViewModel.Settings;
        var dialog = new SettingsDialog(Content.XamlRoot, before, ViewModel.WritableCalendars, startupEnabled, startupAvailable);
        if (!await ShowDialogAsync(dialog) || dialog.Result is not { } result)
            return;

        await ViewModel.SaveSettingsAsync(result);

        if (startupAvailable && dialog.StartupEnabled != startupEnabled)
        {
            var (enabled, problem) = await StartupService.SetEnabledAsync(dialog.StartupEnabled);
            if (_startupItem is not null)
                _startupItem.IsChecked = enabled;
            if (problem is not null)
                ViewModel.ShowInfo(problem);
        }

        if (result.Theme != before.Theme)
        {
            var restart = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "Đổi giao diện",
                Content = "Giao diện mới sẽ áp dụng khi mở lại app. Mở lại ngay?",
                PrimaryButtonText = "Mở lại ngay",
                CloseButtonText = "Để sau",
                DefaultButton = ContentDialogButton.Primary,
            };
            if (await ShowDialogAsync(restart))
            {
                _exiting = true;
                _minuteTimer.Stop();
                _tray?.Dispose();
                ((App)Application.Current).Restart();
            }
        }
    }

    /// <summary>Only one ContentDialog may be open at a time; a second request is ignored.</summary>
    private async Task<bool> ShowDialogAsync(ContentDialog dialog)
    {
        if (_dialogOpen)
            return false;
        _dialogOpen = true;
        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    // ---- Keyboard shortcuts (ui.md): T today, D/W/M view, ←/→ previous/next ----------------

    private void AddShortcuts()
    {
        Add(VirtualKey.T, arrowKey: false, () => ViewModel.GoToTodayCommand.Execute(null));
        Add(VirtualKey.D, arrowKey: false, () => ViewModel.ViewMode = CalendarViewMode.Day);
        Add(VirtualKey.W, arrowKey: false, () => ViewModel.ViewMode = CalendarViewMode.Week);
        Add(VirtualKey.M, arrowKey: false, () => ViewModel.ViewMode = CalendarViewMode.Month);
        Add(VirtualKey.Left, arrowKey: true, () => ViewModel.PreviousCommand.Execute(null));
        Add(VirtualKey.Right, arrowKey: true, () => ViewModel.NextCommand.Execute(null));
        Add(VirtualKey.N, arrowKey: false, () => _ = NewEventAsync());

        // Ctrl+, opens the settings (VirtualKey has no name for the comma key: 188 = VK_OEM_COMMA).
        var settings = new KeyboardAccelerator { Key = (VirtualKey)188, Modifiers = VirtualKeyModifiers.Control };
        settings.Invoked += (sender, args) =>
        {
            args.Handled = true;
            _ = OpenSettingsAsync();
        };
        ((UIElement)Content).KeyboardAccelerators.Add(settings);

        void Add(VirtualKey key, bool arrowKey, Action action)
        {
            var accelerator = new KeyboardAccelerator { Key = key };
            accelerator.Invoked += (_, args) =>
            {
                if (ShouldLeaveKeyToFocusedControl(arrowKey))
                    return;
                action();
                args.Handled = true;
            };
            ((UIElement)Content).KeyboardAccelerators.Add(accelerator);
        }
    }

    /// <summary>
    /// Letters must still reach text boxes (event editor, Phase 6); arrows must still move inside
    /// controls that use them (mini month, view selector).
    /// </summary>
    private bool ShouldLeaveKeyToFocusedControl(bool arrowKey)
    {
        if (FocusManager.GetFocusedElement(Content.XamlRoot) is not DependencyObject focused)
            return false;
        if (focused is TextBox or PasswordBox or RichEditBox or AutoSuggestBox)
            return true;
        if (!arrowKey)
            return false;
        for (var e = focused; e is not null; e = VisualTreeHelper.GetParent(e))
        {
            if (ReferenceEquals(e, MiniCalendar) || ReferenceEquals(e, ViewSelector))
                return true;
        }
        return false;
    }

    private async void OnRemoveAccountClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not AccountGroupViewModel group || !ViewModel.IsIdle)
            return;

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Xoá tài khoản?",
            Content = $"Xoá {group.Email} khỏi app: thu hồi quyền truy cập, xoá token và lịch đã lưu trên máy.\n\n" +
                      "Lịch trên Google không bị ảnh hưởng. Bạn có thể thêm lại tài khoản này bất cứ lúc nào.",
            PrimaryButtonText = "Xoá",
            CloseButtonText = "Huỷ",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.RemoveAccountCommand.ExecuteAsync(group);
    }

    /// <summary>Closes the palette flyout after a color is picked (the command does the work).</summary>
    private void OnSwatchClick(object sender, RoutedEventArgs e)
    {
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(((FrameworkElement)sender).XamlRoot))
        {
            if (popup.Child is FlyoutPresenter)
                popup.IsOpen = false;
        }
    }
}
