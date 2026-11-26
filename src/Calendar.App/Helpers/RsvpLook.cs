using Calendar.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace Calendar.App.Helpers;

/// <summary>
/// Invite state drawn on event blocks (ui.md / task 7.2): dashed outline = not answered yet,
/// diagonal stripes = tentative, struck-through and faded = declined. Own events and accepted
/// invites keep the normal look.
/// </summary>
internal static class RsvpLook
{
    /// <summary>Restyles a block (Border with the title somewhere in its content) for the event's response.</summary>
    public static void Decorate(Border block, CalendarEvent ev, Color color)
    {
        switch (ev.SelfRsvp)
        {
            case RsvpStatus.NeedsAction:
                // Border cannot dash: draw the outline with a Rectangle laid over the content.
                var content = (UIElement)block.Child;
                var padding = block.Padding;
                block.Padding = new Thickness(0);
                block.BorderThickness = new Thickness(0);
                block.Background = new SolidColorBrush(ColorHex.WithAlpha(color, 0x14));
                if (content is FrameworkElement fe)
                    fe.Margin = padding;
                var outline = new Rectangle
                {
                    Stroke = new SolidColorBrush(color),
                    StrokeThickness = 1.5,
                    StrokeDashArray = [4, 2],
                    RadiusX = block.CornerRadius.TopLeft,
                    RadiusY = block.CornerRadius.TopLeft,
                    IsHitTestVisible = false,
                };
                block.Child = new Grid { Children = { outline, content } };
                break;

            case RsvpStatus.Tentative:
                block.Background = Stripes(color);
                break;

            case RsvpStatus.Declined:
                block.Opacity = 0.55;
                StrikeThrough(block.Child);
                break;
        }
    }

    /// <summary>Month view "● 9:00 Title" items: hollow dot = not answered, faded dot + italic = tentative, struck-through = declined.</summary>
    public static void Decorate(StackPanel item, Ellipse dot, CalendarEvent ev, Color color)
    {
        switch (ev.SelfRsvp)
        {
            case RsvpStatus.NeedsAction:
                dot.Fill = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                dot.Stroke = new SolidColorBrush(color);
                dot.StrokeThickness = 1.5;
                break;
            case RsvpStatus.Tentative:
                dot.Fill = Stripes(color);
                dot.Stroke = new SolidColorBrush(color);
                dot.StrokeThickness = 1;
                foreach (var text in item.Children.OfType<TextBlock>())
                    text.FontStyle = Windows.UI.Text.FontStyle.Italic;
                break;
            case RsvpStatus.Declined:
                item.Opacity = 0.55;
                StrikeThrough(item);
                break;
        }
    }

    /// <summary>Diagonal stripes in the calendar color (repeating gradient with hard stops).</summary>
    private static LinearGradientBrush Stripes(Color color) => new()
    {
        MappingMode = BrushMappingMode.Absolute,
        StartPoint = new Point(0, 0),
        EndPoint = new Point(6, 6),
        SpreadMethod = GradientSpreadMethod.Repeat,
        GradientStops =
        {
            new GradientStop { Color = ColorHex.WithAlpha(color, 0x55), Offset = 0 },
            new GradientStop { Color = ColorHex.WithAlpha(color, 0x55), Offset = 0.5 },
            new GradientStop { Color = ColorHex.WithAlpha(color, 0x12), Offset = 0.5 },
            new GradientStop { Color = ColorHex.WithAlpha(color, 0x12), Offset = 1 },
        },
    };

    private static void StrikeThrough(UIElement? element)
    {
        switch (element)
        {
            case TextBlock text:
                text.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
                break;
            case Panel panel:
                foreach (var child in panel.Children)
                    StrikeThrough(child);
                break;
            case Border border:
                StrikeThrough(border.Child);
                break;
        }
    }
}
