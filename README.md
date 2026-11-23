# SysSentinel - Ngày 1

Solution: Core (models/interfaces) -> Data (SQLite) -> UI (WPF, MVVM, DI).

## Chạy
```powershell
dotnet restore
dotnet build
# Chạy terminal / IDE bằng quyền Administrator (app.manifest = requireAdministrator)
dotnet run --project src/SysSentinel.UI
```
Target mặc định: net10.0. Nếu chỉ có .NET 8 SDK: đổi `net10.0` -> `net8.0`, `net10.0-windows` -> `net8.0-windows`,
và version gói `10.0.*` -> `8.0.*` trong 3 file .csproj.

DB: `%LocalAppData%\SysSentinel\syssentinel.db`  |  Log: `%LocalAppData%\SysSentinel\logs\`
