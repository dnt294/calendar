# CLAUDE.md

Calendar client WinUI 3 cho Windows 11, dùng cá nhân. Đọc trước khi làm:

- `docs/spec.md` — scope v1 và **ngoài scope** (không tự thêm tính năng ngoài spec).
- `docs/architecture.md` — cấu trúc project, quy tắc phụ thuộc `App → Core ← Providers.*`.
- `docs/plan.md` — làm theo phase, đánh dấu `[x]` khi task đạt tiêu chí "Xong khi".

Quy ước:
- Core không tham chiếu Windows/WinUI hay SDK provider. Logic thuần (layout, sync, mapping) phải có test xUnit.
- Không commit secret: `appsettings.local.json` đã gitignore.
- Docs viết tiếng Việt, code/identifier tiếng Anh.
