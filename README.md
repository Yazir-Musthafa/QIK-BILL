# Nexora Business Suite

A Windows desktop application (WPF, .NET 10) for billing, inventory, accounting, CRM and service management for Indian businesses. **Status: Phase 1 of 9.** Only the foundation exists: the shell, design system, themes, database, sign-in and first-run setup. It records no business transactions yet. Do not use it for real accounts or GST.

The full specification is in `docs/PROJECT_BRIEF.md`, and status is in `docs/PROGRESS.md`.

## Layout

| Project | Role |
|---|---|
| `src/Nexora.Domain` | Entities and business rules. No framework dependencies. |
| `src/Nexora.Application` | Use cases (`IWorkspaceService`), validation, password hashing, persistence interfaces. |
| `src/Nexora.Infrastructure` | EF Core `NexoraDbContext`, SQLite provider behind `IDatabaseProvider`, migrations. |
| `src/Nexora.Accounting` | Reserved for Phase 3. |
| `src/Nexora.Printing` | Reserved for Phase 6. |
| `src/Nexora.Desktop` | WPF app: views, view models, design system (`Themes/`). Builds `Nexora.exe`. |
| `tests/Nexora.Tests` | xUnit tests: domain, security, SQLite integration, XAML design rules. |

## Build

You need the .NET SDK version in `global.json` (10.0.401, latest patch allowed).

**Windows:**

```powershell
./scripts/build-windows.ps1            # restore, build, test, publish win-x64
./artifacts/publish/win-x64/Nexora.exe
```

**macOS or Linux (build and test only; WPF cannot run):**

```bash
dotnet restore Nexora.sln --locked-mode
dotnet build Nexora.sln
dotnet test tests/Nexora.Tests
```

`EnableWindowsTargeting=true` in `Directory.Build.props` lets the WPF projects compile on macOS and Linux.

CI: `.github/workflows/windows.yml` runs the build script on `windows-latest`.

## Database migrations

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/Nexora.Infrastructure \
  --output-dir Persistence/Migrations/Sqlite --namespace Nexora.Infrastructure.Persistence.Migrations.Sqlite
```

EF may write `NexoraDbContextModelSnapshot.cs` into a folder named after its namespace. If it does, move the file into `Persistence/Migrations/Sqlite/`. The app applies migrations on startup. A test fails if the model has changes with no migration.

## Runtime data

Data lives in `%LOCALAPPDATA%\Nexora\BusinessSuite\`: `nexora.db` (SQLite, WAL mode) and `logs\nexora-YYYYMMDD.jsonl`. Logs roll daily and are kept for 14 days. Logs record error types, never passwords or exception messages.

## Keyboard

Ctrl+K command palette · Ctrl+B sidebar · Ctrl+Shift+L theme · Ctrl+1 dashboard · F1 help · Esc close · Ctrl+Shift+G component gallery (hidden).
