using Windows.ApplicationModel;

namespace Calendar.App.Services;

/// <summary>Start with Windows (task 8.3) through the MSIX startup task declared in Package.appxmanifest.</summary>
internal static class StartupService
{
    private const string TaskId = "CalendarStartup";

    public static async Task<bool> IsEnabledAsync()
    {
        var task = await StartupTask.GetAsync(TaskId);
        return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
    }

    /// <returns>The resulting state, and a message when Windows did not allow the change.</returns>
    public static async Task<(bool Enabled, string? Problem)> SetEnabledAsync(bool enabled)
    {
        var task = await StartupTask.GetAsync(TaskId);
        if (!enabled)
        {
            task.Disable();
            return (false, null);
        }

        var state = await task.RequestEnableAsync();
        return state switch
        {
            StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy => (true, null),
            StartupTaskState.DisabledByUser =>
                (false, "Windows đang chặn: bật lại Calendar trong Settings › Apps › Startup."),
            StartupTaskState.DisabledByPolicy => (false, "Chính sách của máy không cho khởi động cùng Windows."),
            _ => (false, null),
        };
    }
}
