# Windows QA checklist: Phases 1–3

Phases 1–3 were built and tested on macOS. Nothing in this list has been performed yet. Run it on Windows 10 22H2 and Windows 11 before calling Phase 1 visually accepted. Record the date, machine, scaling and result for each item.

## Setup

1. Install the .NET SDK version in `global.json`.
2. Run `./scripts/build-windows.ps1`, or start `artifacts/publish/win-x64/QIKSuite.exe`.
3. Data is stored in `%LOCALAPPDATA%\QIKSuite\` (`qiksuite.db`, and `logs\qiksuite-*.jsonl`). Delete that folder to repeat first-run setup.

## First run and sign-in

- [ ] The first launch shows the setup wizard. Step 1 has focus in Company name.
- [ ] Each step shows inline errors for an empty name, no state selected, a short password and mismatched passwords. Next does not advance until they are fixed.
- [ ] Back keeps the values already entered. The review step shows them correctly.
- [ ] "Create workspace" opens the dashboard with a success toast. The UI stays responsive while the password is hashed (about 0.5 s).
- [ ] With the demonstration option ticked, the company switcher lists the demo company with a "Demo" badge.
- [ ] Close and reopen. The sign-in screen appears with focus in Username.
- [ ] A wrong password shows "The username or password is incorrect.", and the password box clears and takes focus.
- [ ] Five wrong passwords lock the account for 15 minutes, with a clear message.
- [ ] A second launch while QIK Suite is open shows "already running".

## Shell

- [ ] Ctrl+B collapses and expands the sidebar smoothly. Labels and group names hide when collapsed, and tooltips show labels.
- [ ] Ctrl+K opens the palette with focus in the search box. Typing filters, Up/Down moves, Enter runs, and Esc or clicking outside closes.
- [ ] The company switcher changes company, shows a toast and reloads the page. Sign out, then sign in, returns to the last company.
- [ ] Notifications and the user menu open and close, including clicking the button a second time.
- [ ] Ctrl+Shift+L and the toolbar button switch theme in place without losing state. The choice survives a restart.
- [ ] F1 opens Help. Ctrl+1 opens the Dashboard. Ctrl+Shift+G opens the component gallery.
- [ ] Every "Soon" module opens a page stating its phase. "Back to dashboard" works.
- [ ] Settings → Companies → Delete is offered only for the demo company. It is refused while the demo company is active, and works after switching.

## Visual

- [ ] Both themes: no hard-to-read text, no white flashes, popups and tooltips use theme colours, scroll bars are slim and themed.
- [ ] The dashboard shows "—" for a real company and sample figures with the "Demonstration data" badge for the demo company. Amounts are right-aligned with Indian grouping.
- [ ] The gallery renders every control and state. Disabled, error and focus states are visible.
- [ ] 1366×768 at 100%: the dashboard is usable with no clipped header actions, and the KPI grid stays at 4 columns.
- [ ] 1920×1080 at 125% and 150%, and 3840×2160 at 200%: text is crisp and the layout is not blurry (per-monitor DPI v2). Moving the window between monitors with different scaling re-renders it correctly.
- [ ] Segoe UI Variable renders on Windows 11, and Segoe UI on Windows 10. Icons render on both (Segoe Fluent Icons or MDL2 fallback).
- [ ] With Windows "Animation effects" off, no transitions play.
- [ ] Long company name (120 characters): truncates with an ellipsis in the switcher and does not break the layout.

## Keyboard and accessibility

- [ ] The whole flow (setup, sign-in, navigation, palette, theme, sign-out) works without a mouse.
- [ ] The Tab order is logical on every screen. The focus ring is always visible.
- [ ] Narrator reads field names, button names, login errors and toasts.
- [ ] High-contrast mode is at least readable. (Known gap: there is no dedicated high-contrast theme.)

## Robustness

- [ ] Make `%LOCALAPPDATA%\QIKSuite` read-only. Startup shows the "could not start" message, and the log records only the error type.
- [ ] The log files contain no passwords, usernames typed at sign-in, or database paths in error messages.

## Phase 2: companies, users, roles, audit

- [ ] Settings shows General, Company profile, Branches and warehouses, Document numbering, Users, Roles and permissions, Financial years and Audit trail for an administrator.
- [ ] Company profile: a valid GSTIN saves (example: `27AAPFU0939F1ZV` with state Maharashtra), a GSTIN for another state is refused with a clear message, the logo upload accepts PNG/JPEG up to 512 KB.
- [ ] Branches and warehouses: create, edit, deactivate; a warehouse can be linked to a branch; duplicate codes are refused.
- [ ] Numbering: change a prefix; the "next number looks like" column updates.
- [ ] Users: create a Read-only user with a temporary password. Sign out and sign in as them; the change-password screen appears first. After changing it, the sidebar shows only what Read-only can see, and Save buttons are disabled.
- [ ] As a Read-only user, Settings shows only the sections their role allows.
- [ ] Roles: tick and untick permissions, save, sign in as a member, and confirm the change applies. The Administrator role cannot be edited. A custom role in use cannot be deleted.
- [ ] Removing the last administrator's access, or changing their role, is refused.
- [ ] Create a second company from Settings → General; switch to it in the top bar. Its branches, users and audit trail are separate.
- [ ] Audit trail: changes show who, when, and old/new values; password changes show "(changed)", never the hash. "Verify integrity" reports intact.
- [ ] Change password from the user menu works.

## Phase 3: accounting

- [ ] Chart of accounts shows the tree with indented groups and System badges. Create a ledger under Sundry Debtors; create a group; deactivate a non-system ledger; a system account cannot be deactivated.
- [ ] Opening balances: enter cash and capital that do not balance; the posted voucher (`OB/…`) puts the difference in Opening Balance Difference.
- [ ] Vouchers: create a receipt (debit Cash, credit Sales). The difference indicator shows until it balances. Save draft, then Post; the number appears as `RV/2026-27/0001` and the editor becomes read-only.
- [ ] An unbalanced voucher shows the error and does not post. A journal using Cash is refused with a clear message.
- [ ] Reverse a posted voucher with a date: an `RJ/…` voucher appears, the original shows "Reversed", and Reverse is then disabled.
- [ ] Ledger: choose Cash and a date range in one financial year; opening balance, lines with running balance and closing balance show; a range spanning two years is refused with a message.
- [ ] Trial balance: totals match and no "Does not balance" badge appears.
- [ ] Financial statements: P&L net profit equals "Profit for the current year" on the balance sheet; the balance sheet totals match.
- [ ] Settings → Financial years: open the next year; close the current year (refused while drafts exist in it); posting into a closed year is refused; reopen.
- [ ] Amounts everywhere use Indian grouping (12,34,567.00) and right alignment.
- [ ] Keyboard: Tab moves account → debit → credit → note across voucher lines; Enter on Ledger/Trial balance runs Show.
