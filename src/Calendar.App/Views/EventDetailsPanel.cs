using Calendar.App.Helpers;
using Calendar.App.ViewModels;
using Calendar.Core.Models;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Calendar.App.Views;

/// <summary>
/// Content of the event details flyout (task 6.1, ui.md): title, time, calendar/account, location,
/// Join button, guests with their responses, description; Edit and Delete when allowed.
/// </summary>
internal sealed class EventDetailsPanel : StackPanel
{
    private const int MaxGuestsShown = 12;

    public EventDetailsPanel(CalendarEvent ev, CalendarChoice? calendar, bool canWrite, string writeDisabledReason,
        Action onEdit, Action onDelete)
    {
        Spacing = 8;
        Width = 360;

        var color = ColorHex.Parse(calendar?.Calendar.EffectiveColor);
        var header = new Grid { ColumnSpacing = 10 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.Children.Add(new Rectangle
        {
            Width = 14, Height = 14, RadiusX = 3, RadiusY = 3, Margin = new Thickness(0, 6, 0, 0),
            VerticalAlignment = VerticalAlignment.Top, Fill = new SolidColorBrush(color),
        });
        var title = new TextBlock
        {
            Text = ev.Title,
            Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };
        Grid.SetColumn(title, 1);
        header.Children.Add(title);
        Children.Add(header);

        AddLine("", EventText.When(ev) + (ev.IsOccurrence ? "  ·  Lặp lại" : ""));
        if (calendar is not null)
            AddLine("", calendar.Display);
        if (!string.IsNullOrWhiteSpace(ev.Location))
            AddLine("", ev.Location!, selectable: true);

        if (Uri.TryCreate(ev.MeetingUrl, UriKind.Absolute, out var meeting))
        {
            var join = new Button
            {
                Content = "Tham gia cuộc họp",
                Style = (Style)Application.Current.Resources["AccentButtonStyle"],
                Margin = new Thickness(24, 0, 0, 0),
            };
            ToolTipService.SetToolTip(join, meeting.ToString());
            join.Click += async (_, _) => await Windows.System.Launcher.LaunchUriAsync(meeting);
            Children.Add(join);
        }

        var guests = ev.Attendees.ToList();
        if (guests.Count > 0)
        {
            var list = new StackPanel { Spacing = 2, Margin = new Thickness(24, 0, 0, 0) };
            list.Children.Add(new TextBlock { Text = $"{guests.Count} khách mời", FontWeight = FontWeights.SemiBold });
            foreach (var a in guests.Take(MaxGuestsShown))
            {
                var name = string.IsNullOrWhiteSpace(a.DisplayName) ? a.Email : $"{a.DisplayName} ({a.Email})";
                var tags = new[] { a.IsOrganizer ? "người tổ chức" : null, a.IsOptional ? "không bắt buộc" : null, EventText.Rsvp(a.Status) }
                    .Where(t => !string.IsNullOrEmpty(t));
                list.Children.Add(new TextBlock
                {
                    Text = $"{name} — {string.Join(", ", tags)}",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Foreground = (Brush)Application.Current.Resources[a.Status == RsvpStatus.Declined ? "TextFillColorTertiaryBrush" : "TextFillColorSecondaryBrush"],
                    TextDecorations = a.Status == RsvpStatus.Declined ? Windows.UI.Text.TextDecorations.Strikethrough : Windows.UI.Text.TextDecorations.None,
                });
            }
            if (guests.Count > MaxGuestsShown)
                list.Children.Add(new TextBlock { Text = $"… và {guests.Count - MaxGuestsShown} người khác", FontSize = 12 });
            AddWithIcon("", list);
        }

        var description = HtmlText.ToPlainText(ev.Description);
        if (description.Length > 0)
        {
            var text = new TextBlock
            {
                Text = description, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontSize = 13,
            };
            AddWithIcon("", new ScrollViewer { Content = text, MaxHeight = 200, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        }

        var editable = calendar?.Calendar.CanEdit == true;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 0) };
        var edit = new Button { Content = "Sửa", IsEnabled = editable && canWrite };
        var delete = new Button { Content = "Xoá", IsEnabled = editable && canWrite };
        var reason = !editable ? "Lịch chỉ đọc" : canWrite ? null : writeDisabledReason;
        if (reason is not null)
        {
            ToolTipService.SetToolTip(edit, reason);
            ToolTipService.SetToolTip(delete, reason);
        }
        edit.Click += (_, _) => onEdit();
        delete.Click += (_, _) => onDelete();
        buttons.Children.Add(edit);
        buttons.Children.Add(delete);
        Children.Add(buttons);
    }

    private void AddLine(string glyph, string text, bool selectable = false) =>
        AddWithIcon(glyph, new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = selectable });

    private void AddWithIcon(string glyph, FrameworkElement content)
    {
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) });
        if (content.Margin.Left > 0)
            content.Margin = new Thickness(0, content.Margin.Top, content.Margin.Right, content.Margin.Bottom);
        Grid.SetColumn(content, 1);
        row.Children.Add(content);
        Children.Add(row);
    }
}
