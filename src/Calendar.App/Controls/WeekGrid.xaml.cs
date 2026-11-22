using System.Globalization;
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
        if (!IsLoaded)
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

        foreach (var item in layout.Items)
        {
            var color = ParseColor(colors.GetValueOrDefault((item.Event.AccountId, item.Event.CalendarId)));
            var block = new Border
            {
                Background = new SolidColorBrush(WithAlpha(color, 0x40)),
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

            var canvas = new Canvas { Tag = day };
            Grid.SetColumn(canvas, i + 1);
            Grid.SetRowSpan(canvas, 24);
            BodyGrid.Children.Add(canvas);
            _dayCanvases.Add(canvas);

            var range = DateRange.ForDays(day, 1, Zone);
            foreach (var item in DayLayout.Arrange(events, range, MinEventDuration))
            {
                var color = ParseColor(colors.GetValueOrDefault((item.Event.AccountId, item.Event.CalendarId)));
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
            Background = new SolidColorBrush(WithAlpha(color, 0x40)),
            BorderBrush = new SolidColorBrush(color),
            BorderThickness = new Thickness(3, 0, 0, 0),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 4, 2),
            Child = content,
        };
        ToolTipService.SetToolTip(block, string.IsNullOrEmpty(ev.Location) ? $"{ev.Title}\n{time}" : $"{ev.Title}\n{time}\n{ev.Location}");
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

    // ---- Helpers ----------------------------------------------------------------------------

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    private static Color ParseColor(string? hex)
    {
        if (hex is { Length: 7 } && hex[0] == '#' &&
            uint.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return Color.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return Colors.SteelBlue;
    }
}
