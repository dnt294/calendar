namespace Calendar.Core.Models;

/// <summary>A popup reminder, <paramref name="MinutesBefore"/> minutes before the event starts.</summary>
public sealed record Reminder(int MinutesBefore)
{
    public int MinutesBefore { get; } = MinutesBefore >= 0
        ? MinutesBefore
        : throw new ArgumentOutOfRangeException(nameof(MinutesBefore), "Must be >= 0.");
}
