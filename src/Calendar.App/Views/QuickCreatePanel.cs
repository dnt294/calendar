using Calendar.App.Helpers;
using Calendar.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace Calendar.App.Views;

/// <summary>
/// Quick create flyout (task 6.2, ui.md): title, the chosen time (already filled), calendar
/// (defaults to the last used / primary one), Save, or "Thêm chi tiết…" for the full editor.
/// </summary>
internal sealed class QuickCreatePanel : StackPanel
{
    private readonly TextBox _title;
    private readonly ComboBox _calendar;

    public QuickCreatePanel(DateTimeOffset start, DateTimeOffset end, bool isAllDay,
        IReadOnlyList<CalendarChoice> calendars, CalendarChoice? selected,
        Action<string, CalendarChoice> onSave, Action<string, CalendarChoice?> onMoreDetails)
    {
        Spacing = 10;
        Width = 340;

        _title = new TextBox { PlaceholderText = "Thêm tiêu đề", FontSize = 16 };
        Children.Add(_title);

        Children.Add(new TextBlock
        {
            Text = EventText.When(start, end, isAllDay),
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap,
        });

        _calendar = new ComboBox
        {
            ItemsSource = calendars,
            SelectedItem = selected,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Header = "Lịch",
        };
        Children.Add(_calendar);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var more = new Button { Content = "Thêm chi tiết…" };
        var save = new Button { Content = "Lưu", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        buttons.Children.Add(more);
        buttons.Children.Add(save);
        Children.Add(buttons);

        void Save()
        {
            if (_calendar.SelectedItem is CalendarChoice calendar)
                onSave(TitleOrDefault(), calendar);
        }
        save.Click += (_, _) => Save();
        _title.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                Save();
            }
        };
        more.Click += (_, _) => onMoreDetails(_title.Text.Trim(), _calendar.SelectedItem as CalendarChoice);

        Loaded += (_, _) => _title.Focus(FocusState.Programmatic);
    }

    private string TitleOrDefault() => string.IsNullOrWhiteSpace(_title.Text) ? "(Không có tiêu đề)" : _title.Text.Trim();
}
