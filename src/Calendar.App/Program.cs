using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Calendar.App;

/// <summary>
/// Entry point (replaces the XAML-generated Main). The app lives in the tray, so a second launch
/// (Start menu, startup task) must not start a second copy — that would mean two tray icons and
/// duplicate reminders. It hands its activation to the running instance instead, which shows its window.
/// </summary>
public static class Program
{
    private const string InstanceKey = "main";

    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var instance = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!instance.IsCurrent)
        {
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            // Redirect off the STA thread and wait for it, so this process can exit right after.
            Task.Run(() => instance.RedirectActivationToAsync(activation).AsTask()).Wait();
            return 0;
        }

        Application.Start(_p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }
}
