using Calendar.Core.Models;
using Microsoft.UI.Xaml;

namespace Calendar.App.Controls;

/// <summary>An event was clicked in a grid; <see cref="Anchor"/> is the element to show a flyout at.</summary>
public sealed class EventClickedEventArgs(CalendarEvent ev, FrameworkElement anchor) : EventArgs
{
    public CalendarEvent Event { get; } = ev;
    public FrameworkElement Anchor { get; } = anchor;
}

/// <summary>
/// Empty space was clicked or dragged over: a new event could go there. For all-day slots,
/// Start/End follow the floating all-day convention of <see cref="CalendarEvent"/>.
/// </summary>
public sealed class SlotSelectedEventArgs(DateTimeOffset start, DateTimeOffset end, bool isAllDay, FrameworkElement anchor) : EventArgs
{
    public DateTimeOffset Start { get; } = start;
    public DateTimeOffset End { get; } = end;
    public bool IsAllDay { get; } = isAllDay;
    public FrameworkElement Anchor { get; } = anchor;
}
