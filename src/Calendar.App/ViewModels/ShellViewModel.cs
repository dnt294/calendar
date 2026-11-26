using System.Collections.ObjectModel;
using System.Globalization;
using Calendar.App.Helpers;
using Calendar.Core.Editing;
using Calendar.Core.Layout;
using Calendar.Core.Models;
using Calendar.Core.Providers;
using Calendar.Core.Storage;
using Calendar.Core.Sync;
using Calendar.Providers.Google;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

namespace Calendar.App.ViewModels;

/// <summary>An account whose token is expired or revoked; shown as an InfoBar with a sign-in button.</summary>
public sealed class ReauthPromptViewModel(Account account, IAsyncRelayCommand signIn)
{
    public Account Account { get; } = account;
    public string Message => $"{Account.Email} cần đăng nhập lại. Lịch của tài khoản này đang hiện từ dữ liệu đã lưu.";
    public IAsyncRelayCommand SignIn { get; } = signIn;
}

/// <summary>State of the main window: visible week, its events, accounts, sign-in and sync status.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SyncOnFocusMinAge = TimeSpan.FromMinutes(1);

    /// <summary>TODO(Phase 9): read from settings.</summary>
    private static readonly DayOfWeek FirstDayOfWeek = DayOfWeek.Monday;

    private readonly ICalendarStore _store;
    private readonly SyncEngine _engine;
    private readonly EventEditor _editor;
    private readonly SyncCoordinator _sync;
    private readonly DispatcherQueue _dispatcher;
    private readonly string? _configError;
    private int _reloadVersion;
    private bool _initialized;
    private int _handledRuns;

    public ShellViewModel(ICalendarStore store, SyncEngine engine, EventEditor editor, SyncCoordinator sync,
        DispatcherQueue dispatcher, string? configError)
    {
        _store = store;
        _engine = engine;
        _editor = editor;
        _sync = sync;
        _dispatcher = dispatcher;
        _configError = configError;
        SelectedDate = DateOnly.FromDateTime(DateTime.Today);
        UpdateVisibleRange();
        _sync.StateChanged += (_, _) => _dispatcher.TryEnqueue(() => _ = OnSyncStateChangedAsync());
    }

    // ---- State ------------------------------------------------------------------------------

    /// <summary>Day, Week or Month.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTimeGridView), nameof(IsMonthView))]
    public partial CalendarViewMode ViewMode { get; set; } = CalendarViewMode.Week;

    public bool IsTimeGridView => ViewMode != CalendarViewMode.Month;
    public bool IsMonthView => ViewMode == CalendarViewMode.Month;

    /// <summary>The date the user is looking at; the visible range is derived from it and <see cref="ViewMode"/>.</summary>
    [ObservableProperty]
    public partial DateOnly SelectedDate { get; set; }

    /// <summary>First visible day (time grid start / month grid top-left).</summary>
    [ObservableProperty]
    public partial DateOnly StartDate { get; set; }

    /// <summary>Number of visible days in the time grid: 1 or 7 (the Month view scrolls on its own).</summary>
    [ObservableProperty]
    public partial int Days { get; set; } = 7;

    [ObservableProperty]
    public partial string PreviousLabel { get; set; } = "Tuần trước";

    [ObservableProperty]
    public partial string NextLabel { get; set; } = "Tuần sau";

    [ObservableProperty]
    public partial string RangeLabel { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<CalendarEvent> Events { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<CalendarInfo> Calendars { get; set; } = [];

    /// <summary>An account operation (sign-in, removal) is running.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsBusy { get; set; }

    public bool IsIdle => !IsBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotSyncing))]
    public partial bool IsSyncing { get; set; }

    public bool IsNotSyncing => !IsSyncing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnline), nameof(CanWrite), nameof(WriteDisabledReason))]
    public partial bool IsOffline { get; set; }

    /// <summary>Calendars new events can go to (writable ones of every account).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanWrite), nameof(WriteDisabledReason))]
    public partial IReadOnlyList<CalendarChoice> WritableCalendars { get; set; } = [];

    /// <summary>Edits are online-only (spec): disabled while offline or without a writable calendar.</summary>
    public bool CanWrite => !IsOffline && WritableCalendars.Count > 0;

    public string WriteDisabledReason => IsOffline ? "Cần kết nối mạng" : WritableCalendars.Count == 0 ? "Chưa có lịch nào ghi được" : "";

    private IReadOnlyList<CalendarChoice> _allCalendars = [];
    private CalendarChoice? _lastUsedCalendar;

    /// <summary>Calendar preselected for a new event: the last one used, else the first primary calendar.</summary>
    public CalendarChoice? DefaultCalendar =>
        WritableCalendars.FirstOrDefault(c => _lastUsedCalendar is { } last
                                              && c.Calendar.AccountId == last.Calendar.AccountId
                                              && c.Calendar.Id == last.Calendar.Id)
        ?? WritableCalendars.FirstOrDefault(c => c.Calendar.IsPrimary)
        ?? WritableCalendars.FirstOrDefault();

    /// <summary>Calendar (and account) an event belongs to, including read-only calendars.</summary>
    public CalendarChoice? CalendarOf(CalendarEvent ev) =>
        _allCalendars.FirstOrDefault(c => c.Calendar.AccountId == ev.AccountId && c.Calendar.Id == ev.CalendarId);

    public bool IsOnline => !IsOffline;

    [ObservableProperty]
    public partial string SyncStatus { get; set; } = "Chưa đồng bộ";

    [ObservableProperty]
    public partial bool IsMessageOpen { get; set; }

    [ObservableProperty]
    public partial string Message { get; set; } = "";

    [ObservableProperty]
    public partial InfoBarSeverity MessageSeverity { get; set; }

    /// <summary>Sidebar tree: account → calendars.</summary>
    public ObservableCollection<AccountGroupViewModel> Accounts { get; } = [];

    [ObservableProperty]
    public partial bool HasNoAccounts { get; set; } = true;

    /// <summary>Accounts that must sign in again (task 4.4).</summary>
    public ObservableCollection<ReauthPromptViewModel> ReauthPrompts { get; } = [];

    // ---- Lifecycle --------------------------------------------------------------------------

    /// <summary>Offline-first: show the cache immediately, then sync in the background.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            await _store.InitializeAsync();
            _initialized = true;
            await RefreshSidebarAsync();
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            ShowMessage(InfoBarSeverity.Error, $"Không mở được dữ liệu: {ex.Message}");
            return;
        }

        if (HasNoAccounts && _configError is null)
            ShowMessage(InfoBarSeverity.Informational, "Bấm \"Thêm tài khoản\" để đăng nhập Google.");

        _sync.Start(SyncInterval);
        if (!HasNoAccounts)
            _ = _sync.SyncNowAsync();
    }

    /// <summary>Window got focus: sync if the last one is more than a minute old.</summary>
    public void OnWindowActivated()
    {
        if (_initialized && !HasNoAccounts)
            _ = _sync.SyncIfStaleAsync(SyncOnFocusMinAge);
    }

    // ---- Navigation -------------------------------------------------------------------------

    [RelayCommand]
    private void GoToToday() => SelectedDate = DateOnly.FromDateTime(DateTime.Today);

    [RelayCommand]
    private void Previous() => SelectedDate = CalendarNavigation.Step(ViewMode, SelectedDate, -1);

    [RelayCommand]
    private void Next() => SelectedDate = CalendarNavigation.Step(ViewMode, SelectedDate, 1);

    /// <summary>Month grid was scrolled to another month: follow it without scrolling back.</summary>
    public void OnMonthScrolled(DateOnly month)
    {
        if (SelectedDate.Year == month.Year && SelectedDate.Month == month.Month)
            return;
        var today = DateOnly.FromDateTime(DateTime.Today);
        SelectedDate = today.Year == month.Year && today.Month == month.Month ? today : month;
    }

    /// <summary>Month grid: a day number was clicked.</summary>
    public void OpenDay(DateOnly date)
    {
        SelectedDate = date;
        ViewMode = CalendarViewMode.Day;
    }

    partial void OnSelectedDateChanged(DateOnly value) => UpdateVisibleRange();

    partial void OnViewModeChanged(CalendarViewMode value) => UpdateVisibleRange();

    private void UpdateVisibleRange()
    {
        var (first, days) = CalendarNavigation.VisibleDays(ViewMode, SelectedDate, FirstDayOfWeek);
        StartDate = first;
        Days = days;
        RangeLabel = CalendarNavigation.Label(ViewMode, SelectedDate, FirstDayOfWeek, CultureInfo.CurrentCulture);
        (PreviousLabel, NextLabel) = ViewMode switch
        {
            CalendarViewMode.Day => ("Ngày trước", "Ngày sau"),
            CalendarViewMode.Month => ("Tháng trước", "Tháng sau"),
            _ => ("Tuần trước", "Tuần sau"),
        };
        _ = ReloadSafeAsync();
    }

    // ---- Sync -------------------------------------------------------------------------------

    [RelayCommand]
    private Task SyncNowAsync() => _sync.SyncNowAsync();

    private async Task OnSyncStateChangedAsync()
    {
        IsSyncing = _sync.IsSyncing;
        IsOffline = _sync.IsOffline;
        SyncStatus = FormatSyncStatus();

        // Compare run counts instead of watching IsSyncing flip: for a fast run, both the "started"
        // and "finished" notifications are handled after it has already ended.
        var completed = _sync.CompletedRuns;
        if (completed == _handledRuns)
            return;
        _handledRuns = completed;

        await RefreshSidebarAsync();
        await ReloadSafeAsync();
        ApplySyncResults(_sync.LastResults, _sync.LastError);
    }

    private string FormatSyncStatus()
    {
        if (_sync.IsSyncing)
            return "Đang đồng bộ…";
        var last = _sync.LastSyncedAt is { } t ? $"cập nhật lúc {t.ToLocalTime():t}" : "chưa đồng bộ";
        return _sync.IsOffline ? $"Offline · {last}" : $"Online · {last}";
    }

    private void ApplySyncResults(IReadOnlyList<AccountSyncResult> results, Exception? runError)
    {
        // Re-auth prompts reflect the latest run: add newly failing accounts, drop recovered ones.
        var needReauth = results.Where(r => r.Error is ReauthenticationRequiredException).Select(r => r.Account).ToList();
        foreach (var stale in ReauthPrompts.Where(p => needReauth.All(a => a.Id != p.Account.Id)).ToList())
            ReauthPrompts.Remove(stale);
        foreach (var account in needReauth.Where(a => ReauthPrompts.All(p => p.Account.Id != a.Id)))
            ReauthPrompts.Add(new ReauthPromptViewModel(account,
                new AsyncRelayCommand(() => SignInAgainAsync(account))));

        if (runError is not null)
        {
            ShowMessage(InfoBarSeverity.Error, $"Đồng bộ lỗi: {runError.Message}");
            return;
        }

        // Network failures are shown by the Offline status, auth failures by the prompts above.
        var other = results
            .Where(r => r.Error is not null && r.Error is not ReauthenticationRequiredException && !SyncErrors.IsNetworkError(r.Error))
            .ToList();
        if (other.Count > 0)
            ShowMessage(InfoBarSeverity.Error, string.Join("\n",
                other.Select(r => $"{r.Account.Email}: không đồng bộ được ({r.Error!.Message}). Đang hiện dữ liệu đã lưu.")));
    }

    // ---- Accounts ---------------------------------------------------------------------------

    [RelayCommand(IncludeCancelCommand = true)]
    private Task AddAccountAsync(CancellationToken ct) => SignInCoreAsync(loginHint: null, ct);

    private async Task SignInAgainAsync(Account account)
    {
        await SignInCoreAsync(account.Email, CancellationToken.None);
        await _sync.SyncNowAsync(); // refreshes the prompts
    }

    private async Task SignInCoreAsync(string? loginHint, CancellationToken ct)
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
            var account = await _sync.RunExclusiveAsync(token => _engine.AddAccountAsync(GoogleCalendarProvider.Id,
                SyncEngine.DefaultWindow(DateTimeOffset.Now), token, loginHint), timeout.Token);
            await RefreshSidebarAsync();
            await ReloadAsync();

            if (loginHint is not null && !string.Equals(account.Email, loginHint, StringComparison.OrdinalIgnoreCase))
                ShowMessage(InfoBarSeverity.Warning,
                    $"Bạn đã đăng nhập {account.Email} thay vì {loginHint}; {account.Email} được thêm như tài khoản mới.");
            else
                ShowMessage(InfoBarSeverity.Success, loginHint is null ? $"Đã thêm {account.Email}." : $"Đã đăng nhập lại {account.Email}.");
        }
        catch (OperationCanceledException)
        {
            ShowMessage(InfoBarSeverity.Warning, ct.IsCancellationRequested
                ? "Đã huỷ đăng nhập."
                : "Hết thời gian chờ đăng nhập. Thử lại nhé.");
        }
        catch (Exception ex)
        {
            ShowMessage(InfoBarSeverity.Error, $"Không đăng nhập được: {ex.Message}");
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
            var signOutError = await _sync.RunExclusiveAsync(
                ct => _engine.RemoveAccountAsync(group.Account, ct), CancellationToken.None);
            foreach (var prompt in ReauthPrompts.Where(p => p.Account.Id == group.Account.Id).ToList())
                ReauthPrompts.Remove(prompt);
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

    // ---- Data -------------------------------------------------------------------------------

    private async Task ReloadAsync()
    {
        if (!_initialized)
            return;

        // Navigation can trigger several reloads at once; only the latest one may update the view.
        var version = ++_reloadVersion;
        var zone = TimeZoneInfo.Local;
        var (first, days) = CalendarNavigation.LoadRange(ViewMode, SelectedDate, FirstDayOfWeek);
        var range = DateRange.ForDays(first, days, zone);

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

        var emails = accounts.ToDictionary(a => a.Id, a => a.Email);
        _allCalendars = calendars
            .Where(c => emails.ContainsKey(c.AccountId))
            .Select(c => new CalendarChoice(c, emails[c.AccountId]))
            .ToList();
        WritableCalendars = _allCalendars.Where(c => c.Calendar.CanEdit).ToList();
    }

    // ---- Create / edit / delete (Phase 6) ---------------------------------------------------

    /// <summary>Tells the user why a write action is not available (task 6.5).</summary>
    public void ExplainWriteDisabled() =>
        ShowMessage(InfoBarSeverity.Warning, IsOffline
            ? "Đang offline: tạo/sửa/xoá cần kết nối mạng."
            : "Chưa có lịch nào ghi được. Thêm tài khoản trước.");

    public Task<bool> CreateEventAsync(CalendarChoice calendar, EventDraft draft)
    {
        _lastUsedCalendar = calendar;
        return RunWriteAsync(ct => _editor.CreateAsync(calendar.Calendar, draft, ct), $"Đã tạo \"{draft.Title}\".");
    }

    public Task<bool> UpdateEventAsync(CalendarEvent ev, EventDraft draft) =>
        RunWriteAsync(ct => _editor.UpdateAsync(ev, draft, ct), $"Đã lưu \"{draft.Title}\".");

    public Task<bool> DeleteEventAsync(CalendarEvent ev) =>
        RunWriteAsync(async ct => { await _editor.DeleteAsync(ev, ct); return true; }, $"Đã xoá \"{ev.Title}\".");

    /// <summary>
    /// Online-only write, never overlapping a sync. On success the view reloads from the cache the
    /// editor just updated; on failure nothing changes locally and the reason is shown.
    /// </summary>
    private async Task<bool> RunWriteAsync<T>(Func<CancellationToken, Task<T>> write, string successMessage)
    {
        if (IsOffline)
        {
            ShowMessage(InfoBarSeverity.Warning, "Đang offline: tạo/sửa/xoá cần kết nối mạng.");
            return false;
        }
        try
        {
            await _sync.RunExclusiveAsync(write, CancellationToken.None);
            await ReloadAsync();
            ShowMessage(InfoBarSeverity.Success, successMessage);
            return true;
        }
        catch (InvalidDraftException ex)
        {
            ShowMessage(InfoBarSeverity.Error, string.Join("\n", ex.Problems.Select(EventText.Problem)));
        }
        catch (ReauthenticationRequiredException)
        {
            ShowMessage(InfoBarSeverity.Warning, "Tài khoản cần đăng nhập lại trước khi sửa lịch.");
            _ = _sync.SyncNowAsync(); // brings up the sign-in prompt
        }
        catch (Exception ex) when (SyncErrors.IsNetworkError(ex))
        {
            ShowMessage(InfoBarSeverity.Warning, "Không kết nối được tới máy chủ. Thay đổi chưa được lưu.");
        }
        catch (Exception ex)
        {
            ShowMessage(InfoBarSeverity.Error, $"Không lưu được thay đổi: {ex.Message}");
        }
        return false;
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
