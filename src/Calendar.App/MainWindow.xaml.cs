using Calendar.App.ViewModels;
using Calendar.Core.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

    // ---- Keyboard shortcuts (ui.md): T today, D/W/M view, ←/→ previous/next ----------------

    private void AddShortcuts()
    {
        Add(VirtualKey.T, arrowKey: false, () => ViewModel.GoToTodayCommand.Execute(null));
        Add(VirtualKey.D, arrowKey: false, () => ViewModel.ViewMode = CalendarViewMode.Day);
        Add(VirtualKey.W, arrowKey: false, () => ViewModel.ViewMode = CalendarViewMode.Week);
        Add(VirtualKey.M, arrowKey: false, () => ViewModel.ViewMode = CalendarViewMode.Month);
        Add(VirtualKey.Left, arrowKey: true, () => ViewModel.PreviousCommand.Execute(null));
        Add(VirtualKey.Right, arrowKey: true, () => ViewModel.NextCommand.Execute(null));

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
