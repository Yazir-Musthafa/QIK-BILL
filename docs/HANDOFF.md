# Handoff: end of Phase 4 (11 October 2026)

Read this first, then:

- `PROJECT_BRIEF.md`: the full spec; there is no `AGENTS.md`;
- `PROGRESS.md`: the evidence for each claim;
- `ACCOUNTING_MAP.md`;
- `WORKING_GUIDE.md`: screen status and the Windows test script.

**Phase 5 (sales and purchases with GST) is next.** The owner has confirmed only that the app opens on Windows.

## State

- **Build:** clean, 0 warnings. `dotnet restore --locked-mode` passes, and a `win-x64` self-contained publish produces `QIKSuite.exe`.
- **Tests:** **605 passing** (`dotnet test tests/Nexora.Tests`): 327 behavioural, 246 XAML design-rule, 31 guide checks and 1 binding check.
- **Migrations:** `InitialWorkspace`, `CompaniesUsersRolesAudit`, `AccountingEngine`, `Inventory`. The last two contain SQLite immutability triggers.
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

## Rule: keep the working guide current

After every phase, update `docs/WORKING_GUIDE.md` before stopping:

- add the new screens to the status table;
- add test cases for every new function, in the same format;
- re-classify anything that changed.

Never mark something FULLY FUNCTIONAL unless the code is wired to a real service and tests cover its logic. `Design/WorkingGuideTests` fails the build if a sidebar item (`NavCatalog`) or Settings section has no status-table row. The same rule is in `CLAUDE.md`.

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

## Phase 4 in brief

- **Shared data table:** `Controls/QikDataGrid.cs`, styled in `Themes/Controls.xaml`. Its logic lives in `Application/Preferences/TableLayout.cs`: layout merge, filter, totals and per-user storage through `UserPreferenceService`. Use `<controls:QikDataGrid LayoutKey="…">` for new lists, and mark summable columns with `controls:Table.Total="True"`.
- **Inventory domain** (`Domain/Inventory`): units, tax categories, products and variants, unit conversions, batches, settings, stock documents, `StockMovement` (append-only) and `CostPool` (FIFO and weighted average).
- **Services:**
  - `InventoryCatalogueService`: products, units, tax categories, settings.
  - `StockService`: documents, balances, valuation, reconciliation, alerts.
  - `InventorySeedContributor`: upgrades existing companies.
- **Accounting link:** `Application/Abstractions/ILedgerPoster`, implemented by `Accounting/LedgerPoster.cs` on top of `VoucherService.PostCoreAsync` and `ReverseCoreAsync`, under one shared posting lock. Business documents post vouchers in their own transaction this way. Phase 5 sales and purchases should use the same path.
- **Numbering:** `NumberAllocator` is shared by vouchers and stock documents. Use it for invoice numbers in Phase 5.
- **The reconciliation invariant:** stock value = Inventory ledger balance. Keep it by never posting to Inventory except through `ILedgerPoster` from stock-affecting documents. Phase 5 sales must post COGS from `CostPool` in the same transaction as the stock issue.

## Next steps

1. **Test Phase 4 on Windows** with `WORKING_GUIDE.md` sections 3.1–3.7, and report anything that fails.
2. **Phase 5, sales and purchases with GST:**
   - parties as sub-ledgers under Sundry Debtors and Creditors;
   - invoices that issue or receive stock through `StockService` and post the accounting (including COGS) in one transaction;
   - dated GST rates by place of supply;
   - reservations from sales orders;
   - totals recalculated in the Application layer, with idempotent posting.
3. **Data-table follow-ups:** per-column filters, and editing or deactivating units and tax categories.
4. **Before Phase 6,** choose the PDF and barcode libraries after a licence check.
