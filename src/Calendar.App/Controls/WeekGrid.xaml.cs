using System.Globalization;
using Calendar.App.Helpers;
using Calendar.Core.Layout;
using Calendar.Core.Models;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Calendar.App.Controls;

/// <summary>
/// Day/Week time grid: hour gutter, N day columns, all-day band, current-time line.
/// Timed events are placed with <see cref="DayLayout"/>, all-day events with <see cref="AllDayLayout"/>.
/// </summary>
public sealed partial class WeekGrid : UserControl
{
    private const double GutterWidth = 56;
    private const double AllDayRowHeight = 22;
    private const double EventGap = 2;
    private static readonly TimeSpan MinEventDuration = TimeSpan.FromMinutes(20);

    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly List<Canvas> _dayCanvases = [];
    private static readonly TimeSpan SnapStep = TimeSpan.FromMinutes(15);
    private Border? _selection;
    private (Canvas Canvas, double StartY)? _drag;

    /// <summary>An event block was clicked.</summary>
    public event EventHandler<EventClickedEventArgs>? EventClicked;

    /// <summary>Empty time (or the all-day band) was clicked or dragged over.</summary>
    public event EventHandler<SlotSelectedEventArgs>? SlotSelected;

    /// <summary>Removes the highlighted slot (call when the quick-create flyout closes).</summary>
    public void ClearSelection()
    {
        if (_selection?.Parent is Canvas parent)
            parent.Children.Remove(_selection);
        _selection = null;
    }
    private Rectangle? _nowLine;
    private Ellipse? _nowDot;
    private bool _scrolledInitially;

    public WeekGrid()
    {
        InitializeComponent();
        _clock.Tick += (_, _) => UpdateNowLine();
        Loaded += (_, _) =>
        {
            _clock.Start();
            Rebuild();
        };
        Unloaded += (_, _) => _clock.Stop();
        // Hidden in Month view (where Days is 35-42): skip work then, catch up when shown again.
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => Rebuild());
    }

    // ---- Properties -------------------------------------------------------------------------

    public static readonly DependencyProperty StartDateProperty = DependencyProperty.Register(
        nameof(StartDate), typeof(DateOnly), typeof(WeekGrid),
        new PropertyMetadata(DateOnly.FromDateTime(DateTime.Today), OnLayoutPropertyChanged));

    /// <summary>First visible day.</summary>
    public DateOnly StartDate
    {
        get => (DateOnly)GetValue(StartDateProperty);
        set => SetValue(StartDateProperty, value);
    }

    public static readonly DependencyProperty DaysProperty = DependencyProperty.Register(
        nameof(Days), typeof(int), typeof(WeekGrid), new PropertyMetadata(7, OnLayoutPropertyChanged));

    /// <summary>Number of day columns (7 = week, 1 = day view).</summary>
    public int Days
    {
        get => (int)GetValue(DaysProperty);
        set => SetValue(DaysProperty, value);
    }

    public static readonly DependencyProperty EventsProperty = DependencyProperty.Register(
        nameof(Events), typeof(IReadOnlyList<CalendarEvent>), typeof(WeekGrid),
        new PropertyMetadata(null, OnLayoutPropertyChanged));

    public IReadOnlyList<CalendarEvent>? Events
    {
        get => (IReadOnlyList<CalendarEvent>?)GetValue(EventsProperty);
        set => SetValue(EventsProperty, value);
    }

    public static readonly DependencyProperty CalendarsProperty = DependencyProperty.Register(
        nameof(Calendars), typeof(IReadOnlyList<CalendarInfo>), typeof(WeekGrid),
        new PropertyMetadata(null, OnLayoutPropertyChanged));

    /// <summary>Used to color events by their calendar.</summary>
    public IReadOnlyList<CalendarInfo>? Calendars
    {
        get => (IReadOnlyList<CalendarInfo>?)GetValue(CalendarsProperty);
        set => SetValue(CalendarsProperty, value);
    }

    public static readonly DependencyProperty HourHeightProperty = DependencyProperty.Register(
        nameof(HourHeight), typeof(double), typeof(WeekGrid), new PropertyMetadata(48.0, OnLayoutPropertyChanged));

    public double HourHeight
    {
        get => (double)GetValue(HourHeightProperty);
        set => SetValue(HourHeightProperty, value);
    }

    /// <summary>Hour the grid scrolls to when first shown.</summary>
    public int ScrollToHour { get; set; } = 8;

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((WeekGrid)d).Rebuild();

    private static TimeZoneInfo Zone => TimeZoneInfo.Local;

    // ---- Build ------------------------------------------------------------------------------

    private void Rebuild()
    {
        if (!IsLoaded || Visibility != Visibility.Visible)
            return;

        var days = Math.Max(1, Days);
        var events = Events ?? [];
        var colors = (Calendars ?? []).ToDictionary(c => (c.AccountId, c.Id), c => c.EffectiveColor);

        SetupColumns(HeaderGrid, days);
        SetupColumns(AllDayGrid, days);
        SetupColumns(BodyGrid, days);

        BuildHeader(days);
        BuildAllDay(events, days, colors);
        BuildBody(events, days, colors);
        UpdateNowLine();

        if (!_scrolledInitially)
        {
            _scrolledInitially = true;
            // Wait for the ScrollViewer to measure the new content before scrolling.
            DispatcherQueue.TryEnqueue(() => Scroller.ChangeView(null, ScrollToHour * HourHeight, null, disableAnimation: true));
        }
    }

    private static void SetupColumns(Grid grid, int days)
    {
        grid.Children.Clear();
        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(GutterWidth) });
        for (var i = 0; i < days; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
    }

    private void BuildHeader(int days)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var culture = CultureInfo.CurrentCulture;
        for (var i = 0; i < days; i++)
        {
            var day = StartDate.AddDays(i);
            var isToday = day == today;

            var number = new TextBlock
            {
                Text = day.Day.ToString(culture),
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = isToday ? Brush("TextOnAccentFillColorPrimaryBrush") : Brush("TextFillColorPrimaryBrush"),
            };
            var numberHost = new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(18),
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = isToday ? Brush("AccentFillColorDefaultBrush") : null,
                Child = number,
            };
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(new TextBlock
            {
                Text = culture.DateTimeFormat.GetAbbreviatedDayName(day.DayOfWeek),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = Brush(isToday ? "AccentTextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush"),
            });
            stack.Children.Add(numberHost);
            Grid.SetColumn(stack, i + 1);
            HeaderGrid.Children.Add(stack);
        }
    }

    private void BuildAllDay(IReadOnlyList<CalendarEvent> events, int days,
        Dictionary<(string, string), string> colors)
    {
        var layout = AllDayLayout.Arrange(events, StartDate, days);
        for (var r = 0; r < Math.Max(1, layout.RowCount); r++)
            AllDayGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(AllDayRowHeight) });

        var label = new TextBlock
        {
            Text = "cả ngày",
            FontSize = 11,
            Foreground = Brush("TextFillColorTertiaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        AllDayGrid.Children.Add(label);

        // Transparent hit targets per day (behind the bars): click → new all-day event on that day.
        for (var i = 0; i < days; i++)
        {
            var day = StartDate.AddDays(i);
            var target = new Border { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
            target.Tapped += (_, _) =>
            {
                var (s, e) = CalendarEvent.AllDaySpan(day, day);
                SlotSelected?.Invoke(this, new SlotSelectedEventArgs(s, e, true, target));
            };
            Grid.SetColumn(target, i + 1);
            Grid.SetRowSpan(target, Math.Max(1, layout.RowCount));
            AllDayGrid.Children.Add(target);
        }

        foreach (var item in layout.Items)
        {
            var color = ColorHex.Parse(colors.GetValueOrDefault((item.Event.AccountId, item.Event.CalendarId)));
            var block = new Border
            {
                Background = new SolidColorBrush(ColorHex.WithAlpha(color, 0x40)),
                BorderBrush = new SolidColorBrush(color),
                BorderThickness = new Thickness(item.ContinuesBefore ? 0 : 3, 0, 0, 0),
                CornerRadius = new CornerRadius(item.ContinuesBefore ? 0 : 4, item.ContinuesAfter ? 0 : 4,
                    item.ContinuesAfter ? 0 : 4, item.ContinuesBefore ? 0 : 4),
                Margin = new Thickness(EventGap, 1, EventGap, 1),
                Padding = new Thickness(6, 0, 6, 0),
                Child = new TextBlock
                {
                    Text = item.Event.Title,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            };
            ToolTipService.SetToolTip(block, item.Event.Title);
            RsvpLook.Decorate(block, item.Event, color);
            var clicked = item.Event;
            block.Tapped += (_, e) =>
            {
                e.Handled = true;
                EventClicked?.Invoke(this, new EventClickedEventArgs(clicked, block));
            };
            Grid.SetRow(block, item.Row);
            Grid.SetColumn(block, item.FirstColumn + 1);
            Grid.SetColumnSpan(block, item.ColumnSpan);
            AllDayGrid.Children.Add(block);
        }
    }

    private void BuildBody(IReadOnlyList<CalendarEvent> events, int days,
        Dictionary<(string, string), string> colors)
    {
        _dayCanvases.Clear();
        _nowLine = null;
        _nowDot = null;
        for (var h = 0; h < 24; h++)
            BodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HourHeight) });

        var divider = Brush("DividerStrokeColorDefaultBrush");

        // Hour labels + horizontal lines.
        for (var h = 0; h < 24; h++)
        {
            if (h > 0)
            {
                var label = new TextBlock
                {
                    Text = new DateTime(2000, 1, 1, h, 0, 0).ToString("t", CultureInfo.CurrentCulture),
                    FontSize = 11,
                    Foreground = Brush("TextFillColorTertiaryBrush"),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, -8, 8, 0),
                };
                Grid.SetRow(label, h);
                BodyGrid.Children.Add(label);
            }

            var line = new Border { BorderBrush = divider, BorderThickness = new Thickness(0, h == 0 ? 0 : 1, 0, 0) };
            Grid.SetRow(line, h);
            Grid.SetColumn(line, 1);
            Grid.SetColumnSpan(line, days);
            BodyGrid.Children.Add(line);
        }

        // Day columns: separator, weekend tint, event canvas.
        for (var i = 0; i < days; i++)
        {
            var day = StartDate.AddDays(i);
            var isWeekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

            var column = new Border
            {
                BorderBrush = divider,
                BorderThickness = new Thickness(1, 0, 0, 0),
                Background = isWeekend ? Brush("SubtleFillColorSecondaryBrush") : null,
            };
            Grid.SetColumn(column, i + 1);
            Grid.SetRowSpan(column, 24);
            BodyGrid.Children.Add(column);

            // Transparent background so empty time receives clicks (a null background is not hit-tested).
            var canvas = new Canvas { Tag = day, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
            canvas.PointerPressed += OnCanvasPointerPressed;
            canvas.PointerMoved += OnCanvasPointerMoved;
            canvas.PointerReleased += OnCanvasPointerReleased;
            canvas.PointerCaptureLost += (_, _) => _drag = null;
            Grid.SetColumn(canvas, i + 1);
            Grid.SetRowSpan(canvas, 24);
            BodyGrid.Children.Add(canvas);
            _dayCanvases.Add(canvas);

            var range = DateRange.ForDays(day, 1, Zone);
            foreach (var item in DayLayout.Arrange(events, range, MinEventDuration))
            {
                var color = ColorHex.Parse(colors.GetValueOrDefault((item.Event.AccountId, item.Event.CalendarId)));
                var block = CreateEventBlock(item, day, color);
                canvas.Children.Add(block);
            }

            canvas.SizeChanged += (_, e) => PositionEvents((Canvas)canvas, e.NewSize.Width);
        }
    }

    private Border CreateEventBlock(DayLayoutItem item, DateOnly day, Color color)
    {
        var top = DayTimeline.MinutesFromMidnight(item.VisualStart, day, Zone) / 60 * HourHeight;
        var bottom = DayTimeline.MinutesFromMidnight(item.VisualEnd, day, Zone) / 60 * HourHeight;
        var height = Math.Max(bottom - top - EventGap, 12);

        var ev = item.Event;
        var time = $"{ev.Start.ToLocalTime():t} – {ev.End.ToLocalTime():t}";
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = ev.Title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = height > 40 ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MaxLines = Math.Max(1, (int)((height - 18) / 16)),
        });
        if (height >= 34)
        {
            content.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(ev.Location) ? time : $"{time} · {ev.Location}",
                FontSize = 11,
                Foreground = Brush("TextFillColorSecondaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        var block = new Border
        {
            Tag = item,
            Height = height,
            Background = new SolidColorBrush(ColorHex.WithAlpha(color, 0x40)),
            BorderBrush = new SolidColorBrush(color),
            BorderThickness = new Thickness(3, 0, 0, 0),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 4, 2),
            Child = content,
        };
        RsvpLook.Decorate(block, ev, color);
        ToolTipService.SetToolTip(block, string.IsNullOrEmpty(ev.Location) ? $"{ev.Title}\n{time}" : $"{ev.Title}\n{time}\n{ev.Location}");
        // Pressing an event must not start a slot drag on the canvas underneath.
        block.PointerPressed += (_, e) => e.Handled = true;
        block.Tapped += (_, e) =>
        {
            e.Handled = true;
            EventClicked?.Invoke(this, new EventClickedEventArgs(ev, block));
        };
        Canvas.SetTop(block, top);
        return block;
    }

    private static void PositionEvents(Canvas canvas, double width)
    {
        foreach (var child in canvas.Children)
        {
            if (child is not Border { Tag: DayLayoutItem item } block)
                continue;
            // Leave a small gap on the right so a click on empty space stays possible.
            var usable = Math.Max(0, width - 8);
            Canvas.SetLeft(block, item.Left * usable + EventGap / 2);
            block.Width = Math.Max(0, item.Width * usable - EventGap);
        }
    }

    private void UpdateNowLine()
    {
        if (_nowLine is not null)
            ((Canvas)_nowLine.Parent).Children.Remove(_nowLine);
        if (_nowDot is not null)
            ((Canvas)_nowDot.Parent).Children.Remove(_nowDot);
        _nowLine = null;
        _nowDot = null;

        var now = DateTimeOffset.Now;
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        var canvas = _dayCanvases.FirstOrDefault(c => (DateOnly)c.Tag == today);
        if (canvas is null)
            return;

        var y = DayTimeline.MinutesFromMidnight(now, today, Zone) / 60 * HourHeight;
        var brush = Brush("SystemFillColorCriticalBrush");
        _nowLine = new Rectangle { Height = 2, Fill = brush, IsHitTestVisible = false };
        _nowLine.SetBinding(WidthProperty, new Microsoft.UI.Xaml.Data.Binding { Source = canvas, Path = new PropertyPath("ActualWidth") });
        Canvas.SetTop(_nowLine, y - 1);
        _nowDot = new Ellipse { Width = 10, Height = 10, Fill = brush, IsHitTestVisible = false };
        Canvas.SetTop(_nowDot, y - 5);
        Canvas.SetLeft(_nowDot, -5);
        canvas.Children.Add(_nowLine);
        canvas.Children.Add(_nowDot);
    }

    // ---- Slot selection ---------------------------------------------------------------------

    private void OnCanvasPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var canvas = (Canvas)sender;
        if (!e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed)
            return;
        ClearSelection();
        var y = e.GetCurrentPoint(canvas).Position.Y;
        _drag = (canvas, y);
        canvas.CapturePointer(e.Pointer);
        ShowSelection(canvas, y, y);
        e.Handled = true;
    }

    private void OnCanvasPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_drag is not { } drag || !ReferenceEquals(sender, drag.Canvas))
            return;
        ShowSelection(drag.Canvas, drag.StartY, e.GetCurrentPoint(drag.Canvas).Position.Y);
    }

    private void OnCanvasPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_drag is not { } drag || !ReferenceEquals(sender, drag.Canvas))
            return;
        _drag = null;
        drag.Canvas.ReleasePointerCapture(e.Pointer);
        var (start, end) = SlotRange(drag.StartY, e.GetCurrentPoint(drag.Canvas).Position.Y);
        ShowSelection(drag.Canvas, start, end);
        var day = (DateOnly)drag.Canvas.Tag;
        SlotSelected?.Invoke(this, new SlotSelectedEventArgs(
            LocalTime.At(day, start), LocalTime.At(day, end), false, _selection!));
        e.Handled = true;
    }

    /// <summary>
    /// Snapped time range for a press at <paramref name="y1"/> released at <paramref name="y2"/>:
    /// a plain click gives one hour starting at the half hour clicked, a drag gives the dragged range.
    /// </summary>
    private (TimeSpan Start, TimeSpan End) SlotRange(double y1, double y2)
    {
        TimeSpan At(double y, TimeSpan step) => TimeSpan.FromMinutes(
            Math.Clamp(Math.Floor(y / HourHeight * 60 / step.TotalMinutes) * step.TotalMinutes, 0, DayTimeline.MinutesPerDay));

        var day = TimeSpan.FromDays(1);
        if (Math.Abs(y2 - y1) < HourHeight / 8)
        {
            var start = At(y1, TimeSpan.FromMinutes(30));
            return (start, start + TimeSpan.FromHours(1) > day ? day : start + TimeSpan.FromHours(1));
        }
        var a = At(Math.Min(y1, y2), SnapStep);
        var b = At(Math.Max(y1, y2), SnapStep) + SnapStep;
        return (a, b > day ? day : b);
    }

    private void ShowSelection(Canvas canvas, double y1, double y2) => ShowSelection(canvas, SlotRange(y1, y2).Start, SlotRange(y1, y2).End);

    private void ShowSelection(Canvas canvas, TimeSpan start, TimeSpan end)
    {
        if (_selection is null || !ReferenceEquals(_selection.Parent, canvas))
        {
            ClearSelection();
            _selection = new Border
            {
                Background = new SolidColorBrush(ColorHex.WithAlpha(((SolidColorBrush)Brush("AccentFillColorDefaultBrush")).Color, 0x50)),
                BorderBrush = Brush("AccentFillColorDefaultBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                IsHitTestVisible = false,
            };
            canvas.Children.Add(_selection);
        }
        Canvas.SetLeft(_selection, EventGap / 2);
        Canvas.SetTop(_selection, start.TotalHours * HourHeight);
        _selection.Width = Math.Max(0, canvas.ActualWidth - 8 - EventGap);
        _selection.Height = Math.Max(8, (end - start).TotalHours * HourHeight - EventGap);
    }

    // ---- Helpers ----------------------------------------------------------------------------

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
