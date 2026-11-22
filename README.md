# Calendar

Calendar client native Windows 11 (WinUI 3 / .NET 10) gộp nhiều tài khoản Google (Gmail, Workspace) và Microsoft (Outlook.com, M365). Dùng cá nhân.

- [Spec](docs/spec.md) — cái gì, vì sao, ngoài scope
- [Kiến trúc](docs/architecture.md)
- [Giao diện](docs/ui.md)
- [Kế hoạch](docs/plan.md)
- [Thiết lập OAuth](docs/setup-oauth.md)

## Yêu cầu

Windows 11, Visual Studio 2026 (hoặc 2022 17.14+) với workload **WinUI application development** và **.NET desktop development**, .NET 10 SDK.

## Chạy

1. Làm theo [setup-oauth.md](docs/setup-oauth.md), tạo `src/Calendar.App/appsettings.local.json`.
2. Mở `Calendar.sln`, chọn `Calendar.App (Package)`, F5.
