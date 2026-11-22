# Đề xuất giao diện

Phong cách: **Fluent / Windows 11** — nền Mica, custom title bar, bo góc, theo theme sáng/tối của hệ thống. Tham chiếu cảm giác: Outlook mới + Notion Calendar, nhưng gọn hơn.

## Màn hình chính

```
┌──────────────────────────────────────────────────────────────────────────────────┐
│ ▢ Calendar   [Today] ‹ ›  Tháng 11, 2026            [Ngày|Tuần|Tháng]  ⟳  [+ Mới] │  ← title bar (Mica)
├───────────────────┬──────────────────────────────────────────────────────────────┤
│  Tháng 11 2026    │        T2 23   T3 24   T4 25   T5 26   T6 27   T7 28   CN 29  │
│ T2 T3 T4 T5 T6 .. │ cả ngày ░Nghỉ lễ░                                            │
│  ·  ·  ·  ·  ·    ├──────────────────────────────────────────────────────────────┤
│  (mini month)     │  9:00 ┌Standup┐        ┌Standup┐        ┌Standup┐           │
│                   │       └───────┘        └───────┘        └───────┘           │
│ ▾ dat@gmail.com   │ 10:00        ┌1:1 ─┐┌Review┐                                │
│   ☑ ● Cá nhân     │ ━━━━━━━━━━━━━━━━━━━●━━━━━━━━━━━━━━━━━━━━━━━━━━ (giờ hiện tại)  │
│   ☑ ● Sinh nhật   │ 11:00        └─────┘└──────┘  ┊Invite?┊  ← viền nét đứt =     │
│ ▾ dat@kuzen.io    │                                ┊      ┊    chưa trả lời       │
│   ☑ ● Công việc   │ 12:00                                                        │
│   ☐ ● Team        │                                                              │
│ ▾ me@outlook.com  │ 13:00  ▒Tentative▒  ← sọc chéo = tentative                   │
│   ☑ ● Calendar    │        ~~Declined~~ ← gạch ngang, mờ                          │
│                   │                                                              │
│ [+ Thêm tài khoản]│                                                              │
│ ⚙ Cài đặt   ● Online / ⚠ Offline – cập nhật 10:42                               │
└───────────────────┴──────────────────────────────────────────────────────────────┘
```

- **Sidebar** (thu gọn được, `NavigationView`/`SplitView`): mini-month (`CalendarView`), cây account → calendar với checkbox + chấm màu (bấm chấm màu để đổi màu local), nút thêm tài khoản, trạng thái sync ở đáy.
- **Header**: Today, ‹ ›, nhãn khoảng ngày, `SelectorBar` Ngày/Tuần/Tháng, nút Sync (xoay khi đang sync), nút **+ Mới**.
- **Week/Day grid**: cuộn dọc, mặc định cuộn tới giờ làm việc (8:00); khung giờ làm việc nền đậm hơn; cuối tuần nhạt hơn.
- **Month grid**: mỗi ô hiển thị tối đa 3–4 event, dư thì "+N nữa" mở flyout.
- Màu event = màu calendar; event của account khác nhau phân biệt bằng màu, không bằng layout.

## Tương tác

| Hành động | Kết quả |
|---|---|
| Click event | **Flyout chi tiết**: tiêu đề, giờ, calendar/account, địa điểm, link họp (nút **Join**), khách mời + trạng thái, mô tả. Nút RSVP (Có / Có thể / Không) nếu là invite. Nút Sửa, Xoá. |
| Click/kéo trên ô trống | **Quick create** flyout: tiêu đề, giờ (đã điền), calendar (mặc định = calendar mặc định), [Thêm chi tiết…] |
| Double-click event / "Thêm chi tiết" | **Editor** (panel trượt từ phải hoặc `ContentDialog`): đủ các trường trong spec. Chọn calendar hiển thị cả email account để khỏi nhầm. |
| Xoá event lặp | Hộp xác nhận nói rõ "Chỉ xoá lần này (thứ Ba 24/11)". |
| Kéo-thả event | Đổi giờ (nice-to-have). Event lặp: chỉ đổi lần này. |
| Phím tắt | `T` hôm nay, `D/W/M` đổi view, `←/→` lùi/tiến, `N` tạo mới, `Ctrl+R` sync, `Delete` xoá event đang chọn. |

## Tray & thông báo

- **Tray icon**: click trái mở cửa sổ; menu phải: *Sự kiện tiếp theo: Standup 9:00*, Mở, Sync ngay, Thoát. Đóng cửa sổ (X) = ẩn xuống tray.
- **Toast nhắc lịch**:
  ```
  ┌───────────────────────────────────────┐
  │ 📅 Standup — 9:00–9:15 (sau 5 phút)   │
  │ dat@kuzen.io · Google Meet            │
  │ [Join]  [Snooze 5'▾]  [Dismiss]       │
  └───────────────────────────────────────┘
  ```

## Cài đặt

Tài khoản (thêm / xoá / đăng nhập lại), calendar mặc định khi tạo mới, ngày đầu tuần, giờ làm việc, nhắc mặc định, khoảng sync, khởi động cùng Windows, theme (System/Light/Dark).

## Trạng thái lỗi

- Offline: badge ⚠ ở sidebar, nút Mới/Sửa/Xoá/RSVP bị disable kèm tooltip "Cần kết nối mạng".
- Token hết hạn: `InfoBar` ở đầu màn hình "dat@kuzen.io cần đăng nhập lại [Đăng nhập]", lịch của account đó vẫn hiện từ cache.
