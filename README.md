# QIK Suite

A Windows desktop app for small Indian businesses: GST billing, purchases, inventory, double-entry accounts, expenses, CRM and service jobs, in one place, with printing, reports and backups.

> Version 0.10.0, a local release candidate. Major workflows have automated coverage; the screens still require manual Windows verification. Activation is managed inside the desktop app; no server or internet check is required. Please don't use it for real accounts or GST filing yet.

## What it does

- **Billing and purchases:** tax invoices, cash bills, credit notes, purchase bills and returns, with CGST/SGST/IGST worked out from the place of supply. Receipts and payments, with allocation to invoices.
- **Inventory:** products, variants, units, batches, expiry and serial numbers, several warehouses, and FIFO or weighted-average costing.
- **Accounting:** chart of accounts, vouchers, ledgers, trial balance, profit and loss, balance sheet, cash flow, cost centres and bank reconciliation. Sales, purchases and stock post to the books automatically.
- **Expenses, CRM and service:** expense approval, leads and follow-ups, and service job cards with spare parts and warranty.
- **Reports:** 30 reports (sales, GST, stock, ageing, profit and more), with export to Excel, CSV or PDF.
- **Dashboard:** live figures from posted data.
- **Printing:** A4 and thermal invoices, barcode labels and a print centre.
- **Data:** import and export of products, customers, suppliers, opening stock and accounts, plus encrypted, verifiable backups.
- **Users:** roles and permissions, an audit trail, and separate companies.

## Using it

1. Download the installer, `QIKSuite-Setup-<version>.exe`, from the latest successful **Actions** run (artifact `qiksuite-installer`) and run it. It installs for your Windows account and needs no administrator rights. Or build it yourself on Windows with Inno Setup 6 installed:
   ```powershell
   ./scripts/release.ps1 -Version 0.10.0
   ```
2. Choose **Register new customer** on the sign-in page. Enter your company details and choose your own username and password (`admin` is reserved). Registration saves the workspace but keeps all company pages locked. The supplier signs in on the same page with the special admin credentials, enters a reason and chooses **Activate / Reactivate**. After the admin signs out, use your registered account to sign in.
3. A typical day:
   - add products under **Inventory → Products** and customers under **Sales → Customers**;
   - raise invoices under **Sales → Sales invoices** and record receipts against them;
   - enter supplier bills under **Purchases → Purchase bills**;
   - check figures on the **Dashboard** and under **Insights → Reports**.
4. Turn on automatic backups under **Settings → Backup**, with a password, and keep a copy somewhere other than your computer.

For safety, QIK Suite signs you out after 15 minutes without use, when Windows locks, or when you press **Ctrl+Alt+L**.

One activation covers this Windows-user installation and all its staff logins. The admin panel lists registered accounts, activates or suspends the installation, and saves a history with reasons. Suspension locks company access immediately; reactivation requires a fresh customer sign-in. Activated installations work offline indefinitely, with no 30-day check or payment gateway. Management must be performed on that customer's PC, in the same Windows account.

GitHub wraps artifacts in ZIPs, but recipients only need the installer EXE inside. `scripts/download-installer.ps1 -RunId <id>` extracts and verifies it for you. Share that EXE directly.

Your data is stored in `%LOCALAPPDATA%\QIKSuite\`.

Shortcuts: **Ctrl+K** search, **Ctrl+B** sidebar, **Ctrl+Shift+L** light/dark theme, **Ctrl+Alt+L** lock, **F1** help.

## How it's built

- C# on **.NET 10** with **WPF** for the desktop interface (MVVM with CommunityToolkit.Mvvm).
- **SQLite** through Entity Framework Core. Posted records can't be edited: changes are reversals, enforced in code and by database triggers.
- The code is in layers:
  - `Domain`: business rules;
  - `Application`: use cases and permission checks;
  - `Accounting`: posting engine and statements;
  - `Infrastructure`: database and backups;
  - `Printing`: PDF via PDFsharp, barcodes via ZXing.Net;
  - `Reporting`: reports, Excel via the Open XML SDK, imports and the dashboard;
  - `Desktop`: the WPF app, including the local activation admin panel.
- Money is kept as exact decimals. Reports total in the database and are checked against the ledger.
- I built it in stages:
  1. foundation and login;
  2. companies and users;
  3. accounting;
  4. inventory;
  5. billing and GST;
  6. printing;
  7. expenses, CRM and service;
  8. reports and backups;
  9. security, the installer and the release pipeline.

  Each stage came with tests before moving on. GitHub Actions builds, tests and packages it on Windows, and starts, installs and uninstalls it on every push.

### Building and testing on any OS

The interface only runs on Windows, but the code builds and the tests run on macOS or Linux too:

```bash
dotnet restore Nexora.sln --locked-mode
dotnet build Nexora.sln
dotnet test tests/Nexora.Tests
```

## Licence

Third-party packages are MIT or Apache-2.0, and the bundled Noto Sans font is under the SIL Open Font License.

## Local activation administration

The supplier signs in as `admin` with the configured supplier password on the normal login page. The app ships a salted PBKDF2 password verifier, never a plaintext password. Admin permission is separate from company roles, expires after 15 minutes of inactivity, and is cleared on sign-out or Windows lock. Five incorrect admin passwords cause a persistent 15-minute lockout.

`local-activation.db` holds installation status, admin lockout counters and append-only activation history beside `qiksuite.db`. It has its own EF migration and is deliberately excluded from company backups, so restoring an older company backup cannot undo a suspension. Keep that file when moving the same installation's data; a different Windows account or PC starts pending and needs local approval. Existing 0.9.0 installations also need approval after upgrading; their company data is retained.

Local control depends on the customer PC and is bypassable by someone who modifies the program or its local security database. It does not provide remote enforcement. The former web server, signed offline grants and hosting configuration have been removed.

For developer builds, provide `QIK_ADMIN_PASSWORD_HASH_BASE64` (Base64 of the PBKDF2 verifier) to `build-windows.ps1`, or keep it in the ignored root `admin-build.props` as `QikAdminPasswordHashBase64`. GitHub Actions receives it from a private repository secret. No credential configuration is needed on the customer PC.
