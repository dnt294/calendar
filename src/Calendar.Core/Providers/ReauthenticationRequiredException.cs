namespace Calendar.Core.Providers;

/// <summary>The stored credentials of an account are missing, expired or revoked; the user must sign in again.</summary>
public sealed class ReauthenticationRequiredException(string accountId, string? message = null, Exception? inner = null)
    : Exception(message ?? $"Account '{accountId}' needs to sign in again.", inner)
{
    public string AccountId { get; } = accountId;
}
