using System.Text.Json;
using System.Text.Json.Serialization;

namespace Calendar.Core.Settings;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>A calendar chosen in settings, by ids (it may disappear when an account is removed).</summary>
public sealed record CalendarRef(string AccountId, string CalendarId);

/// <summary>User preferences (ui.md "Cài đặt"). Accounts are managed in the sidebar, not here.</summary>
public sealed record AppSettings
{
    public const int MinSyncIntervalMinutes = 1;
    public const int MaxSyncIntervalMinutes = 120;

    public static AppSettings Default { get; } = new();

    public DayOfWeek FirstDayOfWeek { get; init; } = DayOfWeek.Monday;

    /// <summary>Working hours: drawn darker in the Day/Week grid, which opens scrolled to <see cref="WorkDayStart"/>.</summary>
    public TimeOnly WorkDayStart { get; init; } = new(8, 0);
    public TimeOnly WorkDayEnd { get; init; } = new(18, 0);

    /// <summary>New events use their calendar's default reminders (Google's own setting).</summary>
    public bool UseCalendarDefaultReminders { get; init; } = true;

    /// <summary>When not using the calendar default: minutes before the start; null = no reminder.</summary>
    public int? DefaultReminderMinutes { get; init; }

    /// <summary>Calendar preselected for new events; null = the last one used, else the primary one.</summary>
    public CalendarRef? DefaultCalendar { get; init; }

    public int SyncIntervalMinutes { get; init; } = 5;

    /// <summary>Applied at startup (changing it needs a restart of the app).</summary>
    public AppTheme Theme { get; init; } = AppTheme.System;

    /// <summary>Fixes values a hand-edited or old file may contain.</summary>
    public AppSettings Normalize()
    {
        var result = this;
        if (SyncIntervalMinutes is < MinSyncIntervalMinutes or > MaxSyncIntervalMinutes)
            result = result with { SyncIntervalMinutes = Math.Clamp(SyncIntervalMinutes, MinSyncIntervalMinutes, MaxSyncIntervalMinutes) };
        if (WorkDayEnd <= WorkDayStart)
            result = result with { WorkDayStart = Default.WorkDayStart, WorkDayEnd = Default.WorkDayEnd };
        if (DefaultReminderMinutes is < 0)
            result = result with { DefaultReminderMinutes = null };
        if (!Enum.IsDefined(FirstDayOfWeek))
            result = result with { FirstDayOfWeek = Default.FirstDayOfWeek };
        if (!Enum.IsDefined(Theme))
            result = result with { Theme = AppTheme.System };
        return result;
    }
}

/// <summary>Settings as a JSON file in the app's data folder. A missing or broken file gives defaults.</summary>
public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Synchronous load, for the app constructor (the theme must be set before any window exists).</summary>
    public AppSettings Load()
    {
        if (!File.Exists(path))
            return AppSettings.Default;
        try
        {
            return (JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? AppSettings.Default).Normalize();
        }
        catch (JsonException)
        {
            return AppSettings.Default;
        }
    }

    public async Task<AppSettings> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(path))
            return AppSettings.Default;
        try
        {
            await using var stream = File.OpenRead(path);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, Json, ct);
            return (settings ?? AppSettings.Default).Normalize();
        }
        catch (JsonException)
        {
            return AppSettings.Default;
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        var temp = path + ".tmp";
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, settings.Normalize(), Json, ct);
        File.Move(temp, path, overwrite: true);
    }
}
