using Calendar.Core.Models;

namespace Calendar.App.ViewModels;

/// <summary>A calendar as shown in pickers: name plus the account email, so two accounts are never confused.</summary>
public sealed record CalendarChoice(CalendarInfo Calendar, string AccountEmail)
{
    public string Display => AccountEmail.Length == 0 ? Calendar.Name : $"{Calendar.Name} · {AccountEmail}";

    public override string ToString() => Display;
}
