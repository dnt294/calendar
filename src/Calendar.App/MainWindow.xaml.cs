using Calendar.App.Controls;
using Calendar.App.Helpers;
using Calendar.App.ViewModels;
using Calendar.App.Views;
using Calendar.Core.Layout;
using Calendar.Core.Models;
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
        };
        ShowSelectedDate(ViewModel.SelectedDate);

        AddShortcuts();
    }

    private void OnPaneToggleRequested(TitleBar sender, object args) => Shell.IsPaneOpen = !Shell.IsPaneOpen;

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
        EventDraft Draft(string title) => new() { Title = title, Start = e.Start, End = e.End, IsAllDay = e.IsAllDay };
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
        return CreateWithEditorAsync(new EventDraft { Title = "", Start = start, End = start.AddHours(1) }, ViewModel.DefaultCalendar);
    }

    private async Task CreateWithEditorAsync(EventDraft draft, CalendarChoice? calendar)
    {
        var dialog = new EventEditorDialog(Content.XamlRoot, "Sự kiện mới", draft, ViewModel.WritableCalendars,
            calendar ?? ViewModel.DefaultCalendar, calendarLocked: false, note: null);
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
            calendar is null ? Array.Empty<CalendarChoice>() : new[] { calendar }, calendar, calendarLocked: true, note);
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
