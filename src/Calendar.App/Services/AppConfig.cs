using System.Text.Json;
using Calendar.Providers.Google;

namespace Calendar.App.Services;

/// <summary>Reads <c>appsettings.local.json</c> (gitignored, copied next to the exe).</summary>
internal static class AppConfig
{
    private const string FileName = "appsettings.local.json";

    /// <returns>The Google options, or an error message explaining what is missing.</returns>
    public static (GoogleOAuthOptions? Options, string? Error) LoadGoogle()
    {
        var path = Path.Combine(AppContext.BaseDirectory, FileName);
        if (!File.Exists(path))
            return (null, $"Chưa có {FileName}. Làm theo docs/setup-oauth.md rồi build lại.");

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("Google", out var google))
                return (null, $"{FileName} thiếu mục \"Google\".");

            var id = google.TryGetProperty("ClientId", out var i) ? i.GetString() : null;
            var secret = google.TryGetProperty("ClientSecret", out var s) ? s.GetString() : null;
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret) || id.StartsWith("xxx", StringComparison.Ordinal))
                return (null, $"{FileName}: Google ClientId / ClientSecret chưa được điền.");

            return (new GoogleOAuthOptions(id, secret), null);
        }
        catch (JsonException ex)
        {
            return (null, $"{FileName} không phải JSON hợp lệ: {ex.Message}");
        }
    }
}
