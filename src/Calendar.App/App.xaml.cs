using Calendar.App.Services;
using Calendar.App.ViewModels;
using Calendar.Core.Providers;
using Calendar.Core.Storage;
using Calendar.Core.Sync;
using Calendar.Providers.Google;
using Calendar.Providers.Google.Storage;
using Microsoft.UI.Xaml;
using Windows.Storage;

namespace Calendar.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
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

        var viewModel = new ShellViewModel(store, new SyncEngine(store, providers), configError);
        _window = new MainWindow(viewModel);
        _window.Activate();

        await viewModel.InitializeAsync();
    }
}
