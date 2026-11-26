using System.Collections.ObjectModel;
using System.Globalization;
using Calendar.Core.Layout;
using Calendar.Core.Models;
using Calendar.Core.Providers;
using Calendar.Core.Storage;
using Calendar.Core.Sync;
using Calendar.Providers.Google;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;

namespace Calendar.App.ViewModels;

/// <summary>State of the main window: visible week, its events, sign-in and sync status.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private const int DaysInView = 7;
    private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(5);

    /// <summary>TODO(Phase 9): read from settings.</summary>
    private static readonly DayOfWeek FirstDayOfWeek = DayOfWeek.Monday;

    private readonly ICalendarStore _store;
    private readonly SyncEngine _sync;
    private readonly string? _configError;
    private int _reloadVersion;
    private bool _initialized;

    public ShellViewModel(ICalendarStore store, SyncEngine sync, string? configError)
    {
        _store = store;
        _sync = sync;
        _configError = configError;
        StartDate = WeekNavigation.StartOfWeek(DateOnly.FromDateTime(DateTime.Today), FirstDayOfWeek);
    }

    // ---- State ------------------------------------------------------------------------------

    [ObservableProperty]
    public partial DateOnly StartDate { get; set; }

    [ObservableProperty]
    public partial string RangeLabel { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<CalendarEvent> Events { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<CalendarInfo> Calendars { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsBusy { get; set; }

    public bool IsIdle => !IsBusy;

    [ObservableProperty]
    public partial bool IsMessageOpen { get; set; }

    [ObservableProperty]
    public partial string Message { get; set; } = "";

    [ObservableProperty]
    public partial InfoBarSeverity MessageSeverity { get; set; }

    public int Days => DaysInView;

    /// <summary>Sidebar tree: account → calendars.</summary>
    public ObservableCollection<AccountGroupViewModel> Accounts { get; } = [];

    [ObservableProperty]
    public partial bool HasNoAccounts { get; set; } = true;

    // ---- Lifecycle --------------------------------------------------------------------------

    /// <summary>Offline-first: show the cache, then sync in the background.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            await _store.InitializeAsync();
            _initialized = true;
            await RefreshSidebarAsync();
            await ReloadAsync();

            if ((await _store.GetAccountsAsync()).Count > 0)
                await SyncAllAsync();
            else if (_configError is null)
                ShowMessage(InfoBarSeverity.Informational, "Bấm \"Thêm tài khoản\" để đăng nhập Google.");
        }
        catch (Exception ex)
        {
            ShowMessage(InfoBarSeverity.Error, $"Không mở được dữ liệu: {ex.Message}");
        }
    }

    // ---- Navigation -------------------------------------------------------------------------

    [RelayCommand]
    private void GoToToday() => StartDate = WeekNavigation.StartOfWeek(DateOnly.FromDateTime(DateTime.Today), FirstDayOfWeek);

    [RelayCommand]
    private void Previous() => StartDate = StartDate.AddDays(-DaysInView);

    [RelayCommand]
    private void Next() => StartDate = StartDate.AddDays(DaysInView);

    partial void OnStartDateChanged(DateOnly value)
    {
        RangeLabel = WeekNavigation.FormatRangeLabel(value, DaysInView, CultureInfo.CurrentCulture);
        _ = ReloadSafeAsync();
    }

    // ---- Accounts & sync --------------------------------------------------------------------

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task AddAccountAsync(CancellationToken ct)
    {
        if (_configError is not null)
        {
            ShowMessage(InfoBarSeverity.Error, _configError);
            return;
        }

        IsBusy = true;
        ShowMessage(InfoBarSeverity.Informational, "Đang chờ đăng nhập Google trong trình duyệt…");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(SignInTimeout);
        try
        {
            var account = await _sync.AddAccountAsync(GoogleCalendarProvider.Id,
                SyncEngine.DefaultWindow(DateTimeOffset.Now), timeout.Token);
            await RefreshSidebarAsync();
            await ReloadAsync();
            ShowMessage(InfoBarSeverity.Success, $"Đã thêm {account.Email}.");
        }
        catch (OperationCanceledException)
        {
            ShowMessage(InfoBarSeverity.Warning, ct.IsCancellationRequested
                ? "Đã huỷ đăng nhập."
                : "Hết thời gian chờ đăng nhập. Thử lại nhé.");
        }
        catch (Exception ex)
        {
            ShowMessage(InfoBarSeverity.Error, $"Không thêm được tài khoản: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Called after the user confirmed in the dialog.</summary>
    [RelayCommand]
    private async Task RemoveAccountAsync(AccountGroupViewModel group)
    {
        IsBusy = true;
        try
        {
            var signOutError = await _sync.RemoveAccountAsync(group.Account, CancellationToken.None);
            await RefreshSidebarAsync();
            await ReloadAsync();
            if (signOutError is null)
                ShowMessage(InfoBarSeverity.Success, $"Đã xoá {group.Email} khỏi app.");
            else
                ShowMessage(InfoBarSeverity.Warning,
                    $"Đã xoá {group.Email} khỏi máy, nhưng chưa thu hồi được quyền trên Google ({signOutError.Message}). " +
                    "Bạn có thể tự thu hồi tại myaccount.google.com/permissions.");
        }
        catch (Exception ex)
        {
            ShowMessage(InfoBarSeverity.Error, $"Không xoá được {group.Email}: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SyncAllAsync()
    {
        IsBusy = true;
        try
        {
            var results = await _sync.SyncAllAsync(SyncEngine.DefaultWindow(DateTimeOffset.Now), CancellationToken.None);
            await RefreshSidebarAsync();
            await ReloadAsync();

            var failed = results.Where(r => !r.Succeeded).ToList();
            if (failed.Count > 0)
                ShowMessage(failed.Any(r => r.Error is ReauthenticationRequiredException)
                        ? InfoBarSeverity.Warning
                        : InfoBarSeverity.Error,
                    string.Join("\n", failed.Select(r => r.Error is ReauthenticationRequiredException
                        ? $"{r.Account.Email} cần đăng nhập lại (bấm \"Thêm tài khoản\" và chọn lại tài khoản này)."
                        : $"{r.Account.Email}: không sync được ({r.Error!.Message}). Đang hiện dữ liệu đã lưu.")));
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---- Data -------------------------------------------------------------------------------

    private async Task ReloadAsync()
    {
        if (!_initialized)
            return;

        // Navigation can trigger several reloads at once; only the latest one may update the view.
        var version = ++_reloadVersion;
        var zone = TimeZoneInfo.Local;
        var range = DateRange.ForDays(StartDate, DaysInView, zone);

        var calendars = await _store.GetCalendarsAsync();
        var events = await _store.GetEventsAsync(range, zone);
        if (version != _reloadVersion)
            return;

        Calendars = calendars;
        Events = events;
    }

    /// <summary>Rebuilds the account → calendar tree (after sign-in or sync, not on navigation).</summary>
    private async Task RefreshSidebarAsync()
    {
        var accounts = await _store.GetAccountsAsync();
        var calendars = await _store.GetCalendarsAsync();

        Accounts.Clear();
        foreach (var account in accounts)
        {
            var items = calendars
                .Where(c => c.AccountId == account.Id)
                .Select(c => new CalendarItemViewModel(c, OnCalendarPreferenceChangedAsync))
                .ToList();
            Accounts.Add(new AccountGroupViewModel(account, items));
        }
        HasNoAccounts = Accounts.Count == 0;
    }

    /// <summary>Checkbox or color changed in the sidebar: save locally, then redraw the grid.</summary>
    private async Task OnCalendarPreferenceChangedAsync(CalendarItemViewModel item)
    {
        try
        {
            await _store.SetCalendarPreferencesAsync(item.Info.AccountId, item.Info.Id, item.IsEnabled, item.ColorOverride);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            ShowMessage(InfoBarSeverity.Error, $"Không lưu được tuỳ chọn của \"{item.Name}\": {ex.Message}");
        }
    }

    private async Task ReloadSafeAsync()
    {
        try
        {
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            ShowMessage(InfoBarSeverity.Error, $"Không đọc được lịch: {ex.Message}");
        }
    }

    private void ShowMessage(InfoBarSeverity severity, string message)
    {
        MessageSeverity = severity;
        Message = message;
        IsMessageOpen = true;
    }
}
