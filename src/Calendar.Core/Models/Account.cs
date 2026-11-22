namespace Calendar.Core.Models;

/// <summary>A signed-in account of one provider. Several accounts may share a provider.</summary>
/// <param name="Id">Local, stable key — see <see cref="CreateId"/>.</param>
/// <param name="ProviderId">"google", "microsoft", ...</param>
/// <param name="ProviderAccountId">Provider's own user id (Google <c>sub</c>, MSAL home account id).</param>
public sealed record Account(
    string Id,
    string ProviderId,
    string ProviderAccountId,
    string Email,
    string DisplayName)
{
    public static string CreateId(string providerId, string providerAccountId) =>
        $"{providerId}:{providerAccountId}";
}
