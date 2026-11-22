using System.Globalization;
using Calendar.App.Services;
using Calendar.Core.Layout;
using Calendar.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Calendar.App.ViewModels;

/// <summary>State of the main window: visible week, its label and its events.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private const int DaysInView = 7;

    /// <summary>TODO(Phase 9): read from settings.</summary>
    private static readonly DayOfWeek FirstDayOfWeek = DayOfWeek.Monday;

    public ShellViewModel()
    {
        Calendars = SampleData.Calendars;
        GoToToday();
    }

    [ObservableProperty]
    public partial DateOnly StartDate { get; set; }

    [ObservableProperty]
    public partial string RangeLabel { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<CalendarEvent> Events { get; set; } = [];

    public IReadOnlyList<CalendarInfo> Calendars { get; }

    public int Days => DaysInView;

    [RelayCommand]
    private void GoToToday() => StartDate = WeekNavigation.StartOfWeek(DateOnly.FromDateTime(DateTime.Today), FirstDayOfWeek);

    [RelayCommand]
    private void Previous() => StartDate = StartDate.AddDays(-DaysInView);

    [RelayCommand]
    private void Next() => StartDate = StartDate.AddDays(DaysInView);

    partial void OnStartDateChanged(DateOnly value)
    {
        RangeLabel = WeekNavigation.FormatRangeLabel(value, DaysInView, CultureInfo.CurrentCulture);
        // TODO(2.1): load from ICalendarStore instead of sample data.
        Events = SampleData.ForWeek(value);
    }
}
