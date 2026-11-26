using System.Globalization;

namespace Calendar.Providers.Google;

/// <summary>
/// What the Google provider stores as the opaque sync state: when the last full sync and the last
/// sync (full or incremental) started. Incremental syncs ask for events changed since
/// <see cref="LastSync"/> (<c>updatedMin</c>) inside the cache window.
/// </summary>
/// <remarks>
/// Google's <c>syncToken</c> is not used because it cannot be combined with <c>timeMin</c>/<c>timeMax</c>:
/// incremental results would not stay inside the cache window. <c>updatedMin</c> can be combined with
/// the window, and events deleted since that time are always included.
/// Format: <c>v2|&lt;unix seconds of last full sync&gt;|&lt;unix seconds of last sync&gt;</c>.
/// </remarks>
public sealed record GoogleSyncState(DateTimeOffset LastFullSync, DateTimeOffset LastSync)
{
    private const string Prefix = "v2|";

    public string Serialize() =>
        string.Create(CultureInfo.InvariantCulture,
            $"{Prefix}{LastFullSync.ToUnixTimeSeconds()}|{LastSync.ToUnixTimeSeconds()}");

    /// <summary>Null for missing, older-format or corrupt state (caller then does a full sync).</summary>
    public static GoogleSyncState? TryParse(string? value)
    {
        if (value is null || !value.StartsWith(Prefix, StringComparison.Ordinal))
            return null;
        var parts = value[Prefix.Length..].Split('|');
        if (parts.Length != 2
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var full)
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var last)
            || last < full)
            return null;
        return new GoogleSyncState(DateTimeOffset.FromUnixTimeSeconds(full), DateTimeOffset.FromUnixTimeSeconds(last));
    }
}
