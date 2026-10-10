# QIK Suite: notes for coding agents

The spec is `docs/PROJECT_BRIEF.md`. Read `docs/HANDOFF.md`, `docs/PROGRESS.md` and `docs/WORKING_GUIDE.md` before changing anything.

## Build and test (macOS or Windows)

```bash
export PATH="$HOME/.dotnet:$PATH"          # macOS: the SDK lives in ~/.dotnet
dotnet restore Nexora.sln --locked-mode
dotnet build Nexora.sln                    # warnings are errors
dotnet test tests/Nexora.Tests
```

`EnableWindowsTargeting=true` lets the WPF project build on macOS. The UI can't run there; mark anything that needs a Windows run as "UI unverified at runtime".

## Rules

- **Keep the guide current.** After every phase, update `docs/WORKING_GUIDE.md` before stopping:
  - add the new screens to the status table;
  - add test cases for every new function, in the same format;
  - re-classify anything that changed.
  Never mark something FULLY FUNCTIONAL unless the code is wired to a real service and tests cover its logic. `WorkingGuideTests` fails if a sidebar item or Settings section has no status row.
- **Tests.** Never weaken or delete tests to make them pass. The design-rule, binding, isolation and reconciliation tests stay.
- **Migrations.** Every model change gets an EF migration. Move `NexoraDbContextModelSnapshot.cs` into `Persistence/Migrations/Sqlite/` if EF writes it elsewhere.
- **Business tables.** Every new one implements `ICompanyOwned` (and `IAudited` where changes matter), and uses composite `(CompanyId, Id)` foreign keys between company-owned rows.
- **Permissions.** Enforce them in Application services with `IAuthorizer.Demand`, not only by hiding UI.
- **Money and accounting.** Money is `decimal` (paise). Accounting effects go through `VoucherService` using `docs/ACCOUNTING_MAP.md`.
- **Views.** Use only theme tokens: no hard-coded colours, font sizes or radii. Every page-level binding must exist on its view model (`ViewBindingTests`).
- **Naming.** Product name: **QIK Suite**. The executable is `QIKSuite.exe`, and WPF resource URIs use `/QIKSuite;component/`. Namespaces stay `Nexora.*` (see `docs/DECISIONS.md`).
- **Honesty.** Never claim something works unless it was built, run or tested.
- **Commits.** Small commits with a prefix naming the agent (`claude:` or `codex:`).
