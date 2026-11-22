namespace Calendar.Core.Models;

public enum RsvpStatus
{
    /// <summary>Not an invite (own event, or the user is not an attendee).</summary>
    None = 0,
    NeedsAction = 1,
    Accepted = 2,
    Tentative = 3,
    Declined = 4,
}
