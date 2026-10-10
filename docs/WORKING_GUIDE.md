# QIK Suite: working and testing guide

Last audited: **10 October 2026**, at commit `b6ee88e` plus this guide. Each statement below comes from reading the code, a clean build and the automated tests on macOS. **Nothing has been run on Windows yet**, so every screen is "UI unverified at runtime" until someone completes the test script in section 3.

## How this guide was checked

| Check | Result |
|---|---|
| Clean build (`dotnet build Nexora.sln`, after deleting all `bin`/`obj`) | Succeeded, 0 warnings, 0 errors |
| Locked package restore | Succeeded |
| Automated tests (`dotnet test tests/Nexora.Tests`) | **502 passed, 0 failed** (with this guide in place) |
| Of those, design-rule checks on XAML styling | 228 (one per XAML file per rule). They check styling discipline, not behaviour. |
| Of those, the guide's own status-table check | 30 (one per screen, plus one sanity check) |
| Of those, binding check | 1 test: every page-level XAML binding (about 400) points to a real view-model property or command |
| Logic tests (domain, services, database, accounting) | 243 |
| Desktop view models (`src/Nexora.Desktop/ViewModels`) | **No automated tests.** They target Windows and can't be loaded by the test project, so date parsing, voucher-line totals, the command palette and navigation are untested. |
| Fixed during this audit | Tests added for service paths that had none (chart editing, warehouses, branch deactivation, numbering, logo, role change). In the voucher editor, a failed Post created a second draft on each retry; it now keeps editing the same draft and shows it in the list. The numbering column label "Next number looks like" was misleading and is now "Format (shown with number 1)". |

### Status labels

- **FULLY FUNCTIONAL:** a real screen, wired to a real service that saves to the database, with automated tests covering the service logic. The UI itself is still unverified at runtime.
- **PARTIAL:** the screen exists, but some actions are missing, not wired, or not backed by tests. The row says exactly which.
- **LOGIC ONLY:** a service and tests exist, but there is no screen.
- **PLACEHOLDER:** the screen only says "Not available yet".
- **NOT STARTED:** nothing exists.

## 1. Status table

All rows: **Verified on Windows = Not yet.**

Two abbreviations in the "Tested by" column: `WS` = `Workspace/WorkspaceServiceTests`, `Perm` = `Security/PermissionTests`.

| Module | Screen | Status | What works | What does not work yet | Tested by (automated tests) | Verified on Windows |
|---|---|---|---|---|---|---|
| Start | First-run setup wizard | FULLY FUNCTIONAL | Three steps (company, administrator, review) with inline validation. Creates the company, six roles, administrator membership, numbering rules, chart of accounts (57 accounts), first financial year, and optionally a demo company. Signs you straight in. | Nothing known | `WS`, `SetupValidatorTests`, `MigrationTests` | Not yet |
| Start | Sign-in | FULLY FUNCTIONAL | Username (any case) and password. Same message for an unknown user and a wrong password. Locks for 15 minutes after 5 failures. Returns to the last company you used. | No "forgot password" self-service; an administrator resets it | `WS`, `UserLockoutTests`, `Perm` | Not yet |
| Start | Forced password change / Change password (user menu) | FULLY FUNCTIONAL | Required after a temporary password; also available from the user menu. Checks the current password and the 10-character policy. | Nothing known | `Perm`, `AuditTrailTests` | Not yet |
| Shell | Sidebar, top bar, command palette, theme, company switcher, user menu | PARTIAL | Grouped sidebar collapses to icons (Ctrl+B) and is remembered. Ctrl+K palette searches pages and actions. Theme switch (Ctrl+Shift+L) is remembered. Company switcher changes company and role. Sidebar hides pages your role can't open. | Palette, sidebar and keyboard handling are view-model code with no automated tests. Only saving preferences and switching company are tested. | `WS` (preferences, switching), `CompanyIsolationTests` | Not yet |
| Shell | Notifications (bell) | PLACEHOLDER | Opens a panel | Always shows the same "You're all caught up…" text; no notifications exist | None | Not yet |
| Overview | Dashboard | PARTIAL | Layout, quick actions, demo company's labelled sample figures, honest "—" for real companies. Payment and Receipt quick actions open Vouchers. | **Shows no real figures.** Real companies always show "—", and the demo company shows fixed sample numbers. Real dashboard data is Phase 8. Four quick actions open placeholders. | `WS` (empty versus demo snapshot) | Not yet |
| Sales | Sales invoices | PLACEHOLDER | Says "planned for Phase 5" | Everything | None | Not yet |
| Sales | Quotations | PLACEHOLDER | Says "planned for Phase 5" | Everything | None | Not yet |
| Sales | Customers | PLACEHOLDER | Says "planned for Phase 5" | Everything. Workaround: create customer ledgers under Sundry Debtors in Chart of accounts. | None | Not yet |
| Purchases | Purchase invoices | PLACEHOLDER | Says "planned for Phase 5" | Everything | None | Not yet |
| Purchases | Suppliers | PLACEHOLDER | Says "planned for Phase 5" | Everything. Workaround: create supplier ledgers under Sundry Creditors. | None | Not yet |
| Inventory | Products | PLACEHOLDER | Says "planned for Phase 4" | Everything | None | Not yet |
| Inventory | Stock | PLACEHOLDER | Says "planned for Phase 4" | Everything | None | Not yet |
| Accounts | Chart of accounts | FULLY FUNCTIONAL | Tree with indentation and System badges. Create ledgers and groups (nature comes from the group). Rename, move within the same nature, deactivate and reactivate. Duplicate codes and names are refused. The type can't change once an account has postings. | Changing the Type of an existing group is silently ignored (groups stay groups) | `CoverageGapTests` (added in this audit), `ChartOfAccountsTemplateTests`, `AccountDomainTests`, `PostingTests` | Not yet |
| Accounts | Vouchers | FULLY FUNCTIONAL | List of all vouchers. Editor for Journal, Payment, Receipt, Contra, Sales, Purchase, Expense, Income, Credit note, Debit note. Live debit/credit totals and difference. Save draft, Post (numbered), Delete draft, Reverse with a date. Posted vouchers open read-only. | No search or filter on the list. No date picker (type dd-mm-yyyy). No printing (Phase 6). Pressing Post on an invalid voucher first saves it as a draft; it stays open and in the list until you fix and post it or delete it. | `PostingTests`, `VoucherDomainTests`, `VoucherRulesTests`, `AccountingAccessTests` | Not yet |
| Accounts | Ledgers | FULLY FUNCTIONAL | Statement for any ledger account: opening balance, lines, running balance, closing balance | The range must fall inside one financial year (by design). No export or print. | `PostingTests`, `LedgerCalculatorTests` | Not yet |
| Accounts | Trial balance | FULLY FUNCTIONAL | Balances as of a date, totals, and a "Does not balance" badge if they ever differ | No group subtotals. No export or print. | `PostingTests`, `LedgerCalculatorTests` | Not yet |
| Accounts | Financial statements | FULLY FUNCTIONAL | Profit and loss (from–to) and balance sheet (as of "to"), with prior years' profit carried into Retained Earnings | No group subtotals. No cash flow statement (Phase 8). No export or print. | `PostingTests`, `LedgerCalculatorTests` | Not yet |
| Accounts | Opening balances | FULLY FUNCTIONAL | Enter debit/credit for asset, liability and equity ledgers. Posts one opening voucher (`OB/…`); any difference goes to Opening Balance Difference. | Corrections are made by reversing the opening voucher; there is no edit | `PostingTests` | Not yet |
| Accounts | Expenses | PLACEHOLDER | Says "planned for Phase 7" | Everything. Workaround: Payment or Expense vouchers. | None | Not yet |
| Relationships | CRM | PLACEHOLDER | Says "planned for Phase 7" | Everything | None | Not yet |
| Relationships | Service jobs | PLACEHOLDER | Says "planned for Phase 7" | Everything | None | Not yet |
| Insights | Reports | PLACEHOLDER | Says "planned for Phase 8" | Everything. The accounting reports are under Accounts. | None | Not yet |
| System | Print centre | PLACEHOLDER | Says "planned for Phase 6" | Everything; nothing can be printed or exported to PDF | None | Not yet |
| System | Settings | FULLY FUNCTIONAL | Section list showing only the sections your role allows; each section is listed below | See each section | See the sections | Not yet |
| System | Help | PARTIAL | Lists the keyboard shortcuts | Static text, with no service and no tests. Billing function keys are listed as future (Phase 5). | None | Not yet |
| Settings | Settings → General | FULLY FUNCTIONAL | Theme (light/dark). Your companies with your role in each. Create a company (you become its administrator). Delete the demonstration company. | Nothing known | `WS`, `Perm`, `CompanyIsolationTests` | Not yet |
| Settings | Settings → Company profile | FULLY FUNCTIONAL | Trade and legal name, address, city, PIN code, state, phone, email, PAN and GSTIN, with validation (GSTIN check character, state match, PAN match). Logo upload (PNG/JPEG up to 512 KB) and remove. | The logo is stored but **not shown anywhere yet** (it will be used on printed documents in Phase 6). GSTIN is checked offline only. | `CompanyProfileTests`, `TaxIdentifierTests`, `AuditTrailTests`, `CoverageGapTests` | Not yet |
| Settings | Settings → Branches and warehouses | FULLY FUNCTIONAL | Create and edit branches (code, name, state, address) and warehouses (code, name, optional branch). Activate and deactivate. Duplicate codes are refused. | Branches and warehouses aren't used by any other screen yet (Phase 4/5) | `CoverageGapTests`, `CompanyIsolationTests` | Not yet |
| Settings | Settings → Document numbering | FULLY FUNCTIONAL | Prefix (with {FY}), number width and yearly reset for 17 document types. Changes apply to the next posted voucher. | Only voucher types use numbering today; invoice and order types wait for Phases 5–6 | `NumberingTests`, `CoverageGapTests`, `PostingTests` | Not yet |
| Settings | Settings → Users | FULLY FUNCTIONAL | List users. Create a user with a temporary password. Give an existing account access. Change role. Remove and restore access. Reset password. Last-administrator and no-escalation rules. | Nothing known | `Perm`, `CoverageGapTests` | Not yet |
| Settings | Settings → Roles and permissions | FULLY FUNCTIONAL | Six built-in roles plus custom roles. Tick permissions by module, set a discount limit, delete unused custom roles. You can't grant what you don't hold. | The discount limit is stored but not used until sales exist (Phase 5) | `RoleTests`, `Perm` | Not yet |
| Settings | Settings → Financial years | FULLY FUNCTIONAL | List years with their draft counts. Open the next year. Close a year (refused while drafts are dated in it). Reopen. | Nothing known | `PostingTests`, `AccountingAccessTests` | Not yet |
| Settings | Settings → Audit trail | FULLY FUNCTIONAL | Newest 500 entries: who, when, action, summary. Selecting an entry shows field-level old and new values. "Verify integrity" re-checks the hash chain. | No search, filter or paging in the screen (the service supports dates and type, the UI doesn't use them). No export. | `AuditTrailTests`, `AuditChainTests`, `CompanyIsolationTests` | Not yet |
| Hidden | Component gallery (Ctrl+Shift+G) | PARTIAL | Shows every control and state; the confirmation and toast buttons work | A developer reference screen by design; its sample buttons do nothing. No tests. | Design-rule tests only | Not yet |
| Shared | Confirmation dialogs and toasts | PARTIAL | Used for deletes, reversals, closing years and so on. Toasts disappear after 4 seconds. | View-model code only; no automated tests | None | Not yet |

### Files behind each screen

| Screen | View | View model | Service |
|---|---|---|---|
| Setup wizard | `Views/SetupWizardView.xaml` | `ViewModels/SetupWizardViewModel.cs` | `Application/Workspace/WorkspaceService.cs` (`CompleteSetupAsync`), `CompanyProvisioner.cs` |
| Sign-in | `Views/LoginView.xaml` | `ViewModels/LoginViewModel.cs` | `WorkspaceService.SignInAsync` |
| Change password | `Views/Settings/ChangePasswordView.xaml` | `ViewModels/Settings/SettingsViewModels.cs` (`ChangePasswordViewModel`) | `WorkspaceService.ChangePasswordAsync` |
| Shell | `MainWindow.xaml`, `Views/ShellView.xaml` | `ViewModels/MainViewModel.cs`, `ShellViewModel.cs`, `Navigation.cs` | `WorkspaceService` (switching, preferences) |
| Dashboard | `Views/DashboardView.xaml` | `ViewModels/DashboardViewModel.cs` | `WorkspaceService.GetDashboardAsync` → `DemonstrationDashboard.cs` |
| Placeholders | `Views/PlaceholderView.xaml` | `ViewModels/PageViewModels.cs` (`PlaceholderViewModel`) | None |
| Help, Gallery | `Views/HelpView.xaml`, `Views/GalleryView.xaml` | `ViewModels/PageViewModels.cs` | None |
| Settings host | `Views/SettingsView.xaml` | `ViewModels/Settings/SettingsViewModels.cs` (`SettingsViewModel`) | None |
| General | `Views/Settings/GeneralSettingsView.xaml` | `GeneralSettingsViewModel` | `WorkspaceService`, `Application/Companies/CompanyService.cs` |
| Company profile | `Views/Settings/CompanyProfileView.xaml` | `CompanyProfileViewModel` | `CompanyService` |
| Branches and warehouses | `Views/Settings/LocationsView.xaml` | `LocationsViewModel` | `CompanyService` |
| Document numbering | `Views/Settings/NumberingView.xaml` | `NumberingViewModel` | `CompanyService` |
| Users | `Views/Settings/UsersView.xaml` | `UsersViewModel` | `Application/Users/UserService.cs` |
| Roles and permissions | `Views/Settings/RolesView.xaml` | `RolesViewModel` | `Application/Users/RoleService.cs` |
| Audit trail | `Views/Settings/AuditView.xaml` | `AuditViewModel` | `Application/Audit/AuditService.cs` |
| Financial years | `Views/Accounting/FinancialYearsView.xaml` | `ViewModels/Accounting/AccountingViewModels.cs` (`FinancialYearsViewModel`) | `Accounting/LedgerService.cs` |
| Chart of accounts | `Views/Accounting/ChartOfAccountsView.xaml` | `ChartOfAccountsViewModel` | `Accounting/ChartOfAccountsService.cs` |
| Vouchers | `Views/Accounting/VouchersView.xaml` | `VouchersViewModel` | `Accounting/VoucherService.cs` |
| Ledgers, Trial balance, Financial statements | `Views/Accounting/{Ledger,TrialBalance,Statements}View.xaml` | `LedgerViewModel`, `TrialBalanceViewModel`, `StatementsViewModel` | `LedgerService` |
| Opening balances | `Views/Accounting/OpeningBalancesView.xaml` | `OpeningBalancesViewModel` | `VoucherService.PostOpeningBalancesAsync` |

View and view-model paths are under `src/Nexora.Desktop/`. Services are under `src/Nexora.Application/` or `src/Nexora.Accounting/`.

## 2. How to start and first run

**Requirements:** 64-bit Windows 10 or 11. The test build is self-contained, so nothing else needs installing.

1. **Start:** unzip `QIK-Suite-test-build.zip` completely, then double-click `QIKSuite.exe`. If SmartScreen says "Windows protected your PC", click **More info → Run anyway**. The build is unsigned.
2. **The setup wizard appears** the first time.
   - **Step 1, Company:**
     - Company name: up to 120 characters.
     - State: the place of business; it decides intra- or inter-state GST later.
     - First financial year: the list runs from next year back five years. The year runs 1 April – 31 March.
   - **Step 2, Administrator:**
     - Full name.
     - Username: 3–32 letters, digits, dots, hyphens or underscores.
     - Password and confirmation: at least 10 characters, not containing the username.
   - **Step 3, Review:** check the details. Optionally tick **"Also create a demonstration company with sample data"**. Then click **Create workspace**.
3. **What gets created:**
   - the company;
   - six built-in roles (Administrator, Accountant, Billing Operator, Inventory Manager, Service Staff, Read-only), with you as Administrator;
   - numbering rules for 17 document types;
   - a standard Indian chart of accounts (57 accounts and groups);
   - the first financial year, open;
   - with the box ticked, a separate "Demonstration Company (sample data)" with the same structure.
   You land on the dashboard and are already signed in.
4. **Sign in later** with your username (not case-sensitive) and password.
5. **Data:**
   - Everything is in `%LocalAppData%\QIKSuite`. Paste that into the File Explorer address bar.
   - The database is `qiksuite.db` (plus `qiksuite.db-wal` while the app runs).
   - Logs are in `logs\qiksuite-YYYYMMDD.jsonl`, one file per day, kept 14 days. Logs contain error types, never passwords.
   - If an older `%LocalAppData%\Nexora\BusinessSuite` folder exists and `QIKSuite` does not, the data is copied across on first start, and the old folder is left as a backup.
6. **Reset:** close QIK Suite, delete the `%LocalAppData%\QIKSuite` folder, and start again. The setup wizard reappears.

## 3. Test script (Windows)

Work through the sections **in order**: later tests use data from earlier ones. Tick ☐ Pass or ☐ Fail, and write a note next to any Fail.

**Before you start:** delete `%LocalAppData%\QIKSuite` if it exists.

Values used throughout:

| What | Value |
|---|---|
| Company | Test Traders, Maharashtra, FY 2026-27 |
| Administrator | Asha Rao, username `asha`, password `quiet ledger 42` |
| Temporary password for new users | `Welcome 2026!` |
| Password new users choose | `my own phrase 9` |

### 3.1 Setup and sign-in (Phase 1)

| ID | Steps | Expected result | Result |
|---|---|---|---|
| P1-SET-01 | Start QIKSuite.exe. On step 1, click **Next** without filling anything. | Red messages under Company name and State. Stays on step 1. | ☐ Pass ☐ Fail |
| P1-SET-02 | Enter "Test Traders", choose "27 · Maharashtra", choose "2026-27", click **Next**. On step 2, enter Asha Rao / asha / password `short` twice, click **Next**. | "Use at least 10 characters." under Password. | ☐ Pass ☐ Fail |
| P1-SET-03 | Enter password `quiet ledger 42` and confirm `quiet ledger 43`. Click **Next**. | "The passwords do not match." | ☐ Pass ☐ Fail |
| P1-SET-04 | Fix the confirmation and click **Next**. Click **Back**, then **Next** again. | The review shows Test Traders, 27 · Maharashtra, 2026-27, Asha Rao, asha. Values kept after Back. | ☐ Pass ☐ Fail |
| P1-SET-05 | Leave the demonstration box **unticked**. Click **Create workspace**. | "Creating…" shows briefly, then the dashboard opens with the toast "Test Traders is ready." | ☐ Pass ☐ Fail |
| P1-LOG-01 | Open the user menu (initials, top right) and choose **Sign out**. | The sign-in screen appears, with the cursor in Username. | ☐ Pass ☐ Fail |
| P1-LOG-02 | Sign in as `asha` with password `wrong password 1`. | "The username or password is incorrect." The password box clears. | ☐ Pass ☐ Fail |
| P1-LOG-03 | Sign in as `nobody` / `quiet ledger 42`. | The same message as P1-LOG-02 (it does not reveal which part was wrong). | ☐ Pass ☐ Fail |
| P1-LOG-04 | Sign in as `ASHA` / `quiet ledger 42`. | Signed in to Test Traders. | ☐ Pass ☐ Fail |
| P1-LOG-05 | With QIK Suite open, double-click QIKSuite.exe again. | A message says "QIK Suite is already running…". No second window. | ☐ Pass ☐ Fail |
| P1-LOG-06 | Close and reopen QIK Suite. | The sign-in screen appears (not the setup wizard). | ☐ Pass ☐ Fail |

The lockout test needs a second user; it's **P2-USR-09**.

### 3.2 Shell (Phase 1)

| ID | Steps | Expected result | Result |
|---|---|---|---|
| P1-SHL-01 | Press **Ctrl+B**. Then press **Ctrl+B** again. | The sidebar collapses to icons (labels hidden, tooltips show names), then expands. | ☐ Pass ☐ Fail |
| P1-SHL-02 | Collapse the sidebar, close and reopen QIK Suite, and sign in. | The sidebar is still collapsed. Expand it again. | ☐ Pass ☐ Fail |
| P1-SHL-03 | Press **Ctrl+K**, type `trial`, press **Enter**. | The palette opens with the cursor in the search box. "Trial balance" is selected; Enter opens the Trial balance page. | ☐ Pass ☐ Fail |
| P1-SHL-04 | Press **Ctrl+K**, use the Up/Down arrows, then press **Esc**. Open it again and click outside the panel. | The selection moves with the arrows. Esc closes it, and so does clicking outside. | ☐ Pass ☐ Fail |
| P1-SHL-05 | Press **Ctrl+Shift+L**. Close and reopen the app. | Switches to the dark theme with no white flashes. Still dark after reopening. Switch back with the moon/sun button in the top bar. | ☐ Pass ☐ Fail |
| P1-SHL-06 | Click the bell icon. Click it again. | A panel shows "You're all caught up…". The second click closes it. (Notifications are a placeholder.) | ☐ Pass ☐ Fail |
| P1-SHL-07 | Press **F1**. | The Help page lists the shortcuts. | ☐ Pass ☐ Fail |
| P1-SHL-08 | Press **Ctrl+Shift+G**. Click "Open destructive confirmation", then Cancel. Click the four toast buttons. | The component gallery opens. A dialog appears with focus on Cancel. "Last result: Dismissed" shows. Toasts appear bottom right and disappear after about 4 seconds. | ☐ Pass ☐ Fail |
| P1-SHL-09 | Press **Ctrl+1**. | The dashboard opens. A real company shows "—" for every figure and the note "No transactions yet…". | ☐ Pass ☐ Fail |
| P1-SHL-10 | On the dashboard, click **Receipt**, then go back and click **New invoice**. | Receipt opens Vouchers. New invoice opens a page saying it is planned for Phase 5. | ☐ Pass ☐ Fail |
| P1-SHL-11 | Click each item marked "Soon" in the sidebar. | Each says "Not available yet… planned for Phase N" and has a "Back to dashboard" button that works. | ☐ Pass ☐ Fail |
| P1-SHL-12 | Resize the window to about 1366×768. Then set Windows display scaling to 150% and look again. | No clipped buttons or overlapping text. Text stays sharp at 150%. | ☐ Pass ☐ Fail |

### 3.3 Company, branches, numbering (Phase 2)

Open **Settings** (sidebar) → **Company profile**.

| ID | Steps | Expected result | Result |
|---|---|---|---|
| P2-CMP-01 | Leave PAN empty. Enter GSTIN `27AAPFU0939F1ZV`, PIN code `411001`, City Pune, email `accounts@test.example`. Click **Save profile**. | "Company profile saved." After the reload, PAN shows `AAPFU0939F` (taken from the GSTIN). | ☐ Pass ☐ Fail |
| P2-CMP-02 | Change GSTIN to `27AAPFU0939F1ZW` (last letter changed). Save. | "GSTIN is not valid. Check the 15 characters, including the last check character." Nothing saved. | ☐ Pass ☐ Fail |
| P2-CMP-03 | Change GSTIN to `29AAGCB7383J1Z4` (a Karnataka GSTIN) with state still Maharashtra. Save. | "The GSTIN state code does not match the company's state." | ☐ Pass ☐ Fail |
| P2-CMP-04 | Set GSTIN back to `27AAPFU0939F1ZV` and PAN to `ABCPE1234F`. Save. | "The PAN inside the GSTIN does not match the company PAN." | ☐ Pass ☐ Fail |
| P2-CMP-05 | Set PAN to `ABCXE1234F` and clear the GSTIN. Save. | "PAN must be 10 characters, for example ABCDE1234F." (The fourth letter must be a valid holder type.) | ☐ Pass ☐ Fail |
| P2-CMP-06 | Restore PAN `AAPFU0939F` and GSTIN `27AAPFU0939F1ZV`. Enter PIN code `011001`. Save. | "PIN code must be 6 digits." Fix it to `411001` and save successfully. | ☐ Pass ☐ Fail |
| P2-CMP-07 | Click **Choose logo…** and pick a PNG or JPG smaller than 500 KB. | "Logo saved." A **Remove logo** button appears. (The logo isn't displayed anywhere yet.) | ☐ Pass ☐ Fail |
| P2-CMP-08 | Choose a picture larger than 1 MB. | "The logo must be 512 KB or smaller." | ☐ Pass ☐ Fail |
| P2-CMP-09 | Click **Remove logo**. | "Logo removed." The button disappears. | ☐ Pass ☐ Fail |
| P2-LOC-01 | **Branches and warehouses**: branch code `pnq`, name Pune branch, state Maharashtra, address MG Road. Click **Save branch**. | The branch appears with code `PNQ` (upper-cased) and status Active. | ☐ Pass ☐ Fail |
| P2-LOC-02 | Click **New**, enter code `PNQ` and name Duplicate. Save. | "Another branch already uses the code PNQ." | ☐ Pass ☐ Fail |
| P2-LOC-03 | Warehouse: code `W1`, name Main store, branch Pune branch. Click **Save warehouse**. | The warehouse appears with branch "Pune branch". | ☐ Pass ☐ Fail |
| P2-LOC-04 | Click "Active" on the warehouse row. Click it again. | It changes to "Inactive", then back to "Active". | ☐ Pass ☐ Fail |
| P2-NUM-01 | **Document numbering**: select "Quotation". Prefix `Q/{FY}/`, width `3`. Click **Save numbering**. | The success toast appears. The Format column for Quotation shows `Q/2026-27/001`. | ☐ Pass ☐ Fail |
| P2-NUM-02 | Set the width to `9` and save. | "Number width must be between 1 and 8 digits." | ☐ Pass ☐ Fail |
| P2-NUM-03 | Look at "Journal voucher", "Receipt voucher" and "Opening balance voucher". | Their formats are `JV/2026-27/0001`, `RV/2026-27/0001` and `OB/2026-27/001`. Don't change them; section 3.6 expects these. | ☐ Pass ☐ Fail |

### 3.4 Users and roles (Phase 2)

**Settings → Users** and **Roles and permissions.** Sign in again as `asha` whenever a test switches users.

| ID | Steps | Expected result | Result |
|---|---|---|---|
| P2-USR-01 | **Roles and permissions**: look at the list. Select Administrator. | Six roles are listed. Administrator's permission checkboxes are disabled, with the note that it always has every permission. | ☐ Pass ☐ Fail |
| P2-USR-02 | **Users**: select Asha. Change Role to Accountant and click **Apply**. | "Every company needs at least one active administrator." The role is unchanged. | ☐ Pass ☐ Fail |
| P2-USR-03 | With Asha selected, click **Remove access**, then confirm. | "You cannot remove your own access." | ☐ Pass ☐ Fail |
| P2-USR-04 | Add a user: Anu (username `anu`), temporary password `Welcome 2026!`, role Accountant. Then add the same way: `bina` Billing Operator, `ivan` Inventory Manager, `sam` Service Staff, `ravi` Read-only, `omar` Administrator. | Each one gives "User … created…". The list shows six new users, each with a "Temporary password" badge. | ☐ Pass ☐ Fail |
| P2-USR-05 | Add a user with username `ANU`. | "That username is already taken on this computer…" | ☐ Pass ☐ Fail |
| P2-USR-06 | Sign out. Sign in as `ravi` / `Welcome 2026!`. | The "Change password" screen appears before anything else, with a Sign out button. | ☐ Pass ☐ Fail |
| P2-USR-07 | Current password `Welcome 2026!`, new password `ravi password 1`. | "The password must not contain the username." | ☐ Pass ☐ Fail |
| P2-USR-08 | New password `my own phrase 9`, confirmed. Click **Change password**. | "Password changed." The dashboard opens. | ☐ Pass ☐ Fail |
| P2-USR-09 | Sign out. Sign in as `ravi` with a wrong password **five times**. Then try `my own phrase 9`. | Attempts 1–4: "The username or password is incorrect." Attempt 5: "This account is locked after 5 failed attempts. Try again in 15 minutes." The correct password is also refused while locked. | ☐ Pass ☐ Fail |
| P2-USR-10 | Sign in as `asha`. In **Users**, select Ravi, type temporary password `Welcome 2027!` and click **Reset**. Sign out and sign in as `ravi` / `Welcome 2027!`. | The reset succeeds and clears the lock. Ravi is forced to change the password again. Set it back to `my own phrase 9`. | ☐ Pass ☐ Fail |
| P2-USR-11 | Still signed in as Ravi (Read-only), look at the sidebar. | Visible: Dashboard; all Sales, Purchases and Inventory items; Chart of accounts, Vouchers, Ledgers, Trial balance, Financial statements; Expenses; CRM; Service jobs; Reports; Settings; Help. **Not** visible: Opening balances, Print centre. | ☐ Pass ☐ Fail |
| P2-USR-12 | As Ravi, open Settings. | Sections: General, Company profile, Branches and warehouses, Document numbering, Users, Roles and permissions, Financial years. **No** Audit trail. Company profile fields and Save are disabled. The Users page has no edit panel. "Create a company" is not shown. | ☐ Pass ☐ Fail |
| P2-USR-13 | As Ravi, open Vouchers. | No "New voucher" button. The editor is read-only. | ☐ Pass ☐ Fail |
| P2-USR-14 | Sign in as `anu` (choose `my own phrase 9` when asked) and look at the sidebar and Settings. | The sidebar includes Opening balances and Expenses, but no CRM, Service jobs or Print centre. Settings has Audit trail and Financial years, but **no** Users or Roles. | ☐ Pass ☐ Fail |
| P2-USR-15 | Sign in as `bina` (choose the password as before). | The sidebar shows only Dashboard, Sales (3), Inventory (2), CRM, Settings and Help. No Accounts items. | ☐ Pass ☐ Fail |
| P2-USR-16 | Sign in as `ivan`, then as `sam`. | Ivan: Purchases, Inventory, Reports. Sam: Inventory, CRM, Service jobs. Neither sees any Accounts items. | ☐ Pass ☐ Fail |
| P2-USR-17 | Sign in as `asha`. **Roles**: click **New role**, name it "User manager", and tick only Company: View, Users: View, Users: Manage, Users: Manage roles. Save. Add user `uma` with this role. Sign in as `uma` and set a password. | Uma sees Settings → Users and Roles. | ☐ Pass ☐ Fail |
| P2-USR-18 | As `uma`: create a new role "Sneaky" with Accounts: Manage ticked, and save. Then add a user with role Administrator. | The role: "You cannot grant permissions you do not have yourself." The user: "You cannot assign a role that has permissions you do not have yourself." | ☐ Pass ☐ Fail |
| P2-USR-19 | Sign in as `asha`. In Roles, select "User manager" and click **Delete role**. | "Move the users of this role to another role first." | ☐ Pass ☐ Fail |
| P2-USR-20 | Select Accountant in Roles and look for a Delete button. | No Delete button for built-in roles. | ☐ Pass ☐ Fail |
| P2-USR-21 | In Users, select `omar` and click **Remove access**, then confirm. Sign out and try `omar` / `Welcome 2026!`. | "Access removed." Sign-in fails with "Your account does not have access to any company…". | ☐ Pass ☐ Fail |

### 3.5 Audit trail and data isolation (Phase 2)

| ID | Steps | Expected result | Result |
|---|---|---|---|
| P2-AUD-01 | As `asha`: **Settings → Audit trail**. Select the newest "Updated company" entry from section 3.3. | The right panel shows Field / Old / New, for example Pincode "" → 411001 and Gstin "" → 27AAPFU0939F1ZV. User is asha. | ☐ Pass ☐ Fail |
| P2-AUD-02 | Find an "Updated user" entry for Ravi's password change. | PasswordHash shows "(hidden)" → "(changed)". The hash itself never appears. Entries "PasswordChanged" and "PasswordReset" exist. | ☐ Pass ☐ Fail |
| P2-AUD-03 | Click **Verify integrity**. | A green tick and "Audit trail intact: N entries checked, none changed or removed." | ☐ Pass ☐ Fail |
| P2-ISO-01 | **Settings → General → Create a company**: Second Co, Karnataka, 2026-27. Click **Create company**. | "Second Co created…" It appears in Your companies with role Administrator. | ☐ Pass ☐ Fail |
| P2-ISO-02 | Choose Second Co in the top-bar company list. | Toast "Switched to Second Co as Administrator." | ☐ Pass ☐ Fail |
| P2-ISO-03 | In Second Co, check Branches and warehouses, Users, Audit trail, Vouchers and Chart of accounts. | No PNQ branch or W1 warehouse. Users shows only Asha. The audit trail has only Second Co's entries. Vouchers is empty. The chart is the default (no customer ledgers). | ☐ Pass ☐ Fail |
| P2-ISO-04 | Switch back to Test Traders. | Everything from Test Traders is still there. | ☐ Pass ☐ Fail |

### 3.6 Accounting (Phase 3)

Stay in **Test Traders** as `asha`. Type dates exactly as shown (dd-mm-yyyy). Amounts go in the Debit or Credit box of each line.

| ID | Steps | Expected result | Result |
|---|---|---|---|
| P3-COA-01 | Open **Chart of accounts**. | A tree from Assets to Expenses with indentation, groups in bold, and "System" badges on accounts such as Cash, Sales and Retained Earnings. | ☐ Pass ☐ Fail |
| P3-COA-02 | Select the group "Sundry Debtors", click **New**. Code `1131`, name Sharma Traders, Type Receivable. Click **Save**. | "Account saved." Sharma Traders appears indented under Sundry Debtors, nature Asset. | ☐ Pass ☐ Fail |
| P3-COA-03 | Select "Sundry Creditors", click **New**. Code `2111`, name Kaveri Distributors, Type Payable. Save. | It appears under Sundry Creditors, nature Liability. | ☐ Pass ☐ Fail |
| P3-COA-04 | Click **New**, code `1131`, name Duplicate, group Sundry Debtors. Save. | "Account code 1131 is already used." | ☐ Pass ☐ Fail |
| P3-COA-05 | Select **Rent** and change its group to "Current Assets". Save. | "An account cannot move to a group of a different nature." | ☐ Pass ☐ Fail |
| P3-COA-06 | Select **Cash** and click **Deactivate**. | "System accounts are used by posting rules and cannot be deactivated." | ☐ Pass ☐ Fail |
| P3-OPN-01 | **Opening balances**, year 2026-27: Cash debit `25000`, Primary Bank Account debit `175000`, Owner's Capital credit `190000`. Click **Post opening balances** and confirm. | "Opening balances posted as OB/2026-27/001." (The 10,000 difference goes to Opening Balance Difference.) | ☐ Pass ☐ Fail |
| P3-VCH-01 | **Vouchers → New voucher**. Type Receipt, date `05-10-2026`, narration Cash sale. Lines: Cash debit 11800; Sales credit 10000; Output CGST credit 900; Output SGST credit 900. Before posting, watch the totals. | A red "Difference …" shows until the lines balance, then disappears. Both totals read **11,800.00**. | ☐ Pass ☐ Fail |
| P3-VCH-02 | Click **Post**. | "Posted as RV/2026-27/0001." The editor becomes read-only with a "Posted" badge. The list shows the voucher. | ☐ Pass ☐ Fail |
| P3-VCH-03 | With the posted voucher open, try to type in any field. | Nothing can be edited. There are no Save draft, Post or Delete buttons, only Reverse. | ☐ Pass ☐ Fail |
| P3-VCH-04 | New voucher: Payment, `06-10-2026`, Rent and charges. Rent debit 12000; Bank Charges debit 118; Primary Bank Account credit 12118. Post. | "Posted as PV/2026-27/0001." | ☐ Pass ☐ Fail |
| P3-VCH-05 | New: Contra, `07-10-2026`, Cash deposited. Primary Bank Account debit 20000; Cash credit 20000. Post. | "Posted as CV/2026-27/0001." | ☐ Pass ☐ Fail |
| P3-VCH-06 | New: Journal, `08-10-2026`, Salaries paid by owner. Salaries and Wages debit 5000; Owner's Capital credit 5000. Post. | "Posted as JV/2026-27/0001." | ☐ Pass ☐ Fail |
| P3-VCH-07 | New: Sales, `09-10-2026`, Credit sale. Sharma Traders debit 2360; Sales credit 2000; Output IGST credit 360. Post. | "Posted as SV/2026-27/0001." | ☐ Pass ☐ Fail |
| P3-VCH-08 | New: Purchase, `10-10-2026`, Stock purchase. Inventory debit 4000; Input CGST debit 360; Input SGST debit 360; Kaveri Distributors credit 4720. Post. | "Posted as PU/2026-27/0001." | ☐ Pass ☐ Fail |
| P3-VCH-09 | New: Journal, `11-10-2026`. Rent debit 1000; Owner's Capital credit 999.99. Click **Post**. | The live total shows "Difference 0.01". Posting fails with "…differ by 0.01. A voucher must balance." **Known behaviour:** it is left in the list as a draft. Select it and click **Delete draft**, then confirm. | ☐ Pass ☐ Fail |
| P3-VCH-10 | New: Journal, `11-10-2026`. Cash debit 100; Sales credit 100. Post. Then delete the resulting draft. | "Journal vouchers do not use cash or bank accounts. Use a payment, receipt or contra voucher." | ☐ Pass ☐ Fail |
| P3-VCH-11 | New: Payment, `11-10-2026`. Rent debit 100; Owner's Capital credit 100. Post. Then delete the draft. | "A payment voucher must credit a cash or bank account." | ☐ Pass ☐ Fail |
| P3-VCH-12 | New: Receipt, date `31-02-2026`. Post. | "Date: enter a date as dd-mm-yyyy, for example 01-04-2026." | ☐ Pass ☐ Fail |
| P3-VCH-13 | New: Receipt, date `31-03-2026` (before the books start). Cash 10 / Sales 10. Post. Then delete the draft. | "The company's books start on 01 Apr 2026. Choose a later date." | ☐ Pass ☐ Fail |
| P3-VCH-14 | New: Receipt, `11-10-2026`, Cash debit 3000; Sales credit 3000. Post. | "Posted as RV/2026-27/0002." | ☐ Pass ☐ Fail |
| P3-REV-01 | With RV/2026-27/0002 open, set Reversal date `12-10-2026`. Click **Reverse** and confirm. | "Reversed with RJ/2026-27/0001." The original shows "Reversed by RJ/2026-27/0001", and Reverse is now disabled. The list shows the reversal and a "Reversed" badge on the original. | ☐ Pass ☐ Fail |
| P3-LED-01 | **Ledgers**: account "1111 · Cash", from `06-10-2026` to `31-10-2026`. Click **Show**. | Opening balance **36,800.00 Dr**. Three lines: CV 20,000 credit, RV/0002 3,000 debit, RJ/0001 3,000 credit. Closing balance **16,800.00 Dr**. | ☐ Pass ☐ Fail |
| P3-LED-02 | Same account, from `01-03-2027` to `30-04-2027`. Show. | "Choose dates within one financial year (1 April to 31 March)." | ☐ Pass ☐ Fail |
| P3-TB-01 | **Trial balance** as of `31-10-2026`. Show. | Total debit and total credit both **2,23,880.00**. No "Does not balance" badge. Primary Bank Account debit **1,82,882.00**. Owner's Capital credit **1,95,000.00**. Sales credit **12,000.00**. Opening Balance Difference credit **10,000.00**. | ☐ Pass ☐ Fail |
| P3-FS-01 | **Financial statements**, from `01-04-2026` to `31-10-2026`. Show. | Total income **12,000.00**. Total expenses **17,118.00**. Net profit **-5,118.00** (a loss). | ☐ Pass ☐ Fail |
| P3-FS-02 | Look at the balance sheet on the same screen. | Total assets **2,06,762.00**. "Profit for the current year" **-5,118.00**. Total liabilities and equity **2,06,762.00**. No "Does not balance" badge. | ☐ Pass ☐ Fail |
| P3-REC-01 | Compare the screens. | Cash on the trial balance (16,800.00), the ledger closing balance (16,800.00) and the balance sheet (16,800.00) all match. Net profit appears identically in P&L and the balance sheet. | ☐ Pass ☐ Fail |
| P3-FY-01 | **Settings → Financial years**. Click **Open next year**. | "FY 2027-28 opened…" Both years are listed as Open. | ☐ Pass ☐ Fail |
| P3-FY-02 | Vouchers: Receipt, `05-04-2027`, Cash debit 1000; Sales credit 1000. Post. | "Posted as RV/2027-28/0001" (numbering restarts in the new year). | ☐ Pass ☐ Fail |
| P3-FY-03 | **Financial statements**, from `01-04-2027` to `30-04-2027`. | Net profit **1,000.00**. On the balance sheet: Cash **17,800.00**, Retained Earnings **-5,118.00** (last year's loss carried forward), totals equal at **2,07,762.00**. | ☐ Pass ☐ Fail |
| P3-FY-04 | Vouchers: create a Journal draft dated `13-10-2026` (Rent 10 / Owner's Capital 10), click **Save draft** only. Then in Financial years, select 2026-27 and click **Close selected year**, then confirm. | "1 draft voucher(s) are dated in FY 2026-27. Post or delete them before closing the year." | ☐ Pass ☐ Fail |
| P3-FY-05 | Delete that draft. Close 2026-27 again. | "FY 2026-27 closed." Status shows Closed with a date. | ☐ Pass ☐ Fail |
| P3-FY-06 | Try to post a Receipt dated `13-10-2026` (Cash 10 / Sales 10). Then delete the draft. | "FY 2026-27 is closed. Post in an open year or ask an administrator to reopen it." | ☐ Pass ☐ Fail |
| P3-FY-07 | Select 2026-27 and click **Reopen selected year**. | "FY 2026-27 reopened." | ☐ Pass ☐ Fail |
| P3-NUM-01 | **Settings → Document numbering**: Journal voucher prefix `JRN/{FY}/`, width 3. Save. Then post a Journal dated `13-10-2026`: Rent debit 500; Owner's Capital credit 500. | "Posted as JRN/2026-27/002." The counter continues from JV/2026-27/0001. | ☐ Pass ☐ Fail |
| P3-AUD-01 | **Settings → Audit trail**. | Entries "Posted …", "Reversed voucher RV/2026-27/0002 with RJ/2026-27/0001", and the financial year updates are listed. **Verify integrity** reports intact. | ☐ Pass ☐ Fail |

### 3.7 Formats and keyboard

| ID | Steps | Expected result | Result |
|---|---|---|---|
| GEN-FMT-01 | Look at amounts on the trial balance and the voucher list. | Indian grouping (1,82,882.00 and 2,23,880.00), two decimals, right-aligned, digits lined up in columns. | ☐ Pass ☐ Fail |
| GEN-FMT-02 | In a voucher date, type `5-4-2027` and also `05/04/2027`. | Both are accepted. | ☐ Pass ☐ Fail |
| GEN-KEY-01 | In a voucher, press **Tab** repeatedly from the first line's account. | Focus moves Account → Debit → Credit → Line note → Remove → next line's Account. A focus ring is always visible. | ☐ Pass ☐ Fail |
| GEN-KEY-02 | Do P1-LOG-01 to P1-SHL-03 using only the keyboard. | Possible without the mouse. | ☐ Pass ☐ Fail |

### 3.8 Optional: demonstration company (needs a reset)

| ID | Steps | Expected result | Result |
|---|---|---|---|
| P1-DEM-01 | Close the app, delete `%LocalAppData%\QIKSuite`, and run setup again with the demonstration box **ticked**. | The company switcher lists "Demonstration Company (sample data)" with a "Demo" badge. | ☐ Pass ☐ Fail |
| P1-DEM-02 | Switch to the demo company and open the Dashboard. | A "Demonstration data" badge and sample figures (for example Sales ₹18,42,350), charts and lists. | ☐ Pass ☐ Fail |
| P1-DEM-03 | While in the demo company, try **Settings → General → Delete** on it. Then switch to your real company and delete it. | First: "Switch to another company before deleting the demonstration company." Then a confirmation, and "Demonstration company deleted." | ☐ Pass ☐ Fail |

### What to send when something fails

1. **The test ID** (for example P3-VCH-09) and a **screenshot** of the whole window, including any error message. If an error box appears, copy its text too (Ctrl+C works on Windows message boxes).
2. **What you did:** the steps just before the failure, in order, and what you expected.
3. **The newest log file** from `%LocalAppData%\QIKSuite\logs\`, for example `qiksuite-20261012.jsonl`. Logs contain error types only, never passwords.
4. Your **Windows version** (Settings → System → About) and **display scaling** (Settings → System → Display).

## 4. Known gaps and risks

**Not verified**
- **Nothing has been run on Windows.** All WPF screens, keyboard handling, DPI scaling, fonts, themes, popups, dialogs and the single-instance check are unverified at runtime.
- The `win-x64` build was produced on macOS and never started.
- `scripts/build-windows.ps1` and the GitHub Actions workflow have never run.
- Narrator, high-contrast mode and Windows "reduce animations" are untested.
- **View models have no automated tests**: date parsing, amount parsing, voucher totals, navigation and the palette. `ViewBindingTests` checks that page-level bindings point to real members, but not bindings inside list rows or grid columns. Nor does it check behaviour.
- **Test-count composition:** 228 of the 502 tests are XAML design-rule checks, 30 are this guide's status-table checks and 1 is the binding check. The behavioural logic is covered by 243 tests.

**Not built (in phases already done)**
- **Dashboard:** no real figures; notifications are a static panel.
- **Lists:** no search, filter or paging on vouchers or the audit trail. No shared data-table features (column hide, saved layouts, totals row control).
- **Entry:** no date picker.
- **Output:** no printing, PDF or export anywhere.
- **Company:** the logo is stored but not displayed. Branches, warehouses and the discount limit aren't used by any workflow yet.
- **Accounting deferrals:** cash flow, bank reconciliation, cost centres, ageing and bill-wise allocation are deferred to Phase 8. Ledger statements are limited to one financial year.
- **Session:** no session timeout or auto-lock. A role change applies to other users at their next sign-in or company switch.
- **Phase 9 items:** no backup or restore, no import or export, no installer, no code signing.

**Known behaviour to be aware of**
- **Post on an invalid voucher leaves a draft.** The editor saves before posting. The draft stays open, so fixing it and pressing Post again updates the same draft; otherwise delete it.
- **Reports compute in memory.** That's fine at small scale but will need SQL aggregation for large data sets.
- **The audit chain** detects edits and deletions, but someone with file access could rewrite the whole chain. External anchoring is planned for Phase 9.
- **The "at most one reversal per voucher" rule** relies on SQLite treating empty values as distinct. A future SQL Server version needs a filtered index.

**Phases not started:** 4 (products and inventory), 5 (sales, purchases, GST), 6 (printing), 7 (expenses, CRM, service), 8 (reports, dashboard data, import/export, backup), 9 (security hardening, installer, release).

## 5. Phase checklist

| Phase | Status | Definition of done |
|---|---|---|
| 1. Foundation: solution, shell, design system, themes, DI, logging, database, login, setup | Done on macOS, not verified on Windows | Builds clean; setup, sign-in, shell and themes work on Windows 10/11; design-rule tests pass |
| 2. Companies, users, roles, permissions, audit trail, isolation | Done on macOS, not verified on Windows | Permissions enforced in services; audit records old/new values and verifies; tests prove one company can't read another's data |
| 3. Accounting engine | Done on macOS, not verified on Windows (cash flow, bank reconciliation, cost centres and ageing moved to Phase 8) | Every posted voucher balances; posted records are immutable except by reversal; years open/close; trial balance, ledgers, P&L and balance sheet reconcile in tests |
| 4. Products and inventory | Not started | Products, units, variants, warehouses, batches/serials; stock from a movement ledger; FIFO/weighted average costing per company; negative stock blocked unless allowed; inventory postings reconcile to the ledger |
| 5. Sales and purchases with GST | Not started | Quotation-to-invoice and purchase workflows post stock and accounts atomically; GST by place of supply with rates as dated data; totals recalculated in the Application layer; numbering and idempotency |
| 6. Printing | Not started | Printer settings and Print Centre with a verified test page; invoice, receipt and label templates; PDF export; barcode/QR |
| 7. Expenses, CRM, service | Not started | Expense vouchers with approvals; leads to customers; service jobs consuming spare parts that post inventory and accounts |
| 8. Reports, dashboard data, import/export, backup | Not started | Reports from real postings, exportable and printable; real dashboard figures; CSV/Excel import/export with formula-injection protection; encrypted, verifiable backup with a tested restore |
| 9. Security, full test pass, installer, release | Not started | Session timeout, optional encryption, DPAPI secrets; full Windows QA pass; Inno Setup or WiX installer; signed release script; KNOWN_LIMITATIONS.md |

**Keeping this guide current:** after every phase, update this guide before stopping:
- add the new screens to the status table;
- add test cases for every new function in the same format;
- re-classify anything that changed.

Never mark something FULLY FUNCTIONAL unless the code is wired to a real service and tests cover its logic. `Design/WorkingGuideTests` fails the build if a sidebar item or Settings section has no row in section 1.
