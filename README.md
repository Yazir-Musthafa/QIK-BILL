# QIK Suite

A Windows desktop application (WPF, .NET 10) for billing, inventory, accounting, CRM and service management for Indian businesses. **Status: Phases 1–8 implemented; Phase 9 remains.** Built so far:

- the shell, design system and themes;
- companies, users, roles and permissions, the audit trail and company data isolation;
- a double-entry accounting engine: chart of accounts, vouchers, ledgers, trial balance, profit and loss, balance sheet and financial years;
- products and inventory: an append-only stock ledger, FIFO or weighted average costing, batches, serials and expiry, and automatic accounting that reconciles to the ledger.

Sales/purchases with recorded GST, printing/PDF, expenses, CRM, service jobs, 30 reports, a live dashboard, CSV/Excel transfer and whole-workspace backup/restore are implemented and covered by automated service tests. GST portal filing is not implemented. On Windows the app has been seen to open; its screens have not yet been tested there. Do not use it for real accounts or GST.

The full specification is in `docs/PROJECT_BRIEF.md`, and status is in `docs/PROGRESS.md`.

## Layout

| Project | Role |
|---|---|
| `src/Nexora.Domain` | Entities and business rules. No framework dependencies. |
| `src/Nexora.Application` | Use cases (workspace, companies, users, roles, audit), permission checks, password hashing, and the persistence interfaces (`IUnitOfWork`, `IWorkspaceStore`). |
| `src/Nexora.Infrastructure` | EF Core `NexoraDbContext` (company filters, write guard, audit capture, immutability), SQLite provider behind `IDatabaseProvider`, and migrations. |
| `src/Nexora.Accounting` | Posting engine, chart-of-accounts template, ledgers and financial statements. See `docs/ACCOUNTING_MAP.md`. |
| `src/Nexora.Printing` | Document/label templates, PDF, barcodes and print services; Windows drivers live in Desktop. |
| `src/Nexora.Reporting` | Report catalogue, SQL aggregation, dashboard, CSV/Excel import/export and report output. |
| `src/Nexora.Desktop` | WPF app: views, view models, design system (`Themes/`). Builds `QIKSuite.exe`. |
| `tests/Nexora.Tests` | xUnit tests: domain, security, permissions, company isolation, audit, accounting and reconciliation, SQLite integration, XAML design rules. |

## Build

You need the .NET SDK version in `global.json` (10.0.401, latest patch allowed).

**Windows:**

```powershell
./scripts/build-windows.ps1            # restore, build, test, publish win-x64
./artifacts/publish/win-x64/QIKSuite.exe
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

Data lives in `%LOCALAPPDATA%\QIKSuite\`: `qiksuite.db` (SQLite, WAL mode) and `logs\qiksuite-YYYYMMDD.jsonl`. Logs roll daily and are kept for 14 days. Logs record error types, never passwords or exception messages.

## Keyboard

Ctrl+K command palette · Ctrl+B sidebar · Ctrl+Shift+L theme · Ctrl+1 dashboard · F1 help · Esc close · Ctrl+Shift+G component gallery (hidden).
