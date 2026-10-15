# QIK Suite

A Windows desktop app for small Indian businesses: GST billing, purchases, inventory, double-entry accounts, expenses, CRM and service jobs, in one place, with printing, reports and backups.

> Version 0.9.0, a release candidate. Everything is built and covered by automated tests, but the screens are still being checked by hand on Windows. Please don't use it for real accounts or GST filing yet.

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
   ./scripts/release.ps1 -Version 0.9.0
   ```
2. The first time it starts, a short setup asks for your company details and creates an administrator login. You can add a demonstration company to try things out.
3. A typical day:
   - add products under **Inventory → Products** and customers under **Sales → Customers**;
   - raise invoices under **Sales → Sales invoices** and record receipts against them;
   - enter supplier bills under **Purchases → Purchase bills**;
   - check figures on the **Dashboard** and under **Insights → Reports**.
4. Turn on automatic backups under **Settings → Backup**, with a password, and keep a copy somewhere other than your computer.

For safety, QIK Suite signs you out after 15 minutes without use, when Windows locks, or when you press **Ctrl+Alt+L**.

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
  - `Desktop`: the WPF app.
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
