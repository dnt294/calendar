# Spec — Calendar client cho Windows 11

> Trạng thái: **v1 đã chốt** (2026-11-22). Mọi thay đổi scope → cập nhật file này trước khi code.

## 1. Mục tiêu

Một calendar client **native Windows 11** gộp lịch từ nhiều tài khoản Google và Microsoft vào một chỗ, thay thế app Calendar mặc định của Windows.

- **User:** chỉ chủ repo (dùng cá nhân). Dev + build + dùng trên cùng máy Windows bằng Visual Studio.
- **Success:** bỏ hẳn app Calendar của Windows, dùng app này hằng ngày.
- **Chi phí:** 0đ. Không phát hành Store, không verify Google, không chứng chỉ code-signing trả phí.

## 2. Tài khoản hỗ trợ

| Loại | Provider | API | Auth |
|---|---|---|---|
| Gmail cá nhân | Google | Google Calendar API v3 | OAuth 2.0 Desktop client (loopback + PKCE) |
| Google Workspace | Google | Google Calendar API v3 | như trên — **có thể bị admin chặn** |
| Outlook.com / Hotmail | Microsoft | Microsoft Graph v1.0 | MSAL.NET + WAM broker, authority `common` |
| Microsoft 365 (sau) | Microsoft | Microsoft Graph v1.0 | như trên — có thể cần admin consent |

- Nhiều tài khoản cùng lúc, kể cả nhiều tài khoản cùng provider.
- Provider là plugin qua interface `ICalendarProvider` → thêm CalDAV/iCloud sau mà không sửa Core/UI.

## 3. Tính năng v1

### Xem
- [ ] View **Ngày / Tuần / Tháng**; nút Today, ← →; mini-month để nhảy ngày.
- [ ] Sidebar: danh sách account → calendar, checkbox bật/tắt, màu theo calendar (lấy màu từ provider, cho phép đổi local).
- [ ] Event cả ngày hiển thị ở dải trên cùng; event chồng giờ chia cột.
- [ ] Event lặp hiển thị đúng (dùng instance đã expand từ API).
- [ ] Trạng thái RSVP thể hiện trực quan: chưa trả lời / tentative / declined.
- [ ] Đường kẻ giờ hiện tại.
- [ ] Hiển thị theo timezone của máy.

### Sửa
- [ ] Tạo event đơn (quick-create + editor đầy đủ): tiêu đề, calendar (chọn account), bắt đầu/kết thúc, cả ngày, địa điểm, mô tả, khách mời (gõ email), nhắc trước.
- [ ] Sửa / xoá event đơn.
- [ ] Event lặp: sửa / xoá **chỉ lần này** (occurrence). Không tạo/sửa cả chuỗi.
- [ ] Kéo-thả để đổi giờ (nice-to-have trong v1, làm cuối).
- [ ] Accept / Tentative / Decline invite.

### Nền
- [ ] **Cache offline** (SQLite): mất mạng vẫn xem được; app mở lên hiện cache ngay rồi mới sync.
- [ ] Sync incremental: Google `syncToken`, Graph `calendarView/delta`. Định kỳ (mặc định 5 phút) + khi app focus + nút Sync.
- [ ] **Nhắc lịch** bằng Windows toast (Join meeting link nếu có, Snooze, Dismiss).
- [ ] Chạy nền ở **system tray**, tuỳ chọn khởi động cùng Windows.
- [ ] Token hết hạn / bị thu hồi → banner "Đăng nhập lại" cho đúng account, không crash.

## 4. Ngoài scope v1

- macOS / iOS / web.
- Tạo event lặp, sửa/xoá **cả chuỗi** hoặc "lần này và các lần sau".
- Gợi ý danh bạ khi mời người; xem free/busy của người khác.
- Sửa khi offline (offline = chỉ xem).
- Tạo link Meet/Teams tự động.
- Tìm kiếm event.
- Phát hành Store, nhiều user, đồng bộ setting giữa máy.

## 5. Ràng buộc & rủi ro

| Rủi ro | Ảnh hưởng | Xử lý |
|---|---|---|
| Workspace/M365 admin chặn app chưa verify | Không connect được account công ty | **Spike auth là task đầu tiên.** Nếu chặn: nhờ admin allowlist OAuth client ID. |
| Google project để "Testing" → refresh token hết hạn sau 7 ngày | Phải login lại mỗi tuần | Chuyển consent screen sang **In production** (không verify): chịu màn hình cảnh báo + giới hạn 100 user. |
| WinUI 3 không có sẵn control week/day scheduler | Tự làm grid — phần UI tốn công nhất | Custom control, có test cho thuật toán xếp cột event chồng giờ. (Phương án B: Syncfusion SfScheduler, community license free cho cá nhân.) |
| WinUI 3 không có tray icon built-in | — | Thư viện `H.NotifyIcon.WinUI`. |
| Token lưu dạng plaintext | Lộ quyền đọc/ghi lịch | MSAL cache + Google token mã hoá bằng DPAPI (per-user). |
| Provider khác nhau về model (recurrence, RSVP, màu) | Logic rẽ nhánh lan ra UI | Chuẩn hoá về domain model trong Core; UI chỉ biết Core. |

## 6. Stack

- **.NET 10 (LTS)**, C#.
- **WinUI 3 / Windows App SDK** (bản stable mới nhất), MSIX single-project, cài bằng VS Deploy.
- MVVM: `CommunityToolkit.Mvvm`.
- Storage: SQLite qua `Microsoft.Data.Sqlite`.
- Google: `Google.Apis.Calendar.v3`, `Google.Apis.Auth`.
- Microsoft: `Microsoft.Graph`, `Microsoft.Identity.Client` + `.Broker` (WAM) + `.Extensions.Msal` (token cache).
- Notification: `Microsoft.Windows.AppNotifications` (Windows App SDK). Tray: `H.NotifyIcon.WinUI`.
- Test: xUnit cho Core và Providers (mock HTTP).

Chi tiết kiến trúc: [architecture.md](architecture.md). Giao diện: [ui.md](ui.md). Kế hoạch: [plan.md](plan.md).
