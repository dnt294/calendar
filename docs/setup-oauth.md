# Thiết lập OAuth (một lần, miễn phí)

## Google (Gmail + Workspace)

1. https://console.cloud.google.com → tạo project `personal-calendar`.
2. *APIs & Services → Library* → bật **Google Calendar API**.
3. *OAuth consent screen* (Google Auth Platform):
   - User type: **External**.
   - Thêm scope `https://www.googleapis.com/auth/calendar`.
   - **Publishing status → In production** (bấm *Publish app*), **không** submit verification.
     - Lý do: ở trạng thái *Testing*, refresh token hết hạn sau 7 ngày.
     - Đổi lại: lúc login sẽ có màn hình "Google hasn't verified this app" → *Advanced → Go to … (unsafe)*. Giới hạn 100 user — đủ cho cá nhân.
4. *Credentials → Create OAuth client ID* → loại **Desktop app** → tải JSON.
5. Copy `client_id`, `client_secret` vào `src/Calendar.App/appsettings.local.json` (gitignored).

**Workspace bị chặn?** Lỗi kiểu `admin_policy_enforced` / "access blocked by your admin" → nhờ admin vào *Security → API controls → App access control* thêm OAuth client ID vào danh sách Trusted.

## Microsoft (Outlook.com, sau này M365)

1. https://entra.microsoft.com → *App registrations → New registration*.
   - Tên: `personal-calendar`.
   - Supported account types: **Accounts in any organizational directory and personal Microsoft accounts**.
2. *Authentication → Add a platform → Mobile and desktop applications*:
   - Thêm redirect `ms-appx-web://microsoft.aad.brokerplugin/{client-id}` (cho WAM) và `http://localhost`.
   - *Allow public client flows* → **Yes**.
3. *API permissions* → Microsoft Graph (Delegated): `Calendars.ReadWrite`, `User.Read`, `offline_access`.
4. Copy *Application (client) ID* vào `appsettings.local.json`.

**M365 công ty:** nếu tenant tắt user consent → admin cần *Grant admin consent* cho app này.

## `appsettings.local.json` mẫu

```json
{
  "Google": { "ClientId": "xxx.apps.googleusercontent.com", "ClientSecret": "xxx" },
  "Microsoft": { "ClientId": "00000000-0000-0000-0000-000000000000" }
}
```
