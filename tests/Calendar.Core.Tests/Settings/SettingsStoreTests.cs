using Calendar.Core.Settings;

namespace Calendar.Core.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"calendar-settings-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        File.Delete(_path);
        File.Delete(_path + ".tmp");
    }

    [Fact]
    public async Task Missing_file_gives_defaults()
    {
        var settings = await new SettingsStore(_path).LoadAsync();
        Assert.Equal(AppSettings.Default, settings);
        Assert.Equal(DayOfWeek.Monday, settings.FirstDayOfWeek);
        Assert.Equal(5, settings.SyncIntervalMinutes);
        Assert.Equal(AppTheme.System, settings.Theme);
    }

    [Fact]
    public async Task Round_trip()
    {
        var store = new SettingsStore(_path);
        var custom = AppSettings.Default with
        {
            FirstDayOfWeek = DayOfWeek.Sunday,
            WorkDayStart = new TimeOnly(8, 30),
            WorkDayEnd = new TimeOnly(17, 30),
            DefaultReminderMinutes = 10,
            UseCalendarDefaultReminders = false,
            DefaultCalendar = new CalendarRef("google:me", "primary"),
            SyncIntervalMinutes = 15,
            Theme = AppTheme.Dark,
        };

        await store.SaveAsync(custom);

        Assert.Equal(custom, await new SettingsStore(_path).LoadAsync());
        Assert.Equal(custom, new SettingsStore(_path).Load());
    }

    [Fact]
    public async Task Corrupt_file_gives_defaults()
    {
        await File.WriteAllTextAsync(_path, "{ not json");
        Assert.Equal(AppSettings.Default, await new SettingsStore(_path).LoadAsync());
    }

    [Fact]
    public async Task Missing_fields_keep_their_defaults()
    {
        await File.WriteAllTextAsync(_path, """{ "theme": "Light" }""");
        var settings = await new SettingsStore(_path).LoadAsync();
        Assert.Equal(AppTheme.Light, settings.Theme);
        Assert.Equal(AppSettings.Default.SyncIntervalMinutes, settings.SyncIntervalMinutes);
        Assert.Equal(AppSettings.Default.WorkDayStart, settings.WorkDayStart);
    }

    [Fact]
    public async Task Out_of_range_values_are_normalized()
    {
        await File.WriteAllTextAsync(_path, """{ "syncIntervalMinutes": 0, "workDayStart": "18:00:00", "workDayEnd": "09:00:00", "defaultReminderMinutes": -5 }""");
        var settings = await new SettingsStore(_path).LoadAsync();
        Assert.Equal(AppSettings.MinSyncIntervalMinutes, settings.SyncIntervalMinutes);
        Assert.Equal(AppSettings.Default.WorkDayStart, settings.WorkDayStart); // inverted hours → defaults
        Assert.Equal(AppSettings.Default.WorkDayEnd, settings.WorkDayEnd);
        Assert.Null(settings.DefaultReminderMinutes);
    }

    [Fact]
    public void Normalize_keeps_valid_values()
    {
        var valid = AppSettings.Default with { SyncIntervalMinutes = 30, WorkDayStart = new TimeOnly(7, 0), WorkDayEnd = new TimeOnly(19, 0) };
        Assert.Equal(valid, valid.Normalize());
    }
}
