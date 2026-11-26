using System.Globalization;
using Calendar.App.Helpers;
using Calendar.Core.Layout;
using Calendar.Core.Models;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Calendar.App.Controls;

/// <summary>
/// Month view as a continuous, virtualized list of week rows (±10 years around today) that scrolls
/// freely from month to month. About six rows fill the viewport; the month in the middle of the
/// viewport is the "current" one (header label, dimmed days of other months). Events are placed per
/// row with <see cref="MonthLayout"/>; overflowing days show "+N nữa" with a flyout.
/// </summary>
public sealed partial class MonthGrid : UserControl
{
    private const double DayHeaderHeight = 28;
    private const double LaneHeight = 22;
    private const double Gap = 2;
    private const double VisibleRows = 6;
    private const double MinRowHeight = 90;

    /// <summary>TODO(Phase 9): first day of week from settings (must match the view model).</summary>
    private readonly MonthScroll _scroll = new(DateOnly.FromDateTime(DateTime.Today), DayOfWeek.Monday, yearsEachSide: 10);
    private readonly Dictionary<int, Grid> _realized = new();
    private readonly UniformGridLayout _layout;
    private Dictionary<(string, string), string> _colors = new();
    private double _rowHeight = MinRowHeight;
    private DateOnly _visibleMonth;
    private double? _pendingOffset;

    public MonthGrid()
    {
        InitializeComponent();

        // One column of equally tall rows: the extent is exact without realizing rows, so
        // jumping to any month (ChangeView) lands precisely.
        _layout = new UniformGridLayout
        {
            Orientation = Orientation.Horizontal,
            MaximumRowsOrColumns = 1,
            ItemsStretch = UniformGridLayoutItemsStretch.Fill,
            MinItemWidth = 1,
            MinItemHeight = _rowHeight,
        };
        Weeks.Layout = _layout;
        Weeks.ItemsSource = Enumerable.Range(0, _scroll.WeekCount).ToList();
        Weeks.ElementPrepared += (_, e) =>
        {
            var row = (Grid)e.Element;
            _realized[e.Index] = row;
            FillRow(row, e.Index);
        };
        Weeks.ElementClearing += (_, e) =>
        {
            foreach (var key in _realized.Where(kv => ReferenceEquals(kv.Value, e.Element)).Select(kv => kv.Key).ToList())
                _realized.Remove(key);
        };

        Scroller.ViewChanged += (_, _) => OnScrolled();
        Scroller.LayoutUpdated += (_, _) => ApplyPendingScroll();
        Scroller.SizeChanged += (_, e) => OnViewportResized(e.NewSize.Height);
        Loaded += (_, _) =>
        {
            BuildHeader();
            // Let the repeater measure first, then jump without animation.
            DispatcherQueue.TryEnqueue(() => ScrollToMonth(MonthOf(DisplayMonth)));
        };
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) =>
        {
            if (Visibility == Visibility.Visible && IsLoaded)
                DispatcherQueue.TryEnqueue(() => ScrollToMonth(MonthOf(DisplayMonth)));
        });
    }

    /// <summary>A day number was clicked (the shell opens the Day view).</summary>
    public event EventHandler<DateOnly>? DayRequested;

    /// <summary>The user scrolled to another month (first day of it).</summary>
    public event EventHandler<DateOnly>? VisibleMonthChanged;

    /// <summary>An event (bar, item or entry of a "+N" list) was clicked.</summary>
    public event EventHandler<EventClickedEventArgs>? EventClicked;

    /// <summary>Empty space of a day cell was clicked: new all-day event on that day.</summary>
    public event EventHandler<SlotSelectedEventArgs>? SlotSelected;

    // ---- Properties -------------------------------------------------------------------------

    public static readonly DependencyProperty DisplayMonthProperty = DependencyProperty.Register(
        nameof(DisplayMonth), typeof(DateOnly), typeof(MonthGrid),
        new PropertyMetadata(DateOnly.FromDateTime(DateTime.Today), (d, _) => ((MonthGrid)d).OnDisplayMonthChanged()));

    /// <summary>Any day of the month to show; setting a different month scrolls to it.</summary>
    public DateOnly DisplayMonth
    {
        get => (DateOnly)GetValue(DisplayMonthProperty);
        set => SetValue(DisplayMonthProperty, value);
    }

    public static readonly DependencyProperty EventsProperty = DependencyProperty.Register(
        nameof(Events), typeof(IReadOnlyList<CalendarEvent>), typeof(MonthGrid),
        new PropertyMetadata(null, (d, _) => ((MonthGrid)d).RefreshRows()));

    public IReadOnlyList<CalendarEvent>? Events
    {
        get => (IReadOnlyList<CalendarEvent>?)GetValue(EventsProperty);
        set => SetValue(EventsProperty, value);
    }

    public static readonly DependencyProperty CalendarsProperty = DependencyProperty.Register(
        nameof(Calendars), typeof(IReadOnlyList<CalendarInfo>), typeof(MonthGrid),
        new PropertyMetadata(null, (d, _) => ((MonthGrid)d).OnCalendarsChanged()));

    public IReadOnlyList<CalendarInfo>? Calendars
    {
        get => (IReadOnlyList<CalendarInfo>?)GetValue(CalendarsProperty);
        set => SetValue(CalendarsProperty, value);
    }

    // ---- Scrolling --------------------------------------------------------------------------

    private static DateOnly MonthOf(DateOnly date) => new(date.Year, date.Month, 1);

    private void OnDisplayMonthChanged()
    {
        var month = MonthOf(DisplayMonth);
        // Echo of our own VisibleMonthChanged, or not shown yet: nothing to do.
        if (month == _visibleMonth || !IsLoaded || Visibility != Visibility.Visible)
            return;
        ScrollToMonth(month);
    }

    private void ScrollToMonth(DateOnly month)
    {
        _visibleMonth = month;
        ScrollTo(_scroll.TopWeekFor(month) * _rowHeight);
        RefreshRows();
    }

    /// <summary>
    /// ChangeView is silently clamped while the repeater has not measured its full height yet (first
    /// show, switching into Month view), so the target is kept and retried after each layout pass.
    /// </summary>
    private void ScrollTo(double offset)
    {
        _pendingOffset = offset;
        ApplyPendingScroll();
    }

    private void ApplyPendingScroll()
    {
        if (_pendingOffset is not { } offset || Scroller.ScrollableHeight + 0.5 < offset)
            return;
        if (Scroller.ChangeView(null, offset, null, disableAnimation: true))
            _pendingOffset = null;
    }

    private void OnScrolled()
    {
        // Ignore offsets seen before a pending jump has landed (they belong to the old position).
        if (_pendingOffset is not null || _rowHeight <= 0 || Scroller.ViewportHeight <= 0)
            return;
        var month = _scroll.MonthAtCenter(Scroller.VerticalOffset / _rowHeight, Scroller.ViewportHeight / _rowHeight);
        if (month == _visibleMonth)
            return;
        _visibleMonth = month;
        RefreshRows();                       // re-dim days of other months
        VisibleMonthChanged?.Invoke(this, month);
    }

    private void OnViewportResized(double height)
    {
        var rowHeight = Math.Max(MinRowHeight, Math.Floor(height / VisibleRows));
        if (Math.Abs(rowHeight - _rowHeight) < 0.5)
            return;

        // Keep the same week at the top while rows change height. A jump that has not landed yet
        // (first show: this runs right after ScrollToMonth) wins over the current offset.
        var topRow = (_pendingOffset ?? Scroller.VerticalOffset) / _rowHeight;
        _rowHeight = rowHeight;
        _layout.MinItemHeight = rowHeight;
        RefreshRows();
        ScrollTo(topRow * _rowHeight);
    }

    // ---- Rows -------------------------------------------------------------------------------

    private int Lanes => Math.Max(1, (int)((_rowHeight - DayHeaderHeight - Gap) / LaneHeight));

    private void OnCalendarsChanged()
    {
        _colors = (Calendars ?? []).ToDictionary(c => (c.AccountId, c.Id), c => c.EffectiveColor);
        RefreshRows();
    }

    private void RefreshRows()
    {
        foreach (var (index, row) in _realized)
            FillRow(row, index);
    }

    private void FillRow(Grid row, int index)
    {
        var lanes = Lanes;
        var week = MonthLayout.Arrange(Events ?? [], _scroll.WeekStart(index), 1, lanes, TimeZoneInfo.Local)[0];
        BuildWeek(row, week, lanes);
    }

    private void BuildHeader()
    {
        HeaderGrid.Children.Clear();
        HeaderGrid.ColumnDefinitions.Clear();
        var culture = CultureInfo.CurrentCulture;
        for (var i = 0; i < 7; i++)
        {
            HeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var label = new TextBlock
            {
                Text = culture.DateTimeFormat.GetAbbreviatedDayName(_scroll.Origin.AddDays(i).DayOfWeek),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = Brush("TextFillColorSecondaryBrush"),
            };
            Grid.SetColumn(label, i);
            HeaderGrid.Children.Add(label);
        }
    }

    private void BuildWeek(Grid grid, MonthWeek week, int lanes)
    {
        grid.Children.Clear();
        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();
        grid.Height = _rowHeight;
        for (var i = 0; i < 7; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(DayHeaderHeight) });
        for (var l = 0; l < lanes; l++)
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(LaneHeight) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var today = DateOnly.FromDateTime(DateTime.Today);
        var divider = Brush("DividerStrokeColorDefaultBrush");

        for (var d = 0; d < 7; d++)
        {
            var day = week.Start.AddDays(d);
            var isWeekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var cell = new Border
            {
                BorderBrush = divider,
                BorderThickness = new Thickness(d == 0 ? 0 : 1, 0, 0, 1),
                // Transparent (not null) so the empty part of the cell receives clicks.
                Background = isWeekend ? Brush("SubtleFillColorSecondaryBrush") : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            };
            var cellDay = day;
            cell.Tapped += (_, _) =>
            {
                var (s, e) = CalendarEvent.AllDaySpan(cellDay, cellDay);
                SlotSelected?.Invoke(this, new SlotSelectedEventArgs(s, e, true, cell));
            };
            Grid.SetColumn(cell, d);
            Grid.SetRowSpan(cell, lanes + 2);
            grid.Children.Add(cell);

            var inMonth = day.Year == _visibleMonth.Year && day.Month == _visibleMonth.Month;
            grid.Children.Add(DayNumber(day, d, day == today, inMonth));

            if (week.HiddenCounts[d] > 0)
                grid.Children.Add(MoreButton(day, d, week.HiddenCounts[d], week.EventsByDay[d], lanes, _colors));
        }

        foreach (var bar in week.Bars)
        {
            var color = ColorHex.Parse(_colors.GetValueOrDefault((bar.Event.AccountId, bar.Event.CalendarId)));
            FrameworkElement element = bar.IsBlock ? BlockBar(bar, color) : TimedItem(bar.Event, color);
            ToolTipService.SetToolTip(element, Describe(bar.Event));
            AttachClick(element, bar.Event);
            Grid.SetRow(element, bar.Lane + 1);
            Grid.SetColumn(element, bar.FirstColumn);
            Grid.SetColumnSpan(element, bar.ColumnSpan);
            grid.Children.Add(element);
        }
    }
    private Button DayNumber(DateOnly day, int column, bool isToday, bool inMonth)
    {
        var button = new Button
        {
            Content = day.Day == 1 ? day.ToString("d MMM", CultureInfo.CurrentCulture) : day.Day.ToString(CultureInfo.CurrentCulture),
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
            Padding = new Thickness(6, 0, 6, 0),
            MinHeight = 24,
            Height = 24,
            Margin = new Thickness(4, 2, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            FontSize = 12,
            FontWeight = isToday ? FontWeights.SemiBold : FontWeights.Normal,
            CornerRadius = new CornerRadius(12),
        };
        if (isToday)
        {
            button.Background = Brush("AccentFillColorDefaultBrush");
            button.Foreground = Brush("TextOnAccentFillColorPrimaryBrush");
        }
        else if (!inMonth)
        {
            button.Foreground = Brush("TextFillColorTertiaryBrush");
        }
        ToolTipService.SetToolTip(button, "Xem ngày này");
        button.Click += (_, _) => DayRequested?.Invoke(this, day);
        Grid.SetColumn(button, column);
        return button;
    }

    private static Border BlockBar(MonthBar bar, Color color) => new()
    {
        Background = new SolidColorBrush(ColorHex.WithAlpha(color, 0x40)),
        BorderBrush = new SolidColorBrush(color),
        BorderThickness = new Thickness(bar.ContinuesBefore ? 0 : 3, 0, 0, 0),
        CornerRadius = new CornerRadius(bar.ContinuesBefore ? 0 : 4, bar.ContinuesAfter ? 0 : 4,
            bar.ContinuesAfter ? 0 : 4, bar.ContinuesBefore ? 0 : 4),
        Margin = new Thickness(bar.ContinuesBefore ? 0 : Gap, 1, bar.ContinuesAfter ? 0 : Gap, 1),
        Padding = new Thickness(6, 0, 4, 0),
        Child = new TextBlock
        {
            Text = bar.Event.Title,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        },
    };

    private static StackPanel TimedItem(CalendarEvent ev, Color color)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(6, 1, Gap, 1),
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(new TextBlock
        {
            Text = $"{ev.Start.ToLocalTime():t} {ev.Title}",
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return panel;
    }

    private Button MoreButton(DateOnly day, int column, int hidden, IReadOnlyList<CalendarEvent> all, int lanes,
        Dictionary<(string, string), string> colors)
    {
        var list = new StackPanel { Spacing = 4, MinWidth = 220, MaxWidth = 320 };
        list.Children.Add(new TextBlock
        {
            Text = day.ToString(CultureInfo.CurrentCulture.DateTimeFormat.LongDatePattern, CultureInfo.CurrentCulture),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            Margin = new Thickness(0, 0, 0, 4),
        });
        foreach (var ev in all)
        {
            var color = ColorHex.Parse(colors.GetValueOrDefault((ev.AccountId, ev.CalendarId)));
            var item = TimedItem(ev, color);
            if (ev.IsAllDay)
                ((TextBlock)item.Children[1]).Text = ev.Title;
            item.Margin = new Thickness(0);
            item.Padding = new Thickness(4, 2, 4, 2);
            ToolTipService.SetToolTip(item, Describe(ev));
            AttachClick(item, ev, fromMoreList: true);
            list.Children.Add(item);
        }

        var button = new Button
        {
            Content = $"+{hidden} nữa",
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
            Foreground = Brush("AccentTextFillColorPrimaryBrush"),
            FontSize = 12,
            MinHeight = 0,
            Padding = new Thickness(6, 0, 6, 0),
            Margin = new Thickness(0, 1, 0, 1),
            HorizontalAlignment = HorizontalAlignment.Left,
            Flyout = new Flyout { Content = list, Placement = FlyoutPlacementMode.Bottom },
        };
        button.Click += (_, _) => _openMoreButton = button;
        // The line kept free by MonthLayout is the last lane.
        Grid.SetRow(button, lanes);
        Grid.SetColumn(button, column);
        return button;
    }

    private Button? _openMoreButton;

    /// <summary>Clicking an event opens its details; the click must not also count as a click on the cell.</summary>
    private void AttachClick(FrameworkElement element, CalendarEvent ev, bool fromMoreList = false)
    {
        if (element is Panel panel)
            panel.Background ??= new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        element.Tapped += (_, e) =>
        {
            e.Handled = true;
            var anchor = element;
            if (fromMoreList && _openMoreButton is { } more)
            {
                // Close the list first; the details then open at the day's "+N" button.
                more.Flyout?.Hide();
                anchor = more;
            }
            EventClicked?.Invoke(this, new EventClickedEventArgs(ev, anchor));
        };
    }

    private static string Describe(CalendarEvent ev)
    {
        var when = ev.IsAllDay ? "Cả ngày" : $"{ev.Start.ToLocalTime():g} – {ev.End.ToLocalTime():t}";
        return string.IsNullOrEmpty(ev.Location) ? $"{ev.Title}\n{when}" : $"{ev.Title}\n{when}\n{ev.Location}";
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
