# Nexora implementation progress

## Phase 3: complete on macOS, Windows runtime unverified (10 October 2026)

**Scope:** chart of accounts, vouchers, ledgers, double-entry posting and financial-year handling, with unit tests. **WPF UI, printing and installer remain unverified** (no Windows machine). Phases 4–9 have not started.

### Verified by build or test on macOS

| Area | Evidence |
|---|---|
| Build | Clean build: 0 warnings, 0 errors. `dotnet restore --locked-mode` passes before and after a `win-x64` publish. Migration `AccountingEngine` added; the model has no unmigrated changes. |
| Tests | **455 passed, 0 failed**, on 3 consecutive runs after a clean build. Earlier, 15 consecutive runs passed after I fixed a timing race in a Phase 1 test. |
| Double entry | `Voucher.EnsureBalanced` (domain) refuses unbalanced vouchers, single-line vouchers, zero totals, one-account vouchers, lines with both or neither side, negative amounts and more than 2 decimals. Tested in unit and integration tests. |
| Money | `decimal` throughout, stored as whole paise. Rounding policy (half away from zero to the paisa) is tested; paise round-trips exactly. |
| Posting | One transaction covers checks, number allocation, freezing the voucher, ledger entries and the audit event. Numbers follow the numbering rules (`RV/2026-27/0001`) and continue per type. |
| Idempotency and concurrency | Posting twice is refused. 8 concurrent posts of the same draft post it once. 20 concurrent posts of different drafts get unique, gapless numbers 1–20. |
| Immutability | Posted vouchers, their lines and ledger entries cannot be changed. The service refuses (reverse instead), the data context throws, and SQLite triggers abort direct `UPDATE`/`DELETE` (6 raw-SQL cases tested). Deleting a demonstration company with posted vouchers still works. |
| Reversal | Posts a swapped voucher (`RJ/...`) and leaves the original unchanged. The net effect is zero, a second reversal is refused, and it is audited. |
| Rules | Payment must credit cash/bank; receipt must debit cash/bank; contra is cash/bank only; journal must not use cash/bank. Posting is refused for groups, inactive accounts, dates before the books start, years not yet opened, and closed years. |
| Financial years | Open next year, close (refused while drafts are dated in it), reopen. Carry-forward: asset/liability/equity balances continue, income and expense restart, prior profit appears in Retained Earnings. Tested in pure-calculation tests and against the database. |
| Opening balances | Posted as an Opening voucher; any difference goes to Opening Balance Difference; refused for income and expense accounts. |
| Reconciliation | After a mixed set of vouchers (openings, receipts, payments, contra, a GST journal, and a reversal), the checks agree: the trial balance balances; every ledger statement's closing balance equals its trial-balance figure; the balance sheet balances; P&L net profit equals the balance sheet's current-year profit; and trial-balance income and expense totals equal the P&L totals. |
| Chart of accounts | Default Indian chart with 28 system keys. Template tests check codes and names are unique, parents are earlier groups of the same nature, every key exists once, and mapped natures are right. Existing companies get the chart on upgrade. |
| Permissions and isolation | Read-only users can view but not post or change; billing operators cannot see vouchers or ledgers; accountants can post and reverse. Another company's vouchers are invisible and cannot be reversed. Its accounts cannot be used, and SQLite rejects a voucher line pointing to another company's account. |
| Mutation checks | Breaking any one of these failed 1–2 tests each: the balance check, the ledger immutability rule (the trigger still blocked the change), the carry-forward, the double-post check, the closed-year check. |

### Built but unverified (needs Windows)

These screens compile and their XAML passes the design-rule tests: chart of accounts, vouchers (editor with live totals, post, reverse), ledger statement, trial balance, profit and loss with balance sheet, opening balances, and Settings → Financial years.

### Known gaps (Phase 3)

- **Phase 8 items in the brief:** cash flow statement, bank reconciliation, cost and profit centres, ageing, bill-wise allocation.
- **Report performance:** reports aggregate in memory, which is fine at desktop scale. Amounts are integer paise, so SQL aggregation can be added later.
- **Ledger statements** must stay within one financial year (by design).
- **Voucher editor:** dates are typed as dd-mm-yyyy (no date picker yet). There's no keyboard shortcut for adding a line and no voucher printing (Phase 6).
- **Unique index for at most one reversal** relies on SQLite treating NULLs as distinct. SQL Server would need a filtered index.

## Phase 2: complete on macOS, Windows runtime unverified (10 October 2026)

**Scope:** company profiles, branches and warehouses, company-specific numbering, users, roles and per-action permissions, audit trail, data isolation between companies. **WPF UI, printing and installer remain unverified** (no Windows machine).

### Verified by build or test on macOS

| Area | Evidence |
|---|---|
| Build | `dotnet build Nexora.sln`: 0 warnings, 0 errors. Migration `CompaniesUsersRolesAudit` added; a test confirms the model has no unmigrated changes. |
| Tests | **341 passed, 0 failed.** |
| Data isolation | Each company's data is tagged with `CompanyId` and keyed to Companies with cascade delete. Every query is filtered to the signed-in company; companies and users are filtered too. Tests show a company cannot see another's branches, roles, numbering, memberships, users, company record or audit entries. That holds through the services, through the unit of work directly, even when looking rows up by id, and with no session. |
| Write isolation | Saving a row for another company, or moving a row to another company, is refused. |
| Database-level isolation | Composite `(CompanyId, Id)` foreign keys. Raw SQL that links a warehouse to another company's branch, or a membership to another company's role, is rejected by SQLite. |
| No accidental bypass | A model test checks that every company-owned table has the filter in its SQL and a key to Companies. A source scan checks that only `EfWorkspaceStore` (and the audit recorder) can bypass the filter. |
| Permissions | Every protected service call checks the session's permission in the Application layer. Tests cover: read-only users can view but not change anything; accountants cannot manage users; nobody can grant or assign permissions they don't hold; every company keeps an active administrator; deactivated users cannot sign in; role edits take effect; a password reset is refused when the user also works in a company the caller doesn't manage; usernames are unique across companies. |
| Passwords | Temporary passwords must be changed before anything else; the old password stops working after a change. |
| Audit trail | Setup, changes (who, what, when, old and new values), password changes (hash masked), sign-ins and resets are all recorded. Entries are hash-chained (SHA-256) and numbered per company. Editing or deleting a stored row is detected by Verify. Entries cannot be changed through the application, and a failed save leaves no audit entries. |
| Upgrade | A Phase 1 database (companies and users, no roles) gets roles and numbering on startup, and existing users stay administrators. Running it twice changes nothing. |
| Domain rules | GSTIN check character (verified against 6 public GSTINs), PAN format, GSTIN must match the company state and PAN, PIN code, email, logo type and size (512 KB), six built-in roles, discount limit, numbering format with `{FY}`. |
| Mutation checks | Removing the company filter failed 12 tests. Disabling the write guard failed 2. Removing one permission check failed 2. Removing the escalation check failed 1. |

### Built but unverified (needs Windows)

The Settings screens (general, company profile with logo upload, branches and warehouses, numbering, users, roles and permissions, audit trail with integrity check), the forced password change, change password from the user menu, and sidebar items hidden by permission. They compile, and the XAML passes the design-rule tests.

### Known gaps (Phase 2)

- Branch-level GSTINs, company preferences such as costing method and negative stock (Phase 4/5), and logo use on printed documents (Phase 6) are not done.
- The audit chain shows tampering but cannot stop someone with file access from rewriting the whole chain. External anchoring (for example a hash in backups) is a Phase 9 item.
- Import/export and backup/restore are Phase 8, per the phase plan.
- Session timeout and auto-lock: Phase 9.

## Phase 1: complete on macOS, Windows runtime unverified (10 October 2026)

**Scope:** solution structure, application shell, design system, light and dark themes, dependency injection, logging, database setup and migrations, login, and the first-run wizard. Phases 2–9 have not started.

**Environment:** macOS 26.2 on Apple Silicon, .NET SDK 10.0.401 in `~/.dotnet`, `EnableWindowsTargeting=true`. **The WPF UI, printing and the installer cannot be run or tested on this machine.**

### Verified by build or test on macOS

| Area | Evidence |
|---|---|
| Solution builds | `dotnet build Nexora.sln`: 0 warnings, 0 errors, with warnings treated as errors. Includes the XAML markup compile, which checks types, properties and element names. |
| Locked restore | `dotnet restore --locked-mode` passes, both before and after a `win-x64` publish. |
| Self-contained publish | `dotnet publish -r win-x64 --self-contained` produces `Nexora.exe`, the native `e_sqlite3.dll` and the WPF runtime (149 MB). The output was not run. |
| Tests | **221 passed, 0 failed** (`dotnet test tests/Nexora.Tests`). |
| Domain rules | Financial year (April–March, labels, boundaries), GST state codes, company and username validation, lockout after 5 failures for 15 minutes. |
| Password security | PBKDF2-HMAC-SHA256 with 600,000 iterations and a unique 16-byte salt. Comparison is fixed-time. Older hashes are upgraded at sign-in. Malformed hashes fail without throwing. Password policy has a 10-character minimum and rejects passwords containing the username. |
| Database | A migration creates the schema on a new file. Re-running it is safe and keeps data. No model changes lack a migration. WAL journal mode. The plaintext password is absent from the database and WAL files. |
| Workspace flows | Setup is atomic, can only run once and stores nothing on invalid input. Sign-in matches usernames case-insensitively. Unknown user and wrong password give the same message. Sign-in returns to the last company. Company switching works. The demo company can be deleted, but not while it is active, and real companies cannot be deleted this way. Preferences persist. Operations require a session. |
| Formatting | Indian digit grouping (₹12,34,56,789). |
| Design rules (XAML source) | Both themes have the same keys. The brief's colours are exact. Neutral tokens stay neutral. WCAG AA contrast passes for 26 text-on-background pairs across the two themes. No hard-coded colours, gradients, blur or extra shadows. Font sizes and corner radii come from tokens. Every resource key is defined. Theme brushes are dynamic. No emoji. Each rule was checked to fail on a planted violation. |
| Dependencies | All MIT or Apache-2.0 (`THIRD_PARTY_LICENSES.md`). No known vulnerabilities. |
| Tests can fail | Disabling the lockout code made the lockout test fail. Planting design violations failed 6 rules. |

### Built but unverified (needs Windows)

- The app starting at all: DI composition in `App.ConfigureServices`, XAML resource lookup at runtime, theme dictionary swapping, pack URIs.
- How every screen looks and behaves: setup wizard, sign-in, shell, sidebar collapse, command palette, company switcher, popups, dashboard, settings, help, component gallery, dialogs, toasts.
- Keyboard shortcuts, Tab order, focus rings, Narrator, high contrast.
- Animations, and turning them off when Windows animation effects are off.
- DPI scaling from 1366×768 to 4K, and font fallback (Segoe UI Variable, Fluent Icons or MDL2).
- Writing logs and the database under `%LOCALAPPDATA%`, the single-instance mutex, and the startup error message.
- `scripts/build-windows.ps1` and `.github/workflows/windows.yml`: neither has been run (no PowerShell here, no Git remote).

`docs/QA_WINDOWS.md` is the checklist for these.

### Not done in Phase 1 (known gaps)

- **Data table:** column hiding, filtering, saved column layouts and a reusable totals row are not built. The native grid handles sorting, resizing, reordering and keyboard navigation.
- No chart first-load draw animation.
- No dedicated high-contrast theme.
- Users are installation-wide administrators with access to every company. Roles, permissions, per-company access, password reset and audit come in Phase 2.
- Session timeout and auto-lock are not implemented (Phase 9 security).
- The demonstration company has no stored sample records, only fixed illustrative dashboard figures. Phase 4/5 should seed real demo transactions.
- No installer (Phase 9).

This is an implementation checkpoint, not release approval. It claims no commercial readiness, accounting correctness or GST compliance.
