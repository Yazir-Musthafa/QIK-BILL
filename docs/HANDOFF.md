# Handoff: end of Phase 6 (11 October 2026)

Read this first, then:

- `PROJECT_BRIEF.md`: the full spec; there is no `AGENTS.md`;
- `PROGRESS.md`: the evidence for each claim;
- `ACCOUNTING_MAP.md`;
- `WORKING_GUIDE.md`: screen status and the Windows test script.

**Phase 6 (printing, labels, PDF, barcodes and QR) is complete on macOS. Phase 7 (expenses, CRM, service) is next; it was not started.** The owner has confirmed only that the app opens on Windows; nothing has been printed there.

**About "GPT-6 Astra":** the Phase 5 request said an agent called GPT-6 Astra had already worked on Phase 5. The repository had no trace of it: no commits, branches, stashes, worktrees or uncommitted files after the Phase 4 commit `926a72e`. Phase 5 was therefore built from that commit. The three inventory edge cases named in the request were investigated and fixed (see `PROGRESS.md`).

## State

- **Build:** clean, 0 warnings. `dotnet restore --locked-mode` passes, and a `win-x64` self-contained publish produces `QIKSuite.exe`.
- **Tests:** **806 passing** (`dotnet test tests/Nexora.Tests`): 504 behavioural, 270 XAML design-rule, 31 guide checks and 1 binding check.
- **Migrations:** `InitialWorkspace`, `CompaniesUsersRolesAudit`, `AccountingEngine`, `Inventory`, `Billing`, `Printing`. The last four contain SQLite immutability triggers.
- **Commits:** Phase 5 used the prefix `cla:` and Phase 6 uses `claude:` (the owner's requests).
- **New packages (Phase 6):** PDFsharp 6.2.4 (MIT) and ZXing.Net 0.16.11 (Apache-2.0), plus the bundled Noto Sans font (OFL). See `THIRD_PARTY_LICENSES.md` and `DECISIONS.md`.
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

## Phase 5 in brief

- **Inventory edge cases fixed first:**
  - `CostPool.Receive` settles negative weighted-average stock and returns the stock value; the difference is posted to Inventory Adjustments.
  - Physical counts check serials.
  - Reversal ignores reversed documents and checks the date.
  - Tests: `Inventory/InventoryEdgeCaseTests`.
- **Stock core:** `StockService.PostCoreAsync` and `ReverseCoreAsync` (internal) add stock documents to the caller's unit of work, with no voucher, and return each line's value.
  - The public `PostAsync` and `ReverseAsync` wrap them.
  - Billing uses the stock types Sale, SalesReturn, Purchase and PurchaseReturn, and refuses manual posting or reversal of those types.
- **Domain** (`Domain/Billing`):
  - `Party` with its own ledger;
  - `BillingDocument` and `BillingLine` (draft → posted; the posted state is immutable);
  - `BillingCancellation`, `Settlement`, `SettlementReversal`, `Allocation` (append-only);
  - `BillingSettings` (also the seed marker);
  - `BillingCalculator`, the single rounding and tax policy.
- **Services** (`Application/Billing`): `PartyService` (with the internal `OpenItems`), `BillingService` and `SettlementService`.
  - One writable unit of work under the posting lock per post, cancel or settlement.
  - Numbers come from `NumberAllocator`. The numbering keys are `invoice.tax`, `sales.credit-note`, `purchase.bill`, `purchase.debit-note`, `sales.receipt`, `sales.refund`, `purchase.payment` and `purchase.refund`.
  - Vouchers go through `ILedgerPoster` with an explicit voucher type; `LedgerPosting.Type` is new and optional.
- **Permissions:** `sales.*` and `purchases.*` are enforced in services:
  - create covers drafts, posting, parties and settlements;
  - cancel covers cancellation and reversal;
  - price override and the discount limit are checked on save and again at posting, as the user who posts;
  - cost figures need `inventory.view-cost`.
- **Desktop:**
  - `Views/Billing/BillingView.xaml` and `PartiesView.xaml`, each shared by a Sales/Purchase or Customers/Suppliers subclass. The templates are keyed on the abstract base view models.
  - The view-model files avoid the words "class", "record", "enum" and "interface" in comments, because `ViewBindingTests` splits class bodies on them.
- **Invariants to keep:**
  - stock value = Inventory ledger;
  - party ledger balance = open charges − open credits (tested);
  - posted billing documents never change.

## Known problems (Phase 5)

- **Not verified on Windows:** the four new screens have not been run.
- **Possible double counting of stock purchases:** recording bought goods both as a Stock receipt and as a Purchase bill counts them twice. The guide tells users to choose one.
- **Rounded returns:** when totals are rounded to rupees, returning a document in several parts can leave a few paise of round-off difference against the original.
- **Cancellation** needs the document to be the latest stock activity of its products. Otherwise users must make a return.
- **Prefix clashes:** numbering prefixes are not checked against other document types. Posting refuses a number already used by a stock document, but two billing types given the same prefix would share a number format.
- **Opening balances:** party opening balances entered by voucher are not open items.

## Phase 6 in brief

- **`Nexora.Printing` (net10.0, tested on macOS):**
  - `Barcodes/`: Code128, Ean13, ProductBarcode, QrCodes (ZXing), QrContentFormat.
  - `Layout/`: page items, PageSetup, PrintFonts (bundled Noto Sans; also the PDFsharp font resolver), TextFit, PageComposer (pagination, scale, roll height, page numbers).
  - `Templates/`: InvoiceTemplate (tax invoice or cash bill, credit note, purchase bill, debit note, delivery note, purchase order), ReceiptTemplate, StatementTemplate, TestPageTemplate, LabelTemplate.
  - `Pdf/PdfRenderer` and `EscPos/EscPosWriter` (experimental).
  - `PrintingService`: preferences, settings, label layouts, preview, print, PDF, history, test pages.
  - `IPrinterGateway` and `PrintingSeedContributor`.
- **Domain** (`Domain/Printing`): PrinterProfile (one per purpose), PrintSettings (the seed marker), LabelLayout, PrintRecord (append-only), PrinterTest (Sent → confirmed once).
- **Desktop** (`Services/Printing`):
  - `WindowsPrinterGateway`: System.Printing, XPS writer, winspool RAW;
  - `WpfDocumentBuilder`: draws the same page items;
  - `PrintActions`: preview window, print with recovery guidance, PDF save dialog.
- **Desktop screens:**
  - `Views/Printing/PrintCentreView` and `PrintPreviewWindow`;
  - print buttons on BillingView, PartiesView and ProductsView;
  - the scanner hook in `MainWindow.xaml.cs`, using `ScanDetector`; pages implement `IScanTarget`.
- **Rules to keep:**
  - templates print stored figures only;
  - a print is recorded only after the gateway accepts it;
  - a test page passes only when a person confirms it;
  - everything Windows-specific stays behind `IPrinterGateway`, so tests use `FakePrinterGateway`.
- **Binding-test pitfall:** in view-model files, strings and comments must not contain "record", "class", "enum" or "interface" followed by a word. The binding test splits class bodies on them. A toast saying "then record whether…" broke it in Phase 6.

## Known problems (Phase 6)

- **Nothing printed on Windows.** Expect fixes in WindowsPrinterGateway (ticket handling, custom roll sizes, error mapping), WPF text baseline and width differences, and the preview window.
- **Roll paper:** thermal rolls are sent as one custom-size page. Some drivers ignore custom sizes and feed a fixed length.
- **Preferences are per company:** printer names are per computer. Two computers in one company should use identical printer names, or the Windows default.
- **ESC/POS** is untested on any device.

## Next steps

1. **Test Phases 1–6 on Windows** with `WORKING_GUIDE.md` sections 3.1–3.11. Section 3.9 is the printer script (A4, 80 mm thermal, label printer, USB scanner). Report what came out on paper, not only what the screen said.
2. **Phase 5 leftovers.** These were not built; decide with the owner which belong in later phases:
   - quotations, orders and delivery notes (the Quotations page is still a placeholder);
   - dated GST rates, cess, reverse charge and bills of supply;
   - POS and barcode entry, additional charges, customer prices;
   - a billing settings screen.
3. **Data-table follow-ups:** per-column filters, and editing or deactivating units and tax categories.
4. **Printing follow-ups after the Windows run:** fix whatever the printer script shows. Consider per-computer printer preferences, a template designer and batch printing.
5. **Phase 7 (expenses, CRM, service)** is next.
