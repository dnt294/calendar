namespace Calendar.Core.Models;

/// <param name="IsSelf">The attendee is the account owner.</param>
public sealed record Attendee(
    string Email,
    string? DisplayName,
    RsvpStatus Status,
    bool IsSelf = false,
    bool IsOrganizer = false,
    bool IsOptional = false);
