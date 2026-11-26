# Kế hoạch triển khai

Nguyên tắc: mỗi task là một lát dọc chạy được, có tiêu chí "xong" kiểm tra được. Làm tuần tự theo phase; trong phase có thể đổi thứ tự.

## Phase 0 — Chuẩn bị & spike auth (làm trước tiên, ~1 buổi)

- [x] **0.1 Đăng ký Google OAuth** theo [setup-oauth.md](setup-oauth.md): project, bật Calendar API, consent screen External → **In production**, client loại *Desktop app*.
- [ ] **0.2 Đăng ký Azure app** theo [setup-oauth.md](setup-oauth.md): *personal + work accounts*, public client, redirect cho WAM.
- [ ] **0.3 Spike console** (`spikes/AuthSpike`, xoá sau): login + list 10 event sắp tới cho **cả 3 account** (Gmail, Workspace, Outlook.com).
  - Xong khi: cả 3 in ra event. Nếu Workspace bị chặn → dừng, xử lý với admin trước khi đi tiếp.

## Phase 1 — Khung solution (~1 buổi)

- [x] **1.1** Tạo solution theo [architecture.md](architecture.md): Core, Providers.Google, Providers.Microsoft, App (WinUI 3 Blank App, Packaged), 2 project test. Build xanh, App chạy ra cửa sổ trống.
- [x] **1.2** Core models + `ICalendarProvider` + `SyncResult`, `DateRange`, `EventDraft`.
- [x] **1.3** `SqliteCalendarStore`: schema accounts / calendars / events / sync_state; test round-trip.
- [x] ~~**1.4** CI GitHub Actions (windows-latest): build + test.~~ **Bỏ** (2026-11-26): dùng cá nhân, build/test chạy local trong Visual Studio là đủ.

## Phase 2 — Lát dọc đầu tiên: 1 Google account, xem tuần (~2–3 buổi)

- [x] **2.1** Google provider: sign-in (token DPAPI), `GetCalendarsAsync`, `SyncEventsAsync` full sync (`singleEvents=true`).
- [x] **2.2** `DayLayout`: xếp cột event chồng giờ — **viết test trước** (chồng 2, 3, lồng nhau, liền kề không chồng).
- [x] **2.3** `WeekGrid` control: trục giờ, 7 cột, dải cả ngày, đường giờ hiện tại, cuộn tới 8:00.
- [x] **2.4** Shell: title bar Mica, header (Today, ‹ ›, nhãn ngày), nút thêm tài khoản.
  - Xong khi: login Gmail → thấy đúng lịch tuần này, chuyển tuần được.

## Phase 3 — Đa tài khoản + Microsoft (~2 buổi)

- [ ] **3.1** Microsoft provider: MSAL + WAM, cache DPAPI, list calendars, `calendarView` full sync.
- [x] **3.2** Sidebar: cây account → calendar, checkbox, màu; lưu trạng thái bật/tắt + màu local.
- [x] **3.3** Thêm nhiều account (kể cả 2 Google), xoá account (xoá cache + token). _(Xoá + thêm lại kiểm tra với Gmail thật; 2 Google cùng lúc mới kiểm bằng test vì chỉ có 1 tài khoản.)_
  - Xong khi: 3 account hiện cùng lúc, tắt/bật từng calendar đúng.

## Phase 4 — Offline & sync incremental (~2 buổi)

- [x] **4.1** Google `syncToken`, Graph `delta`; xử lý 410 / token delta hết hạn → full resync. Test với HTTP mock. _(Google: dùng `updatedMin` trong khung cache thay cho `syncToken` vì token không kết hợp được với `timeMin`/`timeMax`; full sync mỗi 7 ngày. Graph `delta` làm cùng 3.1.)_
- [x] **4.2** `SyncEngine`: định kỳ 5', khi focus, nút Sync; không chạy chồng; trạng thái Online/Offline/Last synced.
- [x] **4.3** Khởi động offline-first: render từ SQLite trước khi sync.
- [x] **4.4** Lỗi auth → `InfoBar` đăng nhập lại theo account.
  - Xong khi: rút mạng, mở app vẫn thấy lịch; sửa event trên web → app cập nhật trong ≤5'.

## Phase 5 — View Ngày & Tháng, mini-month (~1–2 buổi)

- [x] **5.1** Day view (tái dùng WeekGrid với 1 cột). **5.2** MonthGrid + "+N nữa". **5.3** Mini-month sidebar, phím tắt điều hướng.

## Phase 6 — Tạo / sửa / xoá (~3 buổi)

- [ ] **6.1** Flyout chi tiết event (đọc). **6.2** Quick create. **6.3** Editor đầy đủ (chọn calendar theo account, khách mời gõ email, nhắc trước).
- [ ] **6.4** Update/Delete cho cả 2 provider; occurrence của event lặp chỉ sửa/xoá lần đó.
- [ ] **6.5** Disable thao tác ghi khi offline.
  - Xong khi: tạo/sửa/xoá trên app → thấy đúng trên web Google Calendar và Outlook web, và ngược lại.

## Phase 7 — RSVP (~1 buổi)

- [ ] **7.1** `RespondAsync` (Google patch `attendees[self].responseStatus`; Graph `/accept` `/tentativelyAccept` `/decline`). **7.2** Hiển thị trạng thái (nét đứt / sọc / gạch) + nút trong flyout.

## Phase 8 — Nhắc lịch & tray (~2 buổi)

- [ ] **8.1** Tray icon (`H.NotifyIcon.WinUI`), đóng cửa sổ = ẩn, menu tray.
- [ ] **8.2** `ReminderScheduler` + AppNotification toast: Join / Snooze / Dismiss; không bắn trùng sau khi sync.
- [ ] **8.3** Khởi động cùng Windows (MSIX `StartupTask`).
  - Xong khi: đóng cửa sổ, event có nhắc 5' → toast hiện đúng giờ, Join mở link họp.

## Phase 9 — Hoàn thiện (~1–2 buổi)

- [ ] Trang Cài đặt, phím tắt đầy đủ, kéo-thả đổi giờ (nice-to-have), dark mode kiểm tra, dọn spike.
- [ ] Dùng thật 1 tuần thay app Calendar của Windows → ghi lại backlog v2.

## Backlog v2 (không làm ở v1)

Tạo/sửa event lặp cả chuỗi · gợi ý danh bạ · tìm kiếm · sửa offline (hàng đợi) · tạo link Meet/Teams · CalDAV/iCloud · M365 công ty.
