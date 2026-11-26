using Calendar.App.Services;
using Calendar.App.ViewModels;
using Calendar.Core.Editing;
using Calendar.Core.Providers;
using Calendar.Core.Reminders;
using Calendar.Core.Storage;
using Calendar.Core.Sync;
using Calendar.Providers.Google;
using Calendar.Providers.Google.Storage;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Windows.Storage;

namespace Calendar.App;

public partial class App : Application
{
    private MainWindow? _window;
    private ReminderScheduler? _reminders;
    private DispatcherQueue? _dispatcher;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        // Per-user package data folder: %LOCALAPPDATA%\Packages\<family>\LocalState
        var dataDir = ApplicationData.Current.LocalFolder.Path;
        var store = new SqliteCalendarStore(Path.Combine(dataDir, "calendar.db"));

        var providers = new List<ICalendarProvider>();
        var (google, configError) = AppConfig.LoadGoogle();
        if (google is not null)
        {
            var tokens = new ProtectedFileDataStore(Path.Combine(dataDir, "tokens", "google"), new DpapiDataProtector());
            providers.Add(new GoogleCalendarProvider(google, tokens));
        }

        var engine = new SyncEngine(store, providers);
        var coordinator = new SyncCoordinator(
            ct => engine.SyncAllAsync(SyncEngine.DefaultWindow(DateTimeOffset.Now), ct), TimeProvider.System);
        var editor = new EventEditor(store, providers);
        _reminders = new ReminderScheduler(
            (range, ct) => store.GetEventsAsync(range, TimeZoneInfo.Local, enabledCalendarsOnly: true, ct),
            TimeProvider.System, TimeZoneInfo.Local);

        var viewModel = new ShellViewModel(store, engine, editor, coordinator, _reminders, _dispatcher, configError);
        _window = new MainWindow(viewModel);

        // Reminders → toasts (task 8.2). Handlers are attached before Register, as the SDK requires.
        _reminders.ReminderDue += (_, reminder) => _dispatcher.TryEnqueue(() =>
            ReminderNotifier.Show(reminder, viewModel.CalendarOf(reminder.Event)?.Display));
        AppNotificationManager.Default.NotificationInvoked += (sender, e) =>
        {
            var action = ReminderNotifier.Parse(e);
            _dispatcher.TryEnqueue(() => _ = HandleReminderActionAsync(action));
        };
        ReminderNotifier.Register();

        // A second launch (Start menu, taskbar) is redirected here by Program.Main: show the window.
        AppInstance.GetCurrent().Activated += (_, _) => _dispatcher.TryEnqueue(() => _window.ShowFromTray());

        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        // Started by Windows at sign-in (task 8.3): stay in the tray, do not pop a window.
        if (activation.Kind != ExtendedActivationKind.StartupTask)
            _window.Activate();
        if (activation.Kind == ExtendedActivationKind.AppNotification && activation.Data is AppNotificationActivatedEventArgs toast)
            _ = HandleReminderActionAsync(ReminderNotifier.Parse(toast));

        await viewModel.InitializeAsync();
    }

    private async Task HandleReminderActionAsync(ReminderAction action)
    {
        switch (action.Action)
        {
            case "join" when Uri.TryCreate(action.Url, UriKind.Absolute, out var url):
                await Windows.System.Launcher.LaunchUriAsync(url);
                break;
            case "snooze" when action.Key is not null:
                // After an app restart the reminder is unknown: nothing to snooze, the toast just closes.
                _reminders?.Snooze(action.Key, action.SnoozeFor ?? TimeSpan.FromMinutes(5));
                break;
            case "dismiss":
                break;
            default:
                _window?.ShowFromTray();
                break;
        }
    }

    /// <summary>Called by the tray menu "Thoát": the only real exit (closing the window hides it).</summary>
    public void ExitApp()
    {
        _reminders?.Dispose();
        ReminderNotifier.Unregister();
        Exit();
    }
}
