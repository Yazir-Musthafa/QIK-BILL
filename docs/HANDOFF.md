# Handoff: end of Phase 3 (10 October 2026)

Read this first, then `PROJECT_BRIEF.md` (the full spec; there is no `AGENTS.md`), `PROGRESS.md` (evidence for each claim) and `ACCOUNTING_MAP.md`. **Phase 4 (products and inventory) is next.** The owner plans to test Phases 1–3 on Windows before then; see `QA_WINDOWS.md`.

## State

- **Build:** clean, 0 warnings. `dotnet restore --locked-mode` passes, and a `win-x64` self-contained publish produces `QIKSuite.exe`.
- **Tests:** **455 passing** (`dotnet test tests/Nexora.Tests`).
- **Migrations:** `InitialWorkspace`, `CompaniesUsersRolesAudit`, `AccountingEngine`. The last contains SQLite immutability triggers.
- **Not yet run:** the WPF UI, printing and the installer. Nothing has run on Windows.

## What was done in Phases 2 and 3

**Phase 2: companies, users, roles, audit, isolation.**
- Company profile with PAN and GSTIN validation, branches, warehouses, and company numbering.
- Six built-in roles and a permission catalogue; permissions are enforced by `IAuthorizer.Demand` in every service, with no escalation and a last-administrator guard.
- Temporary passwords that must be changed.
- An audit trail with old and new values, hash-chained and numbered per company, with a Verify function.
- Company isolation via query filters, a write guard, composite `(CompanyId, Id)` foreign keys, and a source scan limiting filter bypasses to `EfWorkspaceStore`.
- Upgrade of Phase 1 databases.
- Settings screens.

**Phase 3: accounting engine.**
- **Domain** (`Domain/Accounting`): `Account`, `Voucher`, `VoucherLine`, `LedgerEntry`, `FinancialYearPeriod`, `Money`.
- **Engine** (`Nexora.Accounting`): `ChartOfAccountsTemplate`, `SystemAccounts`, `AccountingSeedContributor`, `VoucherService` (posting engine), `LedgerService` (statements, trial balance, P&L, balance sheet, years), and `LedgerCalculator` (pure report logic).
- Screens under Accounts in the sidebar, plus Settings → Financial years.

## Design decisions to keep

- **Posting is all-or-nothing.** Everything happens in one writable `IUnitOfWork` (`BEGIN IMMEDIATE` on SQLite) plus an in-process gate, so numbers are gapless and a voucher cannot be posted twice.
- **Ledger entries are the only report source.** New reports must read `LedgerEntry` through `LedgerCalculator`-style functions so everything keeps reconciling. Add a reconciliation test for each new report.
- **Carry-forward is computed, not posted.** Don't add year-end closing entries; closing a year only locks it.
- **Immutability is enforced in three places:** the domain, `ImmutabilityRules` in `NexoraDbContext`, and the SQLite triggers. A new append-only table needs all three, plus a test.
- **New business tables must:** implement `ICompanyOwned` (and `IAudited` where changes matter), use composite foreign keys for references between company-owned rows, and have a migration. The isolation tests then cover them automatically.
- **Seeding:** register seed data through `ICompanySeedContributor`, so existing companies receive it on upgrade.

## Files changed in Phase 3

- **Domain:** `Domain/Accounting/{Money,Account,Voucher}.cs`.
- **Engine:** `src/Nexora.Accounting/*` (Phase 1's placeholder README removed).
- **Infrastructure:** `Persistence/Configurations/AccountingConfigurations.cs`, `NexoraDbContext.cs`, and the `AccountingEngine` migration.
- **Desktop:** `ViewModels/Accounting/*`, `Views/Accounting/*`, `ViewModels/{Navigation,ShellViewModel}.cs`, `ViewModels/Settings/SettingsViewModels.cs`, `Controls/Converters.cs`, `App.xaml(.cs)`, `Views/ViewTemplates.xaml`.
- **Tests:** `tests/Nexora.Tests/Accounting/*`, a timing fix in `Workspace/WorkspaceServiceTests.cs`, and accounting registration in `Infrastructure/SqliteTestDatabase.cs`.
- **Docs:** `ACCOUNTING_MAP.md`, `PROGRESS.md`, `HANDOFF.md`, `QA_WINDOWS.md`, `README.md`.

## Known problems

- **The UI has never been run.** Expect XAML runtime fixes on Windows, especially in the voucher editor's inline combo boxes, the settings section host and popups.
- **Reports aggregate in memory**, and ledger statements are limited to one financial year.
- **Roles take effect at the next session.** Session permissions are cached until sign-in, company switch or refresh.
- **The audit chain** reveals tampering but cannot stop someone from rewriting the whole file. External anchoring belongs in Phase 9.
- **The one-reversal unique index** relies on SQLite treating NULLs as distinct (see `VoucherConfiguration`).

## Next steps

1. **Test on Windows** with `QA_WINDOWS.md`, and fix runtime issues before adding features.
2. **Build the shared data-table control** (column hide, filter, saved layouts, totals row) before Phase 4 list screens.
3. **Phase 4, inventory:**
   - products, units, variants, warehouses and stock movement ledger;
   - FIFO and weighted-average costing chosen per company;
   - stock balances derived from movements;
   - no negative stock unless the company allows it.
   - Post inventory effects through `VoucherService` using `ACCOUNTING_MAP.md`.
4. **Phase 5, sales and purchases:** use `SystemAccounts` for posting, `Money.Round` for tax, and `NumberingRule` for invoice numbers. Recalculate all totals in the Application layer.
5. **Before Phase 6,** pick the PDF and barcode libraries after checking their licences (`THIRD_PARTY_LICENSES.md`).
