# Kiến trúc

## Cấu trúc solution

```
Calendar.sln
src/
  Calendar.Core/                 # net10.0 — KHÔNG phụ thuộc Windows/UI
    Models/                      # Account, CalendarInfo, CalendarEvent, Attendee, Reminder, RsvpStatus
    Providers/ICalendarProvider.cs
    Sync/SyncEngine.cs           # điều phối sync incremental cho mọi account
    Storage/                     # ICalendarStore + SqliteCalendarStore
    Layout/DayLayout.cs          # thuật toán xếp cột event chồng giờ (thuần, dễ test)
  Calendar.Providers.Google/     # net10.0 — map Google API <-> Core models
  Calendar.Providers.Microsoft/  # net10.0 — map Graph <-> Core models
  Calendar.App/                  # net10.0-windows — WinUI 3, MSIX single-project
    Views/ ViewModels/ Controls/ (WeekGrid, MonthGrid) Services/ (Notifications, Tray, Startup)
tests/
  Calendar.Core.Tests/
  Calendar.Providers.Tests/
```

Quy tắc phụ thuộc: `App → Core ← Providers.*`. Core không tham chiếu provider cụ thể; App đăng ký provider qua DI.

## Interface provider (bản nháp)

```csharp
public interface ICalendarProvider
{
    string ProviderId { get; }                       // "google", "microsoft"
    Task<Account> SignInAsync(CancellationToken ct);  // mở trình duyệt / WAM
    Task SignOutAsync(Account account, CancellationToken ct);

    Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(Account a, CancellationToken ct);

    // Incremental: syncState == null => full sync trong cửa sổ thời gian; trả về state mới để lưu.
    Task<SyncResult> SyncEventsAsync(Account a, CalendarInfo c, string? syncState,
                                     DateRange window, CancellationToken ct);

    Task<CalendarEvent> CreateEventAsync(Account a, CalendarInfo c, EventDraft draft, CancellationToken ct);
    Task<CalendarEvent> UpdateEventAsync(Account a, CalendarEvent ev, EventDraft draft, CancellationToken ct);
    Task DeleteEventAsync(Account a, CalendarEvent ev, CancellationToken ct);
    Task RespondAsync(Account a, CalendarEvent ev, RsvpStatus status, CancellationToken ct);
}
```

`SyncResult` = danh sách event upsert + id bị xoá + `syncState` mới + cờ `FullResyncRequired` (Google trả 410 / Graph delta token hết hạn).

## Luồng dữ liệu

1. App mở → đọc SQLite → render ngay (offline-first).
2. `SyncEngine` chạy nền: với mỗi calendar đang bật → `SyncEventsAsync` → ghi SQLite → phát sự kiện `EventsChanged` → ViewModel reload khoảng đang xem.
3. Sửa/tạo/xoá: gọi provider **trực tiếp** (online-only) → thành công thì ghi SQLite → UI cập nhật. Lỗi → hiện thông báo, không đổi cache.
4. `ReminderScheduler` đọc SQLite, đặt hẹn giờ cho các nhắc trong 24h tới, bắn toast.

## Event lặp

- Google: `events.list` với `singleEvents=true` → nhận sẵn từng occurrence. Lưu ý: `syncToken` vẫn dùng được với `singleEvents=true`.
- Graph: `/me/calendars/{id}/calendarView/delta` → trả occurrence trong khoảng thời gian.
- Lưu `SeriesId` + `IsOccurrence` để UI hiện icon lặp và giới hạn sửa ở mức occurrence.

## Bảo mật

- Token Google: tự viết `IDataStore` mã hoá bằng DPAPI (`ProtectedData`, scope CurrentUser) thay vì `FileDataStore` plaintext.
- MSAL: `MsalCacheHelper` (Extensions.Msal) — DPAPI trên Windows.
- OAuth client ID/secret của "Desktop app" không phải bí mật thật, nhưng vẫn để ở `appsettings.local.json` (gitignored), có file `.example` mẫu.
- Scope tối thiểu: Google `calendar` (cần ghi + RSVP); Graph `Calendars.ReadWrite`, `User.Read`, `offline_access`.
