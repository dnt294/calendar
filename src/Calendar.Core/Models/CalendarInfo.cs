namespace Calendar.Core.Models;

/// <summary>A calendar inside an account.</summary>
/// <param name="Id">Provider calendar id (unique within the account).</param>
/// <param name="Color">Provider color as <c>#RRGGBB</c>.</param>
/// <param name="CanEdit">False for read-only calendars (holidays, shared read-only).</param>
public sealed record CalendarInfo(
    string AccountId,
    string Id,
    string Name,
    string Color,
    bool IsPrimary,
    bool CanEdit)
{
    /// <summary>Local preference: shown in the views. Not sent to the provider.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Local preference: color chosen by the user, overrides <see cref="Color"/>.</summary>
    public string? ColorOverride { get; init; }

    public string EffectiveColor => ColorOverride ?? Color;
}
