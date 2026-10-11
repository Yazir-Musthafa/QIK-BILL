# QIK Suite: working and testing guide

Last updated: **11 October 2026**, end of Phase 7. Each statement below comes from reading the code, a clean build and the automated tests on macOS.

**Windows:** the owner reports that the app **opens** on Windows (11 October 2026). No other screen has been confirmed on Windows, so everything else is "UI unverified at runtime" until the test script in section 3 is completed.

## How this guide was checked

| Check | Result |
|---|---|
| Clean build (`dotnet build Nexora.sln`, after deleting all `bin`/`obj`) | Succeeded, 0 warnings, 0 errors (end of Phase 7) |
| Locked package restore | Succeeded |
| Automated tests (`dotnet test tests/Nexora.Tests`) | **873 passed, 0 failed** (with this guide in place) |
| Of those, design-rule checks on XAML styling | 288 (one per XAML file per rule). They check styling discipline, not behaviour. |
| Of those, the guide's own status-table check | 31 (one per screen, plus one sanity check) |
| Of those, binding check | 1 test: every page-level XAML binding (about 700) points to a real view-model property or command |
| Logic tests (domain, services, database, accounting, inventory, billing, printing, expenses, CRM, service, table logic) | 553 (504 at the end of Phase 6, plus 49 Phase 7 tests in `tests/Nexora.Tests/Phase7/`) |
| Desktop view models (`src/Nexora.Desktop/ViewModels`) | **No automated tests.** They target Windows and can't be loaded by the test project, so date parsing, voucher-line totals, the command palette and navigation are untested. |
| Fixed at the start of Phase 5 (inventory edge cases) | (1) A receipt into negative weighted-average stock left value without quantity; the missing units are now settled and the cost difference goes to Inventory Adjustments. (2) A physical count could create a serial that was already in stock in another warehouse, or list a serial twice; both are refused. (3) A chain of stock documents could not be reversed newest first; already-reversed documents no longer block it, and a reversal can't be dated before the product's latest movement. Tests: `Inventory/InventoryEdgeCaseTests`. |
| Fixed during Phase 7 | Found by the new tests: deleting a draft invoice linked to a service job failed on the foreign key; the warranty account added on first use was invisible to the voucher service in the same operation; an attachment named only ".pdf" lost its extension; a lead's phone with +91 did not match the customer's 10-digit phone. Found while building the screens: Service Staff could not list customers to open a job (no Sales: View), so a name-and-phone pick list was added; and opening a service job's invoice draft in the Sales editor would have turned issued parts into ordinary goods, taking them out of stock a second time on posting. Both the service and the editor now refuse that. The Vouchers screen said "created by a stock document… under Stock" for every voucher made by a document; it now names the right place (sales or purchase document, receipt or payment, expense, service job). |
| Mutation checks (Phase 7) | Ten rules were broken on purpose, one at a time, and the tests were run: self-approval allowed, ineligible GST claimed, recurring expense generated twice, lead converted without the duplicate check, credit limit ignored, workflow allowing Received → Ready, warranty parts posted to Cost of Goods Sold, issued parts moving stock again on the invoice, attachment type not checked, and a service invoice draft re-saved as goods. Every one made at least one test fail. Two survived at first (the recurring duplicate needed both guards removed to reach the unique index; the attachment check had no test for a renamed file, which was then added). |
| Fixed during an earlier audit (Phase 3) | Tests added for service paths that had none (chart editing, warehouses, branch deactivation, numbering, logo, role change). In the voucher editor, a failed Post created a second draft on each retry; it now keeps editing the same draft and shows it in the list. The numbering column label "Next number looks like" was misleading and is now "Format (shown with number 1)". |

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
| Start | First-run setup wizard | FULLY FUNCTIONAL | Three steps (company, administrator, review) with inline validation. Creates the company, six roles, administrator membership, numbering rules, chart of accounts (58 accounts), first financial year, and optionally a demo company. Signs you straight in. | Nothing known | `WS`, `SetupValidatorTests`, `MigrationTests` | Not yet |
| Start | Sign-in | FULLY FUNCTIONAL | Username (any case) and password. Same message for an unknown user and a wrong password. Locks for 15 minutes after 5 failures. Returns to the last company you used. | No "forgot password" self-service; an administrator resets it | `WS`, `UserLockoutTests`, `Perm` | Not yet |
| Start | Forced password change / Change password (user menu) | FULLY FUNCTIONAL | Required after a temporary password; also available from the user menu. Checks the current password and the 10-character policy. | Nothing known | `Perm`, `AuditTrailTests` | Not yet |
| Shell | Sidebar, top bar, command palette, theme, company switcher, user menu | PARTIAL | Grouped sidebar collapses to icons (Ctrl+B) and is remembered. Ctrl+K palette searches pages and actions. Theme switch (Ctrl+Shift+L) is remembered. Company switcher changes company and role. Sidebar hides pages your role can't open. | Palette, sidebar and keyboard handling are view-model code with no automated tests. Only saving preferences and switching company are tested. | `WS` (preferences, switching), `CompanyIsolationTests` | Not yet |
| Shell | Notifications (bell) | PARTIAL | Opens a panel listing **your follow-ups** that are overdue or due today (assigned to you, or unassigned ones you created), with counts. Needs CRM: View; otherwise "You're all caught up". | Follow-ups are the only notifications. No low-stock, approval or payment notifications. The panel loads when opened; it does not refresh by itself. The panel code has no automated tests (the summary it shows is tested). | `Phase7/CrmTests` (`FollowUpAndTimelineTests`) | Not yet |
| Overview | Dashboard | PARTIAL | Layout, quick actions, demo company's labelled sample figures, honest "—" for real companies. Payment and Receipt quick actions open Vouchers. **My follow-ups** panel (Phase 7): your overdue and due-today follow-ups, for users with CRM: View. | **Shows no real figures** apart from follow-ups. Real companies always show "—", and the demo company shows fixed sample numbers. Real dashboard data is Phase 8. Four quick actions open placeholders. | `WS` (empty versus demo snapshot) | Not yet |
| Sales | Sales invoices | PARTIAL | **Invoices** list (status filter, search, totals) with a **preview** of the stored document: party and company GSTIN, place of supply, lines with HSN and GST rate, CGST/SGST/IGST, round off, outstanding, allocations, cost (with "View cost"). **Invoice editor**: customer or walk-in cash sale, cash/bank account, warehouse, lines with unit, rate (blank = selling price), discount % or amount, batch, serial; tax-inclusive option; round to rupees. **Save draft and calculate** (totals come from the service), **Save and post**, edit/post/delete drafts, **cancel** with a reason, **Return goods** (credit note against the invoice, partial or full). **Credit notes** list. **Receipts**: customer receipts and refunds with allocation to one or more documents (oldest first or typed), advances, reversal. Price override and discount limit enforced by the service. **Printing (Phase 6):** Preview, Print, Export PDF and Print on receipt printer for the selected document; Delivery note from an invoice; receipts print from the Receipts section. **Scan or type a code** in the editor (barcode, QR identifier or SKU, then Enter) to add a line; a scan with no text box focused does the same. **Phase 7:** customer-specific prices replace the selling price when the rate is left blank; a credit invoice that takes the customer over the **credit limit** asks for confirmation and posts only for users with "Sales: Override credit limit" (recorded in the audit trail); invoices can carry **service lines** (no stock) and **issued-part lines** from service jobs (no second stock movement). Drafts with those lines cannot be opened in the editor (post them as they are, or change them on the service job). | No fast POS entry, hold/resume, additional charges, salesperson, recurring invoices or payment reminders. Service lines can only be added by the service job screen, not typed into the editor. No quotations/orders/delivery notes. No dated GST rates, cess, reverse charge or bills of supply. View-model code has no automated tests. | `Billing/SalesTests`, `Printing/PrintCentreTests` (`DocumentPrintTests`, `ReprintTests`), `Billing/BillingCalculatorTests`, `Billing/PurchaseAndSettlementTests` (`SettlementTests`), `Billing/BillingSecurityTests`, `Inventory/InventoryEdgeCaseTests` | Not yet |
| Sales | Quotations | PLACEHOLDER | Says "planned for Phase 5" | Everything: quotations, sales orders and delivery notes were not built in Phase 5 (see section 4) | None | Not yet |
| Sales | Customers | FULLY FUNCTIONAL | List with outstanding, unapplied credit and ledger balance (totals row). Create and edit: name, phone, email, GSTIN (check character; state taken from it), state, address, city, PIN code, credit limit. A ledger is created under Sundry Debtors (renamed with the customer). Deactivate and reactivate. Open documents and settlements of the selected customer. **Statement of account** for a date range: Preview, Print, Export PDF (Phase 6). | The credit limit is enforced on posting credit sales invoices (Phase 7, see Sales invoices); it is not checked on drafts or receipts. Opening balances of existing customers are not open items (enter them as invoices or an opening voucher). | `Billing/PurchaseAndSettlementTests` (`PartyTests`, `SettlementTests`), `Billing/BillingSecurityTests`, `Printing/PrintCentreTests` (statements) | Not yet |
| Purchases | Purchase bills | PARTIAL | Same page layout as Sales invoices. **Bills**: supplier, supplier bill number (recorded once per supplier), lines with purchase rate (blank = purchase price), discount, new batch with expiry, serials; cash or credit. Posting receives the stock through the inventory engine at the taxable value and claims input CGST/SGST or IGST. **Debit notes** (purchase returns, partial or full) send goods back at book cost. **Payments**: supplier payments and refunds with allocation, advances, reversal. **Printing (Phase 6):** bills and debit notes print, preview and export to PDF; a draft bill can be printed as a **purchase order** (there is no purchase order workflow; the order carries no number). Payments print as payment vouchers. | No purchase orders, goods receipt notes, approvals, freight or other charges, ineligible input tax or reverse charge. View-model code has no automated tests. | `Printing/PrintCentreTests`, `Billing/PurchaseAndSettlementTests` (`PurchaseBillTests`, `SettlementTests`), `Billing/BillingSecurityTests` | Not yet |
| Purchases | Suppliers | FULLY FUNCTIONAL | As Customers, with the ledger under Sundry Creditors and balances shown as payable, and supplier statements. | As Customers. Ledgers created by hand under Sundry Creditors (for example in section 3.6) are not suppliers. | `Billing/PurchaseAndSettlementTests` (`PartyTests`), `Billing/BillingSecurityTests` | Not yet |
| Inventory | Products | FULLY FUNCTIONAL | List (filter, columns, totals, saved layout, show archived). Create and edit: SKU, name, description, category, brand, barcode (EAN-13/EAN-8 check digit, or text), QR identifier, HSN/SAC, tax category, unit, tracking (none, batch, serial; expiry with batch), purchase/selling/MRP, reorder level. Archive (only at zero stock). Variants (size, colour, model) with their own SKU and stock. Alternate units (for example 1 BOX = 12 PCS). Purchase price withheld by the service without "Inventory: View cost". **Labels (Phase 6):** preview and print labels of the selected product with a label layout; a scan with no text box focused selects the scanned product. **Warranty months** (0–120, Phase 7) used by service jobs; variants inherit it. | No product images. No CSV/Excel import or export (Phase 8). A product's unit and tracking are fixed after creation. | `Printing/LayoutTests` (`LabelLayoutTests`), `Printing/PrintCentreTests` (`LabelPrintTests`), `Inventory/InventoryDomainTests`, `Inventory/StockTests` (`ProductCatalogueTests`, `InventoryAccessTests`) | Not yet |
| Inventory | Stock | FULLY FUNCTIONAL | **Balances** by product, warehouse, state, batch and serial. **New document:** opening stock, receipt (credit a supplier, cash or bank), issue, adjustment in/out, transfer, physical count, state change (good, damaged, expired, returned, obsolete). Batch, expiry and serial entry; alternate units. **Documents** list with reversal (latest document of its products only). **Movement ledger.** **Valuation and reconciliation** (stock value against the Inventory ledger). **Low stock and expiry** alerts. Each value-changing document posts its accounting voucher in the same transaction. | No reservations (sales orders were not built). Stock moved by invoices, bills and returns appears here (types Sale, SalesReturn, Purchase, PurchaseReturn) but is cancelled from Sales or Purchases, not reversed here. No printing of stock documents (Phase 6). Stock dates can't go back before a product's last movement. | `Inventory/StockTests` (posting, costing, negative stock, transfers, states, counts, batches, serials, reversal, concurrency, reconciliation, isolation, immutability, permissions), `CostPoolTests`, `Inventory/InventoryEdgeCaseTests` | Not yet |
| Accounts | Chart of accounts | FULLY FUNCTIONAL | Tree with indentation and System badges. Create ledgers and groups (nature comes from the group). Rename, move within the same nature, deactivate and reactivate. Duplicate codes and names are refused. The type can't change once an account has postings. | Changing the Type of an existing group is silently ignored (groups stay groups) | `CoverageGapTests` (added in this audit), `ChartOfAccountsTemplateTests`, `AccountDomainTests`, `PostingTests` | Not yet |
| Accounts | Vouchers | FULLY FUNCTIONAL | List with quick filter, column chooser and saved layout. Editor for Journal, Payment, Receipt, Contra, Sales, Purchase, Expense, Income, Credit note, Debit note. Live debit/credit totals and difference. Save draft, Post (numbered), Delete draft, Reverse with a date. Posted vouchers open read-only. Manual vouchers can't use the Inventory account. Vouchers created by stock documents can only be reversed from Stock. | No date picker (type dd-mm-yyyy). No printing (Phase 6). Pressing Post on an invalid voucher first saves it as a draft; it stays open and in the list until you fix and post it or delete it. | `PostingTests`, `VoucherDomainTests`, `VoucherRulesTests`, `AccountingAccessTests` | Not yet |
| Accounts | Ledgers | FULLY FUNCTIONAL | Statement for any ledger account: opening balance, lines, running balance, closing balance | The range must fall inside one financial year (by design). No export or print. | `PostingTests`, `LedgerCalculatorTests` | Not yet |
| Accounts | Trial balance | FULLY FUNCTIONAL | Balances as of a date, totals, and a "Does not balance" badge if they ever differ | No group subtotals. No export or print. | `PostingTests`, `LedgerCalculatorTests` | Not yet |
| Accounts | Financial statements | FULLY FUNCTIONAL | Profit and loss (from–to) and balance sheet (as of "to"), with prior years' profit carried into Retained Earnings | No group subtotals. No cash flow statement (Phase 8). No export or print. | `PostingTests`, `LedgerCalculatorTests` | Not yet |
| Accounts | Opening balances | FULLY FUNCTIONAL | Enter debit/credit for asset, liability and equity ledgers. Posts one opening voucher (`OB/…`); any difference goes to Opening Balance Difference. | Corrections are made by reversing the opening voucher; there is no edit | `PostingTests` | Not yet |
| Accounts | Expenses | FULLY FUNCTIONAL | **Expenses** list with filters (date range, status, category, department) and totals of amount, GST and total; detail with lines, who created, submitted and decided, attachments, and actions by state. **Editor:** date, paid from (cash or bank), method, supplier, employee (user or name), branch, department, bill number, narration; lines with category, amount before GST, GST category and "Claim GST". GST and totals are recalculated by the service (CGST/SGST, or IGST when the supplier is in another state; unclaimable GST is added to the expense). **Approval:** draft → pending approval → approved or rejected (reason required) → posted. Approving needs "Expenses: Approve"; nobody approves their own expense unless Settings allows it. **Posting** goes through the voucher service (voucher type Expense); posted expenses cannot change and are corrected by **reversal** with a date and reason. **Attachments:** PDF, PNG or JPEG (checked by content, and the name must match), up to 5 MB, at most 5 per expense, stored with a checksum; open and remove (not once posted). **Categories** mapped to expense ledgers (optionally creating the ledger). **Recurring** templates with "Generate due expenses", which creates drafts only. **Totals** by category, date, branch, department or payment method. **Settings:** allow self-approval. | Expenses are always paid from cash or bank: an expense on credit to a supplier is a purchase bill. No mileage, per-diem or advance-settlement workflows. No approval levels by amount. The screen's view-model code has no automated tests. | `Phase7/ExpenseTests` (`ExpensePostingTests`, `ExpenseApprovalTests`, `RecurringExpenseTests`, `ExpenseAttachmentTests`, `ExpenseIsolationTests`) | Not yet |
| Relationships | CRM | FULLY FUNCTIONAL | **Leads and prospects:** name, organisation, phone, email, source, owner, notes, status (new, contacted, qualified, proposal; won or lost with a reason); call notes and other activities; the lead's timeline. **Convert to customer** in one step: if a customer with the same name, phone (last 10 digits) or email exists, nothing is created and the matches are offered to link to instead. **Follow-ups:** task, due date, assignee, customer, notes; open, overdue or done; "mine only"; also on the dashboard and in notifications. **Enquiries** linked to a lead or customer, with product, channel and status. **Customers:** outstanding, ledger balance, credit limit and credit available, purchase history and payment history read from billing (shown only with Sales: View); service history (with Service: View); contacts (one primary); **special prices** per product (needs "Sales: Override price"); communication history. | No e-mail, SMS or WhatsApp sending; communication history is what people record. No lead import or web forms. Contacts of suppliers exist in the service but have no screen. The screen's view-model code has no automated tests. | `Phase7/CrmTests` (`LeadTests`, `FollowUpAndTimelineTests`, `CreditLimitAndPricingTests`, `CrmIsolationTests`) | Not yet |
| Relationships | Service jobs | FULLY FUNCTIONAL | **Job cards:** customer, product, serial (with **Check warranty**: the latest posted sale of that serial plus the product's warranty months), item, accessories, fault, diagnosis, technician, estimated completion, parts warehouse. Numbered `JOB/…`. **Workflow** enforced by the domain: received → diagnosed → awaiting parts / in repair → ready → delivered, or cancelled (reason; no parts left on the job). Delivering a chargeable job with charges needs its invoice posted. **Spare parts** issued through the stock engine (negative-stock, batch and serial rules apply) with a voucher: Cost of Goods Sold for chargeable jobs, **Warranty and Service Costs** for warranty jobs. Unbilled parts can be returned at their cost. **Labour** lines. **Create service invoice:** a sales invoice draft with the parts (no second stock movement) and labour as service lines; post it in Sales invoices. Warranty jobs are not invoiced. **Print** the job card (preview, print, PDF) and the posted service invoice. **Maintenance schedules** that create follow-up tasks once per due date. **History** per customer or per serial. **Settings:** default labour SAC (empty unless you set it), reminder lead days. | The service invoice is posted from Sales invoices, not from the job. No customer approval of estimates, no SMS when ready, no technician workload view. The screen's view-model code has no automated tests. Job card printing is tested as PDF output only. | `Phase7/ServiceTests` (`ServiceWorkflowTests`, `ServicePartsAndBillingTests`, `ServiceLineBillingTests`, `MaintenanceTests`, `ServiceAccessTests`, `Phase7UpgradeTests`, `ServicePrintTests`) | Not yet |
| Insights | Reports | PLACEHOLDER | Says "planned for Phase 8" | Everything. The accounting reports are under Accounts. | None | Not yet |
| System | Print centre | PARTIAL | **Printers and test pages:** installed Windows printers with default flag, status, connection, and the paper sizes, colour and duplex support the driver reports ("Not reported" when it does not). Print a test page on A4, A5, 58 mm or 80 mm, then record **Came out correctly** or **There was a problem** (notes required); the result shows per printer. **Preferences** for invoices, receipts, reports and labels: printer, paper, orientation, margins, scale, font size, copies, and experimental ESC/POS for receipts. **Labels:** several products with quantities, layout, skip used labels on a sheet, preview, print, PDF; scanning adds products; create and edit label layouts. **Print history:** every print and PDF export with who, when, printer, copies and print number, plus test-page results. **Settings:** DUPLICATE marking, label QR content, invoice reference QR content, footer note. | **Nothing has been printed on Windows yet.** The Windows printer code (listing printers, reading capabilities, sending jobs, mapping printer errors, raw ESC/POS) and the preview window have no automated tests. ESC/POS is experimental and untested on any printer. No per-company default for "which preference prints which document" beyond the four purposes. | `Printing/PrintCentreTests` (`TestPrintTests`, `ReprintTests`, `LabelPrintTests`, `PrintAccessTests`), `Printing/LayoutTests`, `Printing/BarcodeTests` | Not yet |
| System | Settings | FULLY FUNCTIONAL | Section list showing only the sections your role allows; each section is listed below | See each section | See the sections | Not yet |
| System | Help | PARTIAL | Lists the keyboard shortcuts | Static text, with no service and no tests. Billing function keys are listed as future. | None | Not yet |
| Settings | Settings → General | FULLY FUNCTIONAL | Theme (light/dark). Your companies with your role in each. Create a company (you become its administrator). Delete the demonstration company. | Nothing known | `WS`, `Perm`, `CompanyIsolationTests` | Not yet |
| Settings | Settings → Company profile | FULLY FUNCTIONAL | Trade and legal name, address, city, PIN code, state, phone, email, PAN and GSTIN, with validation (GSTIN check character, state match, PAN match). Logo upload (PNG/JPEG up to 512 KB) and remove. | The logo is stored but **not shown anywhere yet** (it will be used on printed documents in Phase 6). GSTIN is checked offline only. | `CompanyProfileTests`, `TaxIdentifierTests`, `AuditTrailTests`, `CoverageGapTests` | Not yet |
| Settings | Settings → Branches and warehouses | FULLY FUNCTIONAL | Create and edit branches (code, name, state, address) and warehouses (code, name, optional branch). Activate and deactivate. Duplicate codes are refused. | Branches are not used by any workflow yet; warehouses are used by Stock, Sales and Purchases | `CoverageGapTests`, `CompanyIsolationTests` | Not yet |
| Settings | Settings → Document numbering | FULLY FUNCTIONAL | Prefix (with {FY}), number width and yearly reset for every document type: vouchers, tax invoices, credit and debit notes, purchase bills, receipts, payments, refunds and stock documents. Changes apply to the next posted document. | Quotation, sales order and purchase order numbering exists but nothing uses it yet. Changing a prefix to one that another document type uses is not prevented. | `NumberingTests`, `CoverageGapTests`, `PostingTests` | Not yet |
| Settings | Settings → Users | FULLY FUNCTIONAL | List users. Create a user with a temporary password. Give an existing account access. Change role. Remove and restore access. Reset password. Last-administrator and no-escalation rules. | Nothing known | `Perm`, `CoverageGapTests` | Not yet |
| Settings | Settings → Roles and permissions | FULLY FUNCTIONAL | Six built-in roles plus custom roles. Tick permissions by module, set a discount limit, delete unused custom roles. You can't grant what you don't hold. | Nothing known. The discount limit is enforced on sales lines (Phase 5). | `RoleTests`, `Perm` | Not yet |
| Settings | Settings → Inventory | FULLY FUNCTIONAL | Costing method (weighted average or FIFO, fixed once stock moves), allow negative stock (weighted average only), units (add), tax categories (add, reference rate). | Units and tax categories can be added but not edited or deactivated from this screen. Sales and purchases use each tax category's rate as entered here (the rate is stored on every posted line). There are no effective dates: a changed rate applies to documents posted after the change. | `InventoryRuleTests`, `StockTests`, `UpgradeTests` | Not yet |
| Settings | Settings → Financial years | FULLY FUNCTIONAL | List years with their draft counts. Open the next year. Close a year (refused while drafts are dated in it). Reopen. | Nothing known | `PostingTests`, `AccountingAccessTests` | Not yet |
| Settings | Settings → Audit trail | FULLY FUNCTIONAL | Newest 500 entries: who, when, action, summary, with quick filter and saved layout. Selecting an entry shows field-level old and new values. "Verify integrity" re-checks the hash chain. | No paging beyond 500 or date filter in the screen. No export. | `AuditTrailTests`, `AuditChainTests`, `CompanyIsolationTests` | Not yet |
| Hidden | Component gallery (Ctrl+Shift+G) | PARTIAL | Shows every control and state; the confirmation and toast buttons work | A developer reference screen by design; its sample buttons do nothing. No tests. | Design-rule tests only | Not yet |
| Shared | Data table (filter, columns, totals, saved layouts) | PARTIAL | Used on Products, Stock, Vouchers, Audit trail, Users, Trial balance, the inventory settings lists, Sales invoices, Purchase bills, Customers, Suppliers, Print centre, Expenses, CRM and Service jobs. Quick filter (every word must match), row count, show/hide columns, sort, resize, reorder, totals row, layout saved per user. | Layout merging, filtering, totals and per-user storage are tested (`TableLayoutTests`). The WPF control code itself (applying layouts, footer alignment, column menu) has no automated tests. No per-column filters. | `Preferences/TableLayoutTests` | Not yet |
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
| Products | `Views/Inventory/ProductsView.xaml` | `ViewModels/Inventory/InventoryViewModels.cs` (`ProductsViewModel`) | `Application/Inventory/InventoryCatalogueService.cs` |
| Stock | `Views/Inventory/StockView.xaml` | `StockViewModel` | `Application/Inventory/StockService.cs`, `Accounting/LedgerPoster.cs` |
| Settings → Inventory | `Views/Inventory/InventorySettingsView.xaml` | `InventorySettingsViewModel` | `InventoryCatalogueService` |
| Sales invoices, Purchase bills | `Views/Billing/BillingView.xaml` (one view for both) | `ViewModels/Billing/BillingViewModels.cs` (`SalesBillingViewModel`, `PurchaseBillingViewModel`, base `BillingViewModel`) | `Application/Billing/BillingService.cs`, `SettlementService.cs`, `StockService` (core posting), `LedgerPoster` |
| Customers, Suppliers | `Views/Billing/PartiesView.xaml` (one view for both) | `CustomersViewModel`, `SuppliersViewModel` (base `PartiesViewModel`) | `Application/Billing/PartyService.cs` |
| Print centre | `Views/Printing/PrintCentreView.xaml` | `ViewModels/Printing/PrintCentreViewModels.cs` (`PrintCentreViewModel`) | `Printing/PrintingService.cs`; Windows printers: `Desktop/Services/Printing/WindowsPrinterGateway.cs` |
| Print preview window (from any Print/Preview button) | `Views/Printing/PrintPreviewWindow.xaml` | `Services/Printing/PrintActions.cs` (`PrintPreviewModel`) | `PrintingService`, `Desktop/Services/Printing/WpfDocumentBuilder.cs` |
| Expenses | `Views/Expenses/ExpensesView.xaml` | `ViewModels/Expenses/ExpensesViewModels.cs` (`ExpensesViewModel`) | `Application/Expenses/ExpenseService.cs`, `ExpenseSeed.cs`, `LedgerPoster` |
| CRM | `Views/Crm/CrmView.xaml` | `ViewModels/Crm/CrmViewModels.cs` (`CrmViewModel`) | `Application/Crm/CrmService.cs`, `PartyService` (conversion, customer pick list), `BillingService` and `SettlementService` (customer view) |
| Service jobs | `Views/Service/ServiceView.xaml` | `ViewModels/Service/ServiceViewModels.cs` (`ServiceViewModel`) | `Application/Service/ServiceJobService.cs`, `StockService` (core posting), `BillingService` (invoice), `Printing/PrintingService.cs` (job card) |
| Dashboard follow-ups, notifications | `Views/DashboardView.xaml`, `Views/ShellView.xaml` | `DashboardViewModel`, `ShellViewModel` | `CrmService.MySummaryAsync` |
| Data table | `Themes/Controls.xaml` (style), `Controls/QikDataGrid.cs` | — | `Application/Preferences/TableLayout.cs` (`UserPreferenceService`) |

View and view-model paths are under `src/Nexora.Desktop/`. Services are under `src/Nexora.Application/`, `src/Nexora.Accounting/` or `src/Nexora.Printing/`.

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
   - a standard Indian chart of accounts (58 accounts and groups);
   - the first financial year, open;
   - inventory settings (weighted average, no negative stock), 10 units (PCS, NOS, BOX, PACK, DOZ, KG, G, L, ML, M), 4 tax categories, and numbering for 8 stock document types;
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
| P1-SHL-06 | Click the bell icon. Click it again. | A panel shows "You're all caught up. Follow-ups due today or overdue will appear here." The second click closes it. (Follow-ups are added in section 3.10.) | ☐ Pass ☐ Fail |
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
| P3-OPN-01 | **Opening balances**, year 2026-27: Cash debit `25000`, Primary Bank Account debit `175000`, Owner's Capital credit `190000`. Click **Post opening balances** and confirm. | Inventory is not in the list (opening stock is entered under Stock). "Opening balances posted as OB/2026-27/001." (The 10,000 difference goes to Opening Balance Difference.) | ☐ Pass ☐ Fail |
| P3-VCH-01 | **Vouchers → New voucher**. Type Receipt, date `05-10-2026`, narration Cash sale. Lines: Cash debit 11800; Sales credit 10000; Output CGST credit 900; Output SGST credit 900. Before posting, watch the totals. | A red "Difference …" shows until the lines balance, then disappears. Both totals read **11,800.00**. | ☐ Pass ☐ Fail |
| P3-VCH-02 | Click **Post**. | "Posted as RV/2026-27/0001." The editor becomes read-only with a "Posted" badge. The list shows the voucher. | ☐ Pass ☐ Fail |
| P3-VCH-03 | With the posted voucher open, try to type in any field. | Nothing can be edited. There are no Save draft, Post or Delete buttons, only Reverse. | ☐ Pass ☐ Fail |
| P3-VCH-04 | New voucher: Payment, `06-10-2026`, Rent and charges. Rent debit 12000; Bank Charges debit 118; Primary Bank Account credit 12118. Post. | "Posted as PV/2026-27/0001." | ☐ Pass ☐ Fail |
| P3-VCH-05 | New: Contra, `07-10-2026`, Cash deposited. Primary Bank Account debit 20000; Cash credit 20000. Post. | "Posted as CV/2026-27/0001." | ☐ Pass ☐ Fail |
| P3-VCH-06 | New: Journal, `08-10-2026`, Salaries paid by owner. Salaries and Wages debit 5000; Owner's Capital credit 5000. Post. | "Posted as JV/2026-27/0001." | ☐ Pass ☐ Fail |
| P3-VCH-07 | New: Sales, `09-10-2026`, Credit sale. Sharma Traders debit 2360; Sales credit 2000; Output IGST credit 360. Post. | "Posted as SV/2026-27/0001." | ☐ Pass ☐ Fail |
| P3-VCH-08 | New: Purchase, `10-10-2026`, Services purchase. Purchases (non-stock) debit 4000; Input CGST debit 360; Input SGST debit 360; Kaveri Distributors credit 4720. Post. (Stock purchases are recorded under Stock; see section 3.7.) | "Posted as PU/2026-27/0001." | ☐ Pass ☐ Fail |
| P3-VCH-09 | New: Journal, `11-10-2026`. Rent debit 1000; Owner's Capital credit 999.99. Click **Post**. | The live total shows "Difference 0.01". Posting fails with "…differ by 0.01. A voucher must balance." **Known behaviour:** it is left in the list as a draft. Select it and click **Delete draft**, then confirm. | ☐ Pass ☐ Fail |
| P3-VCH-10 | New: Journal, `11-10-2026`. Cash debit 100; Sales credit 100. Post. Then delete the resulting draft. | "Journal vouchers do not use cash or bank accounts. Use a payment, receipt or contra voucher." | ☐ Pass ☐ Fail |
| P3-VCH-11 | New: Payment, `11-10-2026`. Rent debit 100; Owner's Capital credit 100. Post. Then delete the draft. | "A payment voucher must credit a cash or bank account." | ☐ Pass ☐ Fail |
| P3-VCH-12 | New: Receipt, date `31-02-2026`. Post. | "Date: enter a date as dd-mm-yyyy, for example 01-04-2026." | ☐ Pass ☐ Fail |
| P3-VCH-13 | New: Receipt, date `31-03-2026` (before the books start). Cash 10 / Sales 10. Post. Then delete the draft. | "The company's books start on 01 Apr 2026. Choose a later date." | ☐ Pass ☐ Fail |
| P3-VCH-14 | New: Receipt, `11-10-2026`, Cash debit 3000; Sales credit 3000. Post. | "Posted as RV/2026-27/0002." | ☐ Pass ☐ Fail |
| P3-VCH-15 | New: Journal, `11-10-2026`. Inventory debit 100; Owner's Capital credit 100. Post. Then delete the draft. | "…Inventory is kept by stock documents. Record stock changes under Stock so stock value and the ledger stay equal." | ☐ Pass ☐ Fail |
| P3-REV-01 | With RV/2026-27/0002 open, set Reversal date `12-10-2026`. Click **Reverse** and confirm. | "Reversed with RJ/2026-27/0001." The original shows "Reversed by RJ/2026-27/0001", and Reverse is now disabled. The list shows the reversal and a "Reversed" badge on the original. | ☐ Pass ☐ Fail |
| P3-LED-01 | **Ledgers**: account "1111 · Cash", from `06-10-2026` to `31-10-2026`. Click **Show**. | Opening balance **36,800.00 Dr**. Three lines: CV 20,000 credit, RV/0002 3,000 debit, RJ/0001 3,000 credit. Closing balance **16,800.00 Dr**. | ☐ Pass ☐ Fail |
| P3-LED-02 | Same account, from `01-03-2027` to `30-04-2027`. Show. | "Choose dates within one financial year (1 April to 31 March)." | ☐ Pass ☐ Fail |
| P3-TB-01 | **Trial balance** as of `31-10-2026`. Show. | Total debit and total credit both **2,23,880.00**. No "Does not balance" badge. Primary Bank Account debit **1,82,882.00**. Owner's Capital credit **1,95,000.00**. Sales credit **12,000.00**. Opening Balance Difference credit **10,000.00**. | ☐ Pass ☐ Fail |
| P3-FS-01 | **Financial statements**, from `01-04-2026` to `31-10-2026`. Show. | Total income **12,000.00**. Total expenses **21,118.00**. Net profit **-9,118.00** (a loss). | ☐ Pass ☐ Fail |
| P3-FS-02 | Look at the balance sheet on the same screen. | Total assets **2,02,762.00**. "Profit for the current year" **-9,118.00**. Total liabilities and equity **2,02,762.00**. No "Does not balance" badge. | ☐ Pass ☐ Fail |
| P3-REC-01 | Compare the screens. | Cash on the trial balance (16,800.00), the ledger closing balance (16,800.00) and the balance sheet (16,800.00) all match. Net profit appears identically in P&L and the balance sheet. | ☐ Pass ☐ Fail |
| P3-FY-01 | **Settings → Financial years**. Click **Open next year**. | "FY 2027-28 opened…" Both years are listed as Open. | ☐ Pass ☐ Fail |
| P3-FY-02 | Vouchers: Receipt, `05-04-2027`, Cash debit 1000; Sales credit 1000. Post. | "Posted as RV/2027-28/0001" (numbering restarts in the new year). | ☐ Pass ☐ Fail |
| P3-FY-03 | **Financial statements**, from `01-04-2027` to `30-04-2027`. | Net profit **1,000.00**. On the balance sheet: Cash **17,800.00**, Retained Earnings **-9,118.00** (last year's loss carried forward), totals equal at **2,03,762.00**. | ☐ Pass ☐ Fail |
| P3-FY-04 | Vouchers: create a Journal draft dated `13-10-2026` (Rent 10 / Owner's Capital 10), click **Save draft** only. Then in Financial years, select 2026-27 and click **Close selected year**, then confirm. | "1 draft voucher(s) are dated in FY 2026-27. Post or delete them before closing the year." | ☐ Pass ☐ Fail |
| P3-FY-05 | Delete that draft. Close 2026-27 again. | "FY 2026-27 closed." Status shows Closed with a date. | ☐ Pass ☐ Fail |
| P3-FY-06 | Try to post a Receipt dated `13-10-2026` (Cash 10 / Sales 10). Then delete the draft. | "FY 2026-27 is closed. Post in an open year or ask an administrator to reopen it." | ☐ Pass ☐ Fail |
| P3-FY-07 | Select 2026-27 and click **Reopen selected year**. | "FY 2026-27 reopened." | ☐ Pass ☐ Fail |
| P3-NUM-01 | **Settings → Document numbering**: Journal voucher prefix `JRN/{FY}/`, width 3. Save. Then post a Journal dated `13-10-2026`: Rent debit 500; Owner's Capital credit 500. | "Posted as JRN/2026-27/002." The counter continues from JV/2026-27/0001. | ☐ Pass ☐ Fail |
| P3-AUD-01 | **Settings → Audit trail**. | Entries "Posted …", "Reversed voucher RV/2026-27/0002 with RJ/2026-27/0001", and the financial year updates are listed. **Verify integrity** reports intact. | ☐ Pass ☐ Fail |

### 3.7 Inventory (Phase 4)

Stay in **Test Traders** as `asha`. Section 3.3 created warehouse **W1 Main store**. Use dates in October 2026 **on or after 20-10-2026**, typed as dd-mm-yyyy. All figures assume the default **weighted average** costing.

| ID | Steps | Expected result | Result |
|---|---|---|---|
| P4-SET-01 | **Settings → Inventory**. | Weighted average selected; "Allow negative stock" unticked; 10 units; tax categories Exempt, GST 5%, GST 18%, GST 40%. | ☐ Pass ☐ Fail |
| P4-SET-02 | **Settings → Branches and warehouses**: add warehouse `SHOP`, name Shop floor (no branch). | It appears and is active. | ☐ Pass ☐ Fail |
| P4-PRD-01 | **Products → New product**. SKU `led-18w`, name LED panel 18 W, category Lighting, brand Brightline, barcode `4006381333932`, HSN `9405`, tax GST 18%, unit PCS, tracking None, purchase 310, selling 450, MRP 499, reorder 10. **Save product.** | "This looks like an EAN-13 barcode but its last (check) digit is wrong." | ☐ Pass ☐ Fail |
| P4-PRD-02 | Change the barcode to `4006381333931` and set selling to `550`. Save. | "The selling price cannot be more than the MRP." | ☐ Pass ☐ Fail |
| P4-PRD-03 | Set selling back to `450`. Save. | "Product saved." The list shows SKU `LED-18W`, On hand 0. | ☐ Pass ☐ Fail |
| P4-PRD-04 | New product with SKU `LED-18W` again. Save. | "SKU LED-18W is already used by another product." | ☐ Pass ☐ Fail |
| P4-PRD-05 | Select LED-18W. Under **Alternate units** choose BOX, type `12`, click **Add**. | "1 BOX = 12 base units" is listed. | ☐ Pass ☐ Fail |
| P4-PRD-06 | Create `SYRUP` (Cough syrup, unit NOS, tracking Batch, tick "Track expiry dates"), `PHONE-X` (unit PCS, tracking Serial), `FAN-1200` (unit PCS) and `TSHIRT` (unit PCS). | All four are saved. | ☐ Pass ☐ Fail |
| P4-PRD-07 | Select TSHIRT. Under **Add a variant**: size M, colour Red, variant SKU `TSHIRT-M-RED`. Click **Add variant**. | "Variant added." TSHIRT shows a "Variants" badge; the new row reads "… / M / Red". | ☐ Pass ☐ Fail |
| P4-STK-01 | **Stock → New document.** Receipt, date `20-10-2026`, warehouse Main store, credit account Kaveri Distributors. Line: LED-18W, quantity 10, unit cost 300. **Post document.** | "Posted SR/2026-27/0001. The accounting voucher was posted with it." | ☐ Pass ☐ Fail |
| P4-STK-02 | Receipt, same date and supplier: LED-18W, quantity 10, unit cost 330. Post. Then a third receipt: LED-18W, quantity 1, **unit BOX**, unit cost 3780. Post. | Two more receipts. **Balances**: LED-18W in Main store **32**. (The box became 12 PCS; the average cost stays 315.) | ☐ Pass ☐ Fail |
| P4-STK-03 | Issue, `20-10-2026`, Main store, debit account left blank: LED-18W 5. Post. Open **Documents**. | The issue shows Value change **-1,575.00** (5 × 315). | ☐ Pass ☐ Fail |
| P4-STK-04 | Issue LED-18W 40 from Main store. | "Line 1: only 27 of LED-18W is in this warehouse; 40 is needed." Nothing posted. | ☐ Pass ☐ Fail |
| P4-STK-05 | Transfer, Main store to Shop floor: LED-18W 7. Post. | "Posted ST/2026-27/0001." with **no** voucher sentence. Balances: Main store 20, Shop floor 7. | ☐ Pass ☐ Fail |
| P4-STK-06 | State change, Main store, from Good to Damaged: LED-18W 2. Post. Open **Products**. | LED-18W: On hand **27**, Available **25**. | ☐ Pass ☐ Fail |
| P4-STK-07 | Adjustment out, Main store, from state Damaged: LED-18W 2. Post. | Documents: Value change **-630.00**. Balances no longer show a Damaged row. | ☐ Pass ☐ Fail |
| P4-STK-08 | Physical count, Main store: LED-18W counted quantity 17. Post. | Documents: Value change **-315.00** (one unit short). Balances: Main store 17, Shop floor 7. | ☐ Pass ☐ Fail |
| P4-STK-09 | Receipt: SYRUP 25, unit cost 42.50, batch left empty. Post. | "…SYRUP is batch-tracked; enter the batch number." | ☐ Pass ☐ Fail |
| P4-STK-10 | Same line with batch `L1` and expiry **10 days from today**. Post. Open **Low stock and expiry**. | Posted. L1 is listed under "Expired or expiring within 30 days" with quantity 25. | ☐ Pass ☐ Fail |
| P4-STK-11 | Receipt: PHONE-X quantity 1, unit cost 9000, serial `SN-001`. Post. Repeat with the same serial. Then issue PHONE-X 1 with serial `SN-999`. | First posts. Second: "…serial SN-001 of PHONE-X is already in stock." Third: "…only 0 of serial SN-999 is in this warehouse…". | ☐ Pass ☐ Fail |
| P4-STK-12 | **Documents**: select the SYRUP receipt, reversal date `20-10-2026`, **Reverse selected document**, then confirm. | "Reversed with SX/2026-27/0001." The receipt shows "Reversed"; SYRUP balance is gone. | ☐ Pass ☐ Fail |
| P4-STK-13 | Select the first LED-18W receipt (SR/2026-27/0001) and reverse it. | "Later stock movements exist for these products. Reverse the later documents first, or post an adjustment instead." | ☐ Pass ☐ Fail |
| P4-STK-14 | **Vouchers**: open the journal whose narration starts "Receipt SR/2026-27/0001" and click **Reverse**. | "Voucher … was created by a stock document. Reverse that document under Stock instead, so stock and accounts stay in step." | ☐ Pass ☐ Fail |
| P4-STK-15 | Try a receipt dated `19-10-2026` for LED-18W. | "LED-18W has stock movements up to 20-10-2026. Post on or after that date." | ☐ Pass ☐ Fail |
| P4-VAL-01 | **Valuation and reconciliation**, as of `31-10-2026`, **Show**. | LED-18W 24 at unit cost 315 = **7,560.00**; PHONE-X 1 = **9,000.00**; total **16,560.00**. Stock value 16,560.00, Inventory in the ledger 16,560.00, Difference 0.00, with "Stock value equals the Inventory balance in the ledger." | ☐ Pass ☐ Fail |
| P4-VAL-02 | **Trial balance** as of `31-10-2026`. | Inventory debit **16,560.00**. Inventory Adjustments debit **2,520.00** (1,575 + 630 + 315). Kaveri Distributors credit **23,800.00** (4,720 from section 3.6 + 19,080 of stock receipts). Totals equal. | ☐ Pass ☐ Fail |
| P4-SET-03 | **Settings → Inventory**: choose FIFO and **Save policy**. | "The costing method cannot change after stock has moved…" A warning badge says the method is fixed. | ☐ Pass ☐ Fail |
| P4-ARC-01 | **Products**: archive LED-18W. Then archive FAN-1200. | LED-18W: "This product still has 24 in stock…". FAN-1200 is archived and disappears unless "Show archived" is ticked. Unarchive it again. | ☐ Pass ☐ Fail |
| P4-LOW-01 | Edit LED-18W: reorder level `30`. Save. Open **Stock → Low stock and expiry**. | LED-18W listed: available 24, reorder 30. | ☐ Pass ☐ Fail |
| P4-TBL-01 | On **Products**, type `led` in the filter, then clear it. Click **Columns** and untick Brand. Drag the "Unit" header to the left. Close and reopen QIK Suite, sign in, and open Products. | The filter shows 1 row and "1 row". Brand stays hidden and Unit stays moved after reopening. The totals row shows the On hand total. | ☐ Pass ☐ Fail |
| P4-PRM-01 | Sign in as `bina` (Billing Operator). Open Products, then Stock. | Purchase column empty; purchase price field disabled with the cost note. Stock has no "Valuation and reconciliation" section; the New document panel is disabled. | ☐ Pass ☐ Fail |
| P4-PRM-02 | Sign in as `ravi` (Read-only). | Products editor disabled; no New product button; New document disabled; Settings → Inventory controls disabled. | ☐ Pass ☐ Fail |
| P4-PRM-03 | Sign in as `ivan` (Inventory Manager, set a password if asked). Post an issue of LED-18W 1 from Shop floor. Open Settings → Inventory. | The issue posts. The policy section is disabled ("needs both Inventory: Manage and Company: Manage"). | ☐ Pass ☐ Fail |
| P4-PRM-04 | Sign in as `anu` (Accountant). Open Stock. | Valuation and reconciliation is visible; the New document panel is disabled (no Inventory: Adjust). | ☐ Pass ☐ Fail |
| P4-ISO-01 | Switch to **Second Co** (section 3.5). Open Products and Stock. | No products, balances or documents from Test Traders. | ☐ Pass ☐ Fail |

### 3.8 Sales, purchases and payments (Phase 5)

Continue in **Test Traders** as `asha` after section 3.7. Before you start, LED-18W has 17 in Main store at a unit cost of 315. Its selling price is 450 and its tax category GST 18%. Type the dates given in each step: the screens default to today, which may be earlier than the stock dates from section 3.7.

| ID | Steps | Expected result | Result |
|---|---|---|---|
| P5-CUS-01 | **Customers → New customer**: Meera Electricals, phone 9820012345, state 27 · Maharashtra. **Save**. | "Customer saved." The row shows Outstanding 0.00. | ☐ Pass ☐ Fail |
| P5-CUS-02 | New customer Kaveri Lights with GSTIN `29AAGCB7383J1Z5`. Save. | "The GSTIN is not valid. Check the state code, PAN and check character." | ☐ Pass ☐ Fail |
| P5-CUS-03 | Change the GSTIN to `29AAGCB7383J1Z4`, leave the state empty, save. | Saved; State shows Karnataka. | ☐ Pass ☐ Fail |
| P5-CUS-04 | **Chart of accounts**. | Meera Electricals (code `1130-0001`) and Kaveri Lights (`1130-0002`) are under Sundry Debtors. | ☐ Pass ☐ Fail |
| P5-SUP-01 | **Suppliers → New supplier**: Pune Wholesale, GSTIN `27AABCP5678B1ZE`. Save. | Saved with state Maharashtra; its ledger is under Sundry Creditors. | ☐ Pass ☐ Fail |
| P5-SAL-01 | **Sales invoices → Invoice editor**. Customer Meera Electricals, Payment Credit, date `21-10-2026`, warehouse Main store. Line: LED-18W, quantity 2, rate blank. Click **Save draft and calculate**. | Totals: taxable value **900.00**, GST **162.00**, round off 0.00, total **1,062.00**. Under Invoices the draft is listed as "(draft)", status Draft. Stock is still 17. | ☐ Pass ☐ Fail |
| P5-SAL-02 | Click **Save and post** and confirm. Open **Invoices** and select the invoice. | "Posted INV/2026-27/0001." Status Unpaid, outstanding 1,062.00. Preview: CGST 81.00, SGST 81.00, place of supply Maharashtra, cost of goods **630.00**. Stock: Main store 15. | ☐ Pass ☐ Fail |
| P5-SAL-03 | New invoice for Kaveri Lights, date `21-10-2026`: LED-18W 1. Save and post. | INV/2026-27/0002, place of supply Karnataka, **IGST 81.00**, CGST and SGST 0.00, total 531.00. | ☐ Pass ☐ Fail |
| P5-SAL-04 | New invoice dated `21-10-2026`: Payment **Cash**, account Cash, no customer, name `Walk-in`. LED-18W 1. Save and post. | INV/2026-27/0003, status **Paid**, outstanding blank. | ☐ Pass ☐ Fail |
| P5-SAL-05 | New invoice for Meera, `21-10-2026`: LED-18W quantity 100. **Save and post** and confirm. | "Line 1: only 13 of LED-18W is in this warehouse; 100 is needed." Nothing is posted; the document stays a draft. Delete the draft from the preview. | ☐ Pass ☐ Fail |
| P5-SAL-06 | New invoice for Kaveri Lights, `21-10-2026`: PHONE-X, quantity 1, rate `12000`, serial `SN-001`. Save and post. | INV/2026-27/0004 (no number was used up by P5-SAL-05). PHONE-X has no tax category, so GST is 0.00 and the total is 12,000.00; cost of goods 9,000.00. | ☐ Pass ☐ Fail |
| P5-RET-01 | Select INV/2026-27/0001, click **Return goods**. Set the date to `21-10-2026` and the quantity to 1. **Save and post**. | "Posted CRN/2026-27/0001." Credit notes lists it against INV/2026-27/0001, total 531.00, status Applied. INV/2026-27/0001 shows Partly paid, outstanding 531.00. Stock: Main store 14. | ☐ Pass ☐ Fail |
| P5-RET-02 | Return goods on INV/2026-27/0001 again with quantity 2. | "Line 1: only 1 of … can still be returned." | ☐ Pass ☐ Fail |
| P5-REC-01 | **Receipts**: type Customer receipt, Meera Electricals, account Cash, method Upi, amount `1000`, date `21-10-2026`, reference `UPI 4471`. Click **Allocate oldest first**, then **Post**. | INV/2026-27/0001 gets 531. "Allocated: 531.00 (the rest stays as an advance)". "Posted REC/2026-27/0001." The receipt shows Unapplied 469.00. **Customers**: Meera outstanding 0.00, unapplied 469.00, ledger balance −469.00. | ☐ Pass ☐ Fail |
| P5-REC-02 | Receipt from Kaveri Lights, amount `100`; type `600` against INV/2026-27/0002. Post. | "The allocations (600.00) are more than the amount (100.00)." Nothing is posted. | ☐ Pass ☐ Fail |
| P5-REC-03 | Select REC/2026-27/0001, type the reason `UPI failed` and the date `21-10-2026`, click **Reverse selected** and confirm. | The receipt shows Reversed. INV/2026-27/0001 is Partly paid again with outstanding 531.00; Meera's unapplied is 0.00. | ☐ Pass ☐ Fail |
| P5-CAN-01 | Select INV/2026-27/0001; reason `Wrong customer`, date `21-10-2026`; **Cancel document**. | "Returns have been posted against INV/2026-27/0001. Cancel them first, or return the rest instead." | ☐ Pass ☐ Fail |
| P5-CAN-02 | Select INV/2026-27/0004 (the phone); reason `Customer changed mind`, date `21-10-2026`; Cancel. | "INV/2026-27/0004 cancelled." It stays in the list with status Cancelled. SN-001 is back in stock under Stock → Balances. | ☐ Pass ☐ Fail |
| P5-PUR-01 | **Purchase bills → Bill editor**: supplier Pune Wholesale, reference `PW/881`, date `22-10-2026`. LED-18W quantity 10, rate `300`. Save draft and calculate, then Save and post. | Draft totals 3,000.00 + GST 540.00 = **3,540.00**. "Posted PB/2026-27/0001." Stock: Main store 24. | ☐ Pass ☐ Fail |
| P5-PUR-02 | Another bill from Pune Wholesale with reference `PW/881`. Save and post. | "Supplier bill PW/881 is already recorded as PB/2026-27/0001." Delete the draft. | ☐ Pass ☐ Fail |
| P5-PUR-03 | Select PB/2026-27/0001, **Return goods**, date `22-10-2026`, quantity 2. Save and post. | "Posted DBN/2026-27/0001." Total 708.00. Suppliers: Pune Wholesale outstanding **2,832.00**. | ☐ Pass ☐ Fail |
| P5-PAY-01 | **Payments**: Supplier payment, Pune Wholesale, account Primary Bank Account, method BankTransfer, amount `2832`, date `22-10-2026`, Allocate oldest first, Post. | "Posted PAY/2026-27/0001." PB/2026-27/0001 is Paid. Pune Wholesale balance 0.00. | ☐ Pass ☐ Fail |
| P5-VAL-01 | **Stock → Valuation and reconciliation**, as of `31-10-2026`. **Trial balance** as of the same date. | Difference 0.00. The trial balance totals are equal. Sales, Sales Returns, Output and Input CGST/SGST/IGST, Cost of Goods Sold and the party ledgers have balances. | ☐ Pass ☐ Fail |
| P5-PRM-01 | Sign in as `bina` (Billing Operator). New invoice for Meera: LED-18W 1 with discount % `10`. Save draft. Then rate `400` with no discount. | First: "Line 1: the discount is more than your limit of 5%." Second: "Line 1: you may not change the price of LED-18W (450.00)." The sidebar has no Purchases; the preview shows no cost of goods; there is no Cancel document section. | ☐ Pass ☐ Fail |
| P5-PRM-02 | Sign in as `ravi` (Read-only). Open Sales invoices and Customers. | Lists and previews open; the editor, receipts panel and customer editor are disabled. | ☐ Pass ☐ Fail |
| P5-PRM-03 | Sign in as `ivan` (Inventory Manager). | Purchase bills and Suppliers are available; Sales invoices and Customers are not in the sidebar. | ☐ Pass ☐ Fail |
| P5-ISO-01 | Switch to **Second Co**. Open Customers, Suppliers, Sales invoices and Purchase bills. | All empty. | ☐ Pass ☐ Fail |

### 3.9 Printing, labels and scanners (Phase 6)

Continue in **Test Traders** as `asha` after section 3.8. You need at least one real printer; the printer scripts below say which. Write down the printer model and connection (USB or network) for each script. **Nothing in this section has been run on Windows yet**, so record what actually happens on paper, even when the screen says the job was sent.

**General**

| ID | Steps | Expected result | Result |
|---|---|---|---|
| P6-SET-01 | Sidebar **System → Print centre**. | The section **Printers and test pages** lists the printers Windows has installed. The default printer has a "Default" badge. Paper sizes, colour and duplex show what the driver reports, or "Not reported". | ☐ Pass ☐ Fail |
| P6-SET-02 | **Preferences**. | Four cards: Invoices (A4), Receipts (Thermal80), Reports (A4), Labels (A4), each on "(Windows default printer)". | ☐ Pass ☐ Fail |
| P6-SET-03 | **Settings**: set Label QR content to `{colour}` and **Save settings**. | "Label QR format: {colour} is not a known field. Use: {sku}, {name}, {barcode}, {price}, {mrp}." | ☐ Pass ☐ Fail |
| P6-SET-04 | Set Label QR content back to `{sku}`, Invoice reference QR content to `INV {number} {total}`, Footer note to `Thank you. Goods once sold are taken back only with this bill.` Save. | "Print settings saved." | ☐ Pass ☐ Fail |

**A4 printer (laser or inkjet)**

| ID | Steps | Expected result | Result |
|---|---|---|---|
| P6-A4-01 | **Printers and test pages**: select the A4 printer, paper A4, **Print test page**. | Toast "Test page sent…". A panel asks whether it came out correctly. On paper: a frame on all four sides, a scale that measures **180 mm** with a ruler (±1 mm), text from 6 pt to 16 pt including the ₹ sign, a Code 128 barcode and a QR code. | ☐ Pass ☐ Fail |
| P6-A4-02 | Scan the test page's barcode and QR code with a USB scanner (into Notepad) or a phone. Then click **Came out correctly**; if anything was wrong, type what and click **There was a problem**. | The barcode reads the test code (TP…); the QR reads "QIK Suite test TP…". The printer row's "Last test" shows "Confirmed good" (or "Problem reported") with the time. | ☐ Pass ☐ Fail |
| P6-A4-03 | **Preferences → Invoices**: choose the A4 printer. **Save**. | "Invoices preferences saved." | ☐ Pass ☐ Fail |
| P6-A4-04 | **Sales invoices**: select INV/2026-27/0001. Click **Preview**. | A window shows the invoice: company name, GSTIN and state, the customer, place of supply, lines with HSN and GST rate, the tax summary, totals, amount in words, the reference QR labelled "Reference QR. Not an e-invoice QR.", the footer note, and "Authorised signatory". The totals equal those in the preview panel. | ☐ Pass ☐ Fail |
| P6-A4-05 | In the window, copies 1, **Print**. | Toast "Sent … to <printer>. Check the printer for the paper." The paper matches the preview; amounts line up in their columns; nothing is cut off at the margins. | ☐ Pass ☐ Fail |
| P6-A4-06 | Print the same invoice again from the preview panel's **Print**. Open **Print centre → Print history**. | The second paper has a boxed **DUPLICATE**. History shows two rows for the invoice: print no. 1 and 2, the second with Duplicate True, By Asha Rao. | ☐ Pass ☐ Fail |
| P6-A4-07 | **Export PDF**, save it to the desktop, open it. | The file name starts with "TAX INVOICE" (or "INVOICE" when the company has no GSTIN). The PDF looks like the paper, including ₹. History shows an Output "Pdf" row. | ☐ Pass ☐ Fail |
| P6-A4-08 | Select INV/2026-27/0002 and click **Delivery note**. | "DELIVERY NOTE" with goods, HSN and quantities, **no prices**, and a receiver's signature line. | ☐ Pass ☐ Fail |
| P6-A4-09 | **Purchase bills → Bill editor**: Pune Wholesale, LED-18W 5, rate 300, **Save draft and calculate**. Open **Bills**, select the draft, click **Purchase order**. | "PURCHASE ORDER" without a DRAFT banner, number "Not numbered". Delete the draft afterwards. | ☐ Pass ☐ Fail |
| P6-A4-10 | **Customers** → Meera Electricals → Statement from `01-04-2026` to `31-10-2026` → **Preview**, then **Print**. | "STATEMENT OF ACCOUNT" with opening balance, each posting with its voucher number, debit, credit and running balance (Dr/Cr), the closing balance and the open documents. | ☐ Pass ☐ Fail |
| P6-A4-11 | **Sales invoices → Receipts**: select REC/2026-27/0001 (reversed in P5-REC-03). **Preview**. | "RECEIPT" with a **REVERSED** banner, the amount in words and the method. | ☐ Pass ☐ Fail |
| P6-A4-12 | **Preferences → Invoices**: paper A5, font 8. Save. Preview INV/2026-27/0001. | The page is A5 and the layout still fits. Set it back to A4 and font 9. | ☐ Pass ☐ Fail |

**Thermal receipt printer, 80 mm (58 mm works the same way)**

| ID | Steps | Expected result | Result |
|---|---|---|---|
| P6-T80-01 | Install the printer's Windows driver. Set its paper to 80 mm (roll) in the driver's preferences. Open **Print centre**. | The printer is listed. Capabilities may say "Not reported"; that is acceptable. | ☐ Pass ☐ Fail |
| P6-T80-02 | Select it, paper **Thermal80**, **Print test page**. | A strip with a frame around the content, a **70 mm** scale, text including ₹, a barcode and a QR code. Confirm or report the problem. | ☐ Pass ☐ Fail |
| P6-T80-03 | **Preferences → Receipts**: choose the thermal printer, paper Thermal80. Save. **Sales invoices**: select INV/2026-27/0003 (the cash sale), **Print on receipt printer**. | A narrow "TAX INVOICE · CASH BILL" (or "CASH BILL") with lines, totals, GST summary, amount in words and the footer. Little blank paper after the end (some drivers add a fixed feed). | ☐ Pass ☐ Fail |
| P6-T80-04 | *Experimental, optional:* only if P6-T80-03 failed or printed badly. **Preferences → Receipts**: tick "Send as raw ESC/POS", type the printer's exact Windows name, save, print again. Untick afterwards. | Record exactly what printed. ₹ prints as "Rs." by design. Whether the QR code, barcode and cutter work depends on the model; none has been tested. | ☐ Pass ☐ Fail |

**Label printer (for example a 50 × 25 mm roll)**

| ID | Steps | Expected result | Result |
|---|---|---|---|
| P6-LBL-01 | In Windows, set the label printer's paper to the label size (50 × 25 mm). **Preferences → Labels**: choose the label printer. Save. | Saved. | ☐ Pass ☐ Fail |
| P6-LBL-02 | **Print centre → Labels**: layout "Roll label 50 × 25 mm". Add LED-18W, quantity 3. **Preview**, then **Print labels**. | Three pages of 50 × 25 mm, each with the product name, "₹ 450.00" and an EAN-13 barcode with 4006381333931 under it. Three labels print, one per label, without drifting. | ☐ Pass ☐ Fail |
| P6-LBL-03 | Scan each printed label with the USB scanner into Notepad. | Each reads 4006381333931. | ☐ Pass ☐ Fail |
| P6-LBL-04 | *A4 label sheets (3 × 8, 63.5 × 33.9 mm) on the A4 printer:* set Preferences → Labels back to the A4 printer. Layout "A4 sheet 3 × 8", add LED-18W 5 and FAN-1200 4, skip `2`. **Print labels** on plain paper and hold it against a label sheet. | Labels start at the third position. If they are offset, correct the layout's margins in the layout editor (**Save layout**) and repeat, then print on the sheet. | ☐ Pass ☐ Fail |
| P6-LBL-05 | Layout editor: set label width `70` with 3 columns, **Save layout**. | "The labels do not fit across the page: left margin + columns × width + gaps is wider than the page." | ☐ Pass ☐ Fail |
| P6-LBL-06 | **Settings**: Label QR content `{sku}|{price}`. **New layout**: name "QR 50 × 25", page and label 50 × 25, 1 column, 1 row, margins and gaps 0, Code **Qr**. Save. Preview labels of LED-18W and scan the QR with a phone. | The QR reads "LED-18W|450.00". | ☐ Pass ☐ Fail |
| P6-LBL-07 | **Products**: select LED-18W, layout "Roll label 50 × 25 mm", labels `2`, **Preview labels**. | Two label pages as in P6-LBL-02. | ☐ Pass ☐ Fail |

**USB barcode scanner (set to keyboard mode with Enter after each code)**

| ID | Steps | Expected result | Result |
|---|---|---|---|
| P6-SCN-01 | **Products**: click a row of the list (so no text box has focus), scan LED-18W's barcode. | LED-18W is selected. No text appears in any box. | ☐ Pass ☐ Fail |
| P6-SCN-02 | **Sales invoices → Invoice editor**: click in "Scan or type a code", scan LED-18W twice. | One line with LED-18W, quantity 2. | ☐ Pass ☐ Fail |
| P6-SCN-03 | Click an empty part of the editor (not a text box), scan FAN-1200's code (or type FAN-1200 into the code box and press Enter). | A FAN-1200 line is added. | ☐ Pass ☐ Fail |
| P6-SCN-04 | Click in the Narration box and scan a barcode. | The digits are typed into Narration only; no line is added (scans into a text box are left to that box). Clear the narration. | ☐ Pass ☐ Fail |
| P6-SCN-05 | Type `4006381333931` slowly into the code box and press Enter. Then scan a barcode that belongs to no product. | Typing works the same as scanning in the code box. The unknown code shows "No product has the code …". | ☐ Pass ☐ Fail |
| P6-SCN-06 | **Print centre → Labels**: click an empty area, scan LED-18W. | LED-18W is added to the label list (scanning again adds 1 to its quantity). | ☐ Pass ☐ Fail |

**Printer problems**

| ID | Steps | Expected result | Result |
|---|---|---|---|
| P6-ERR-01 | Switch the A4 printer off (or unplug it) and wait until Windows shows it as offline. Print an invoice. | "Printing did not complete" with "Could not print to …" and the advice "The printer is offline or disconnected…". Print history has **no** new row. (Some drivers queue the job instead of reporting offline: record what happened.) | ☐ Pass ☐ Fail |
| P6-ERR-02 | **Preferences → Invoices**: type printer `Missing printer`, save, print an invoice. | "Windows has no printer named "Missing printer"." with advice to check Printers & scanners. Choose the real printer again. | ☐ Pass ☐ Fail |
| P6-ERR-03 | **Printers and test pages**: with the printer still off, print a test page. | The confirmation panel does not appear; an error with advice shows. **Print history → Test pages** lists it as SendFailed. | ☐ Pass ☐ Fail |

**Permissions and isolation**

| ID | Steps | Expected result | Result |
|---|---|---|---|
| P6-PRM-01 | Sign in as `bina` (Billing Operator). Print an invoice from Sales invoices. | The print works. Print centre is not in the sidebar; Purchase bills is not either. | ☐ Pass ☐ Fail |
| P6-PRM-02 | Sign in as `ivan` (Inventory Manager). **Products → Print labels**. | Labels print. Print centre and Sales invoices are not in the sidebar. | ☐ Pass ☐ Fail |
| P6-ISO-01 | Switch to **Second Co**. Open Print centre → Print history and Preferences. | No history from Test Traders; its own four default preferences. | ☐ Pass ☐ Fail |

### 3.10 Expenses, CRM and service (Phase 7)

Continue in **Test Traders** as `asha` after section 3.9. Type the dates given; use **23-10-2026 or later**, because earlier sections moved stock up to 22-10-2026. Where a figure depends on earlier sections (stock on hand, part costs, invoice numbers), the expected result says "goes up by" or "the next number" instead of an exact value. Users `anu` (Accountant), `bina` (Billing Operator), `sam` (Service Staff) and `ravi` (Read-only) come from section 3.4.

**Expenses**

| ID | Steps | Expected result | Result |
|---|---|---|---|
| P7-EXP-01 | **Expenses → Categories → New**: name `Shop rent`, expense ledger Rent, default GST 18%. **Save**. | "Category saved." It is listed against Rent. | ☐ Pass ☐ Fail |
| P7-EXP-02 | New category `Courier`, tick "Create a new ledger with this name under the group below", group Indirect Expenses, no default GST. Save. Open **Chart of accounts**. | Saved. A ledger **Courier** is under Indirect Expenses. | ☐ Pass ☐ Fail |
| P7-EXP-03 | **Expense editor**: date `23-10-2026`, paid from Cash, method Cash, supplier Pune Wholesale, department `Admin`, bill no. `R-101`. Line 1: Shop rent, amount `10000`, GST 18%, Claim GST ticked. **Add line**: Courier, `500`, GST 18%, Claim GST **unticked**. **Save draft**. | "Draft saved. GST and totals were recalculated." Detail: Shop rent 10,000 with CGST 900 and SGST 900 (Pune Wholesale is in Maharashtra); Courier 500 with GST 90 not claimable. **Total 12,390.00.** Status Draft. | ☐ Pass ☐ Fail |
| P7-EXP-04 | With the draft selected, **Attach a receipt**: a PDF under 5 MB. Then attach a `.txt` file renamed to `.pdf`. Then a PNG picture renamed to `.pdf`. | First: "Attachment added." and it is listed. Second: "Only PDF, PNG and JPEG files can be attached." Third: "The file's content is … but its name ends in ".pdf"…". **Open** shows the first file. | ☐ Pass ☐ Fail |
| P7-EXP-05 | Click **Submit for approval**. Then click **Approve** (still as `asha`). | "Submitted for approval." Status Pending approval. Approve: "You cannot approve or reject an expense you created or submitted…". | ☐ Pass ☐ Fail |
| P7-EXP-06 | Sign in as `anu`. Open Expenses, select the expense, **Approve**. Sign in as `asha` again, select it, **Post**. | "Approved." then "Expense posted." Number **EXP/2026-27/0001**, status Posted. No Edit, Delete or attachment buttons. | ☐ Pass ☐ Fail |
| P7-EXP-07 | **Trial balance** as of `31-10-2026`. | Courier debit **590.00**. Rent, Input CGST and Input SGST are each higher by 10,000, 900 and 900 than before this section. Cash is lower by 12,390. Totals equal. | ☐ Pass ☐ Fail |
| P7-EXP-08 | Select EXP/2026-27/0001: reason `Entered twice`, date `23-10-2026`, **Reverse**. Open the trial balance again. | "Expense reversed." Status Reversed. Courier is 0.00 and Cash is back to its earlier balance. | ☐ Pass ☐ Fail |
| P7-EXP-09 | New expense dated `24-10-2026`, Courier `200`, no GST. **Save and submit for approval.** Sign in as `anu`: **Reject** with the reason box empty, then with `No receipt`. | Empty: "Enter the reason for rejecting." Then "Rejected and sent back."; status Rejected with the reason shown. As `asha` the expense can be edited and submitted again. | ☐ Pass ☐ Fail |
| P7-EXP-10 | **Recurring → New**: name `Monthly rent`, Shop rent, `10000`, every Monthly, next due `01-10-2026`, paid from Cash, GST 18%. Save. Set "Generate drafts due up to" to `30-11-2026` and click **Generate due expenses**. Click it again. | "Recurring expense saved." Then "Created 2 draft(s). They still need approval and posting." The second click: "Nothing is due." The Expenses list shows two drafts dated 01-10-2026 and 01-11-2026; nothing was posted. | ☐ Pass ☐ Fail |
| P7-EXP-11 | **Totals**: group by Category, **Show**. Then by Payment method. | One row per category with count, amount, GST and total. The reversed expense is not counted. | ☐ Pass ☐ Fail |
| P7-EXP-12 | **Settings** (inside Expenses): tick "Allow people to approve expenses they created or submitted", Save. Submit one of the recurring drafts and approve it yourself. Untick the setting and save. | The approval works while the setting is on. | ☐ Pass ☐ Fail |
| P7-EXP-13 | Sign in as `ravi` (Read-only), then as `bina`. | Ravi: Expenses lists and details open; the editor, categories and recurring panels are disabled. Bina: no Expenses in the sidebar. | ☐ Pass ☐ Fail |
| P7-EXP-14 | Switch to **Second Co** and open Expenses. | No expenses, categories or recurring templates from Test Traders. | ☐ Pass ☐ Fail |

**CRM**

| ID | Steps | Expected result | Result |
|---|---|---|---|
| P7-CRM-01 | **CRM → Leads and prospects → New lead**: Ritu Sharma, organisation Sharma Interiors, phone `9876500011`, source Referral, owner Asha Rao. **Save lead**. | "Lead saved." Listed with status New. | ☐ Pass ☐ Fail |
| P7-CRM-02 | With Ritu selected: activity Call, text `Asked for the LED panel price list`, **Record**. Set status Qualified, **Set status**. | "Recorded." The timeline shows the call with your name. "Status changed." | ☐ Pass ☐ Fail |
| P7-CRM-03 | **Follow-ups → New follow-up**: `Send price list to Ritu`, due **today**, assignee Asha Rao. Save. Add another, `Call Meera about payment`, due **yesterday**, customer Meera Electricals. Open the **Dashboard** and click the **bell**. | Both listed; the second shows state Overdue. The dashboard's "My follow-ups" shows both. The bell: "1 overdue and 1 due today. Open CRM → Follow-ups to work on them." | ☐ Pass ☐ Fail |
| P7-CRM-04 | Select the overdue follow-up, **Mark done or reopen**. Tick "Include done". | "Done." It disappears, then reappears with state Done when "Include done" is ticked. | ☐ Pass ☐ Fail |
| P7-CRM-05 | Select Ritu Sharma, **Convert to customer**. Open **Sales → Customers**. | "Ritu Sharma is now a customer." The lead shows Converted (tick "Show won and lost"). Ritu Sharma is a customer with phone 9876500011 and a ledger under Sundry Debtors. | ☐ Pass ☐ Fail |
| P7-CRM-06 | New lead `Meera Electricals`, phone `+91 98200 12345`. Save, then **Convert to customer**. Choose Meera Electricals under the matches and click **Link to this customer**. | "A customer that looks like Meera Electricals already exists…". Linking: "Meera Electricals linked to Meera Electricals." Customers still has only one Meera Electricals. | ☐ Pass ☐ Fail |
| P7-CRM-07 | **Enquiries → New enquiry**: customer Meera Electricals, subject `Bulk LED order`, product LED-18W, channel Call, status Open. Save. | "Enquiry saved." Listed with "From" Meera Electricals. | ☐ Pass ☐ Fail |
| P7-CRM-08 | **Customers**: select Meera Electricals. | Outstanding, ledger balance and credit limit match Sales → Customers. Purchase history lists her invoices; payment history her receipts. Communication history shows activity recorded for her. | ☐ Pass ☐ Fail |
| P7-CRM-09 | Under Contacts add `Ramesh`, role `Purchase manager`, phone `9820011111`, primary ticked. Save contact. | "Contact saved." Listed. | ☐ Pass ☐ Fail |
| P7-CRM-10 | Under Special prices choose LED-18W, price `400`, **Save price**. In **Sales invoices** start an invoice for Meera with LED-18W 1 and the rate left blank, **Save draft and calculate**. Delete the draft. | "Customer price saved." The draft line's rate is **400.00** (not the 450 selling price). | ☐ Pass ☐ Fail |
| P7-CRM-11 | **Sales → Customers**: set Meera's credit limit to `1000`, save. New **credit** invoice for Meera dated `24-10-2026`: LED-18W 5. **Save and post**, and choose **Cancel** in the credit-limit question. Then post it again and confirm. | The question names the limit (1,000.00), the receivable after this invoice and the amount over. Cancel leaves it a draft. Confirming posts it. **Settings → Audit trail** has a "CreditLimitOverridden" entry. | ☐ Pass ☐ Fail |
| P7-CRM-12 | Sign in as `bina` (Billing Operator). Post a credit invoice for Meera of LED-18W 1 and confirm the credit-limit question. | Refused: "…does not allow this. Ask an administrator for the "Override credit limit" permission." The invoice stays a draft. Delete it. | ☐ Pass ☐ Fail |
| P7-CRM-13 | Sign in as `sam` (Service Staff) and `ravi` (Read-only). Open CRM. | Both can view leads, follow-ups and enquiries but not change them (editors disabled). Sam's Customers section shows names and phones without purchase or payment history. | ☐ Pass ☐ Fail |
| P7-CRM-14 | Switch to **Second Co** and open CRM. | No leads, follow-ups, enquiries or contacts from Test Traders. The bell shows "You're all caught up…". | ☐ Pass ☐ Fail |

**Service jobs**

| ID | Steps | Expected result | Result |
|---|---|---|---|
| P7-SRV-01 | **Products**: edit PHONE-X, warranty months `12`. Save. **Sales invoices**: cash sale dated `24-10-2026` to Meera Electricals: PHONE-X 1, serial `SN-001`, rate `12000`. Save and post. | Both saved; the invoice gets the next INV number. | ☐ Pass ☐ Fail |
| P7-SRV-02 | **Service jobs → New job card**: received `25-10-2026`, customer Meera Electricals, serial `SN-001`, click **Check warranty**. | "Under warranty until 24-10-2027 (invoice INV/…, sold 24-10-2026)." Product changes to PHONE-X. | ☐ Pass ☐ Fail |
| P7-SRV-03 | Item `Smartphone`, accessories `Charger`, fault `Does not charge`, technician Sam, estimated completion `28-10-2026`, parts warehouse Main store. **Save job card**. | "Job card saved." The list shows **JOB/2026-27/0001**, charge **Warranty**, status Received. | ☐ Pass ☐ Fail |
| P7-SRV-04 | Open the "Move to" list. Move to Diagnosed, then In repair. | Only Diagnosed and Cancelled are offered from Received. Each move shows "Job JOB/2026-27/0001 is now …". | ☐ Pass ☐ Fail |
| P7-SRV-05 | Date `25-10-2026`. Issue part LED-18W, quantity 1. Look at Stock → Balances, then the trial balance as of `31-10-2026`. | "Part issued from stock." Main store LED-18W goes down by 1. **Warranty and Service Costs** has a debit equal to the part's cost on the job; Inventory is lower by the same amount. | ☐ Pass ☐ Fail |
| P7-SRV-06 | Click **Create service invoice**. | "This is a warranty job: there is nothing to invoice. Its parts cost was posted to Warranty and Service Costs." | ☐ Pass ☐ Fail |
| P7-SRV-07 | Issue PHONE-X 1 with serial `SN-001` on this job. Then LED-18W 999. | First: "…only 0 of serial SN-001…" (it was sold). Second: "…only … of LED-18W is in this warehouse…". Nothing is issued. | ☐ Pass ☐ Fail |
| P7-SRV-08 | Move Ready, then Delivered. | Delivered. The job leaves the list unless "Show delivered and cancelled" is ticked; when shown, it has no Edit or Move controls. | ☐ Pass ☐ Fail |
| P7-SRV-09 | New job card: received `26-10-2026`, Kaveri Lights, item `Ceiling fan`, fault `Noisy`, no serial, Main store. Save. Move to Diagnosed and In repair. Issue LED-18W 1. Add labour `Repair and testing`, quantity 1, rate `500`, SAC as advised by your accountant, tax GST 18%. **Create service invoice**. | Charge **Chargeable**. "Service invoice draft created. Review and post it in Sales invoices." The job shows the draft invoice number. | ☐ Pass ☐ Fail |
| P7-SRV-10 | **Sales invoices**: select the draft and click **Edit**. | "This draft has service or issued-part lines, which this editor cannot change…". | ☐ Pass ☐ Fail |
| P7-SRV-11 | Back in the job: Move Ready, then Delivered. | "Raise and post the invoice for this chargeable job before delivering it." | ☐ Pass ☐ Fail |
| P7-SRV-12 | Post the draft from Sales invoices. Check Stock → Balances. Back in Service jobs, Move Delivered. | The invoice has two lines (the part and the labour) with **IGST** (Kaveri is in Karnataka). LED-18W stock does **not** go down again. Delivered works. | ☐ Pass ☐ Fail |
| P7-SRV-13 | New job for Meera, `27-10-2026`, item `Table lamp`. Diagnosed, In repair. Issue LED-18W 1. Select the part, **Return selected part**, confirm. Then Move to Cancelled with reason `Customer withdrew`, confirm. | "Part returned to stock." Stock goes back up by 1. The job is cancelled with the reason shown. (Cancelling before the return: "Return the issued parts to stock before cancelling the job.") | ☐ Pass ☐ Fail |
| P7-SRV-14 | Select JOB/2026-27/0001 (tick "Show delivered and cancelled"). **Preview job card**, **Print job card**, **Save as PDF**. On the Kaveri job, **Print invoice**. | The job card shows the company, customer, item, serial, fault, warranty date, parts and signature lines; the second print is marked DUPLICATE (if enabled in Print centre). The invoice preview shows the service invoice. | ☐ Pass ☐ Fail |
| P7-SRV-15 | **Maintenance → New schedule**: Meera Electricals, item `Inverter servicing`, every 6 months, next due **3 days from today**, assignee Sam. Save. **Create due reminders**. Click it again. Open CRM → Follow-ups (untick "Mine only"). | "Schedule saved." Then "1 follow-up task(s) created." Second click: "No reminders are due." The follow-up is listed, assigned to Sam. | ☐ Pass ☐ Fail |
| P7-SRV-16 | **History**: serial `SN-001`, **Show**. Then customer Meera Electricals with the serial cleared. | First: JOB/2026-27/0001. Second: the phone and lamp jobs. | ☐ Pass ☐ Fail |
| P7-SRV-17 | **Settings**: default SAC left empty, reminder days `10`. Save. | "Service settings saved." | ☐ Pass ☐ Fail |
| P7-SRV-18 | Sign in as `sam`. Create a job, move it and issue a part. Then sign in as `bina`. | Sam: works, but there is no **Create service invoice** button. Bina: no Service jobs in the sidebar. | ☐ Pass ☐ Fail |
| P7-SRV-19 | Switch to **Second Co** and open Service jobs. | No jobs, schedules or history from Test Traders. | ☐ Pass ☐ Fail |
| P7-VAL-01 | **Stock → Valuation and reconciliation** and the **trial balance**, both as of `31-10-2026`. | Difference 0.00; trial balance totals equal. | ☐ Pass ☐ Fail |

### 3.11 Formats and keyboard

| ID | Steps | Expected result | Result |
|---|---|---|---|
| GEN-FMT-01 | Look at amounts on the trial balance and the voucher list. | Indian grouping (1,82,882.00 and 2,23,880.00), two decimals, right-aligned, digits lined up in columns. | ☐ Pass ☐ Fail |
| GEN-FMT-02 | In a voucher date, type `5-4-2027` and also `05/04/2027`. | Both are accepted. | ☐ Pass ☐ Fail |
| GEN-KEY-01 | In a voucher, press **Tab** repeatedly from the first line's account. | Focus moves Account → Debit → Credit → Line note → Remove → next line's Account. A focus ring is always visible. | ☐ Pass ☐ Fail |
| GEN-KEY-02 | Do P1-LOG-01 to P1-SHL-03 using only the keyboard. | Possible without the mouse. | ☐ Pass ☐ Fail |

### 3.12 Optional: demonstration company (needs a reset)

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
- **Only startup has been seen on Windows** (owner report, 11 October 2026: "it opens"). Every screen, keyboard handling, DPI scaling, fonts, themes, popups, dialogs and the single-instance check remain unverified at runtime.
- The `win-x64` build was produced on macOS and never started.
- `scripts/build-windows.ps1` and the GitHub Actions workflow have never run.
- Narrator, high-contrast mode and Windows "reduce animations" are untested.
- **View models have no automated tests**: date parsing, amount parsing, voucher totals, navigation and the palette. `ViewBindingTests` checks that page-level bindings point to real members, but not bindings inside list rows or grid columns. Nor does it check behaviour.
- **Test-count composition:** 288 of the 873 tests are XAML design-rule checks, 31 are this guide's status-table checks and 1 is the binding check. The behavioural logic is covered by 553 tests.

**Not built (in phases already done)**
- **Dashboard:** no real figures apart from your follow-ups; low stock is shown under Stock instead. Notifications show follow-ups only.
- **Lists:** the shared data table gives quick filter, column choice, totals and saved layouts. There are no per-column filters and no paging beyond what each screen loads.
- **Entry:** no date picker.
- **Output:** no printing, PDF or export anywhere.
- **Company:** the logo is stored but not displayed. Branches aren't used by any workflow yet.
- **Accounting deferrals:** cash flow, bank reconciliation, cost centres, ageing and bill-wise allocation are deferred to Phase 8. Ledger statements are limited to one financial year.
- **Session:** no session timeout or auto-lock. A role change applies to other users at their next sign-in or company switch.
- **Phase 9 items:** no backup or restore, no import or export, no installer, no code signing.

**Known behaviour to be aware of**
- **Post on an invalid voucher leaves a draft.** The editor saves before posting. The draft stays open, so fixing it and pressing Post again updates the same draft; otherwise delete it.
- **Reports compute in memory.** That's fine at small scale but will need SQL aggregation for large data sets.
- **The audit chain** detects edits and deletions, but someone with file access could rewrite the whole chain. External anchoring is planned for Phase 9.
- **The "at most one reversal per voucher" rule** relies on SQLite treating empty values as distinct. A future SQL Server version needs a filtered index.

**Inventory (Phase 4) specifics**
- **Cost and dates:** cost is pooled per product across all warehouses. Stock dates can't go before a product's last movement.
- **Reversal:** a stock document can be reversed only while it is the latest activity for its products; otherwise post an adjustment.
- **States and values:** quantities are tracked per warehouse, state and batch, but value is tracked per product. Transfers and state changes carry no value.
- **Negative stock:** possible only with weighted average. With no stock left, issues use the last unit cost, which can distort the average until the next receipt.
- **Tax rates:** sales and purchases use the tax category rates as entered (stored on each posted line). The seeded rates were not checked against current GST law; check them under Settings → Inventory before real use.
- **Not built:**
  - stock reservations (sales orders were not built);
  - product images;
  - CSV/Excel import/export (Phase 8);
  - printing of stock documents and labels (Phase 6);
  - editing or deactivating units and tax categories from the screen;
  - per-column filters in the data table.
- **Older databases:** if an older database already has manual vouchers posted to Inventory, the reconciliation will show a difference. That is expected, and it is explained on screen.

**Sales, purchases and payments (Phase 5) specifics**
- **Tax:** one rate per product from its tax category: intra-state is split equally into CGST and SGST, inter-state is IGST. Place of supply is the party's state, or the company's state when the party has none. **Not built:** dated rate history, cess, reverse charge, exempt or nil-rated distinctions, bills of supply, ineligible input tax, GST registers and returns data. Nothing here is a statement of GST compliance.
- **Rounding:** each line's tax is rounded to the paisa, and the total may be rounded to rupees through Round Off. A return of part of a line is proportional; the last return of a line takes exactly what remains. When totals are rounded, a document returned in several parts can differ from the original by the round-off paise.
- **Dates:** invoices and bills move stock, so they follow the stock date rule (not before a product's last movement).
- **Cancelling** needs the document to be the latest stock activity of its products, and no receipts, payments, refunds or returns against it. Otherwise make a return (credit or debit note).
- **Returns** are always made against a posted invoice or bill. A credit-mode return settles its original first; anything beyond that stays as unapplied credit, to refund or apply later.
- **Purchase returns** send goods out at book cost (the costing method's value). The difference from the amount charged back to the supplier goes to Inventory Adjustments.
- **Balances:** party balances come from the ledger. Outstanding and unapplied amounts come from documents and allocations. They agree unless someone posts manual vouchers to a party ledger.
- **Allocations** cannot be undone one by one; reverse the receipt or payment instead.
- **Not built:** quotations, sales and purchase orders, delivery notes, goods receipt notes, proforma invoices, hold/resume, barcode or POS entry, additional charges, salesperson, recurring invoices, reminders, approvals, ageing, statements and printing (Phase 6). The billing settings (round to rupees, tax-inclusive rates) have a service but no settings screen; the defaults are "round to rupees" and "tax-exclusive", and both can be changed per document in the editor.

**Printing, labels and scanners (Phase 6) specifics**
- **Unverified on Windows:** printer discovery and capabilities, printing through Windows drivers, the preview window, paper sizes on real printers, thermal roll length, label alignment, raw ESC/POS and USB scanner input. What is tested on macOS: barcode and QR encoding (decoded back by an independent reader), layout arithmetic and pagination, templates printing the stored figures, PDF output, reprint counting and DUPLICATE marking, test-page records, label geometry, ESC/POS command bytes, scan detection, permissions and isolation.
- **Same pages everywhere:** templates produce one set of page items. They are drawn to PDF (PDFsharp) or to the Windows printer and preview (WPF), with the bundled Noto Sans font, so sizes and pagination match. WPF text measurement can differ by a fraction of a point from the layout measurement.
- **Totals:** templates print the stored figures of the posted (or draft) document; the tax summary groups the stored line taxes by rate. Nothing is recalculated.
- **QR codes** on invoices are optional references in a configurable format. They are not GST e-invoice QR codes, and the printout says so.
- **Purchase orders** print from a draft purchase bill; QIK Suite has no purchase order workflow or number. **Delivery notes** print from a sales invoice.
- **Reprints:** every print and PDF export of a document is counted. Copies within one job are not marked DUPLICATE; the second and later jobs are (when enabled). A print is recorded only after Windows accepts it.
- **Test pages** count as passed only when someone confirms the paper.
- **ESC/POS** is experimental: only the command bytes are tested; no printer model has been tried. Text is ASCII (₹ prints as "Rs.").
- **Printer preferences are per company**, not per computer. Two computers sharing a company share printer names, so name printers the same way on both, or leave the Windows default.
- **Not built:** custom template designer, per-customer templates, batch printing of many invoices at once, e-mailing PDFs, print-job status after Windows accepts a job.

**Expenses, CRM and service (Phase 7) specifics**
- **Unverified on Windows:** all three screens, the dashboard follow-ups panel, the notifications list, the credit-limit question in Sales invoices, the warranty field on Products, opening attachments with the Windows default app, and job card printing on paper. What is tested on macOS: expense posting (claimable and unclaimable GST, intra- and inter-state), the approval states and self-approval rule, recurring generation (drafts only, once per due date), attachment limits and content checks, reversal, lead conversion with the duplicate check, follow-up states and the summary, the timeline, the customer view, customer prices, the credit-limit check and its permission, the service workflow, warranty from the sale of a serial, parts through stock (quantity, batch and serial rules), warranty parts to Warranty and Service Costs, returns, labour, the service invoice (no second stock movement), maintenance reminders, permissions, isolation, immutability triggers, the upgrade of existing companies, and job card and service invoice PDFs.
- **Expenses are paid from cash or bank.** The supplier on an expense is for reference; nothing is owed to them. A bill to pay later is a purchase bill.
- **GST on expenses** is worked out by the service from each line's tax category: CGST and SGST, or IGST when the supplier's state differs from the company's. "Claim GST" unticked adds the tax to the expense instead of input tax. As with sales, nothing here is a statement of GST compliance.
- **Approval:** one level. Anyone with "Expenses: Approve" can approve any pending expense except their own (unless Settings allows it). Existing companies' Accountant roles do not get "Expenses: Approve" automatically; tick it under Roles. Only the role templates for new companies include it.
- **Recurring expenses** only create drafts. Each due date is created once even if "Generate" is pressed again; nothing runs in the background.
- **Credit limit** is checked when a credit sales invoice is posted: the customer's ledger balance plus the new invoice against the limit. A limit of 0 means no limit. Drafts, receipts and returns are not checked.
- **Customer prices** are per base unit and replace the selling price only when the rate is left blank. Typing a different rate is still a price override.
- **Lead conversion** stops when a customer has the same name, the same last 10 digits of phone, or the same email. Other near-matches (spelling variants) are not detected.
- **Warranty** comes from the latest posted, uncancelled sale of the serial in this company plus the product's warranty months on the day the job is created. Items sold elsewhere, or without serials, are chargeable unless you change the charge before issuing parts. The charge cannot change once parts are issued.
- **Service invoices** are created as drafts and posted from Sales invoices. They cannot be edited there; to change them, delete the draft, change the job, and create it again. A part on a posted invoice cannot be returned to the job; make a credit note.
- **Labour SAC:** QIK Suite has no default SAC code; enter the one your accountant advises, per line or as the default in Service settings.
- **Not built:** quotations and orders (still not started), multi-level approvals, expense claims and advances, e-mail/SMS/WhatsApp, lead import, supplier contacts screen, estimates for customer approval, technician workload, service contracts (AMC) billing.

**Phases not started:** 8 (reports, dashboard data, import/export, backup), 9 (security hardening, installer, release).

## 5. Phase checklist

| Phase | Status | Definition of done |
|---|---|---|
| 1. Foundation: solution, shell, design system, themes, DI, logging, database, login, setup | Done on macOS, not verified on Windows | Builds clean; setup, sign-in, shell and themes work on Windows 10/11; design-rule tests pass |
| 2. Companies, users, roles, permissions, audit trail, isolation | Done on macOS, not verified on Windows | Permissions enforced in services; audit records old/new values and verifies; tests prove one company can't read another's data |
| 3. Accounting engine | Done on macOS, not verified on Windows (cash flow, bank reconciliation, cost centres and ageing moved to Phase 8) | Every posted voucher balances; posted records are immutable except by reversal; years open/close; trial balance, ledgers, P&L and balance sheet reconcile in tests |
| 4. Products and inventory | Done on macOS, not verified on Windows (deferred: CSV/Excel import/export to Phase 8, reservations and COGS from sales to Phase 5, product images) | Products, units, variants, warehouses, batches/serials; stock from a movement ledger; FIFO/weighted average costing per company; negative stock blocked unless allowed; inventory postings reconcile to the ledger |
| 5. Sales and purchases with GST | Done on macOS, not verified on Windows (not built: quotations, orders, delivery notes, dated GST rates, cess, reverse charge, POS/barcode entry; see section 4) | Quotation-to-invoice and purchase workflows post stock and accounts atomically; GST by place of supply with rates as dated data; totals recalculated in the Application layer; numbering and idempotency |
| 6. Printing | Done on macOS, nothing printed on Windows yet (layout, PDF, barcodes, records and permissions are tested; Windows printing, preview and scanners are unverified) | Printer settings and Print Centre with a verified test page; invoice, receipt and label templates; PDF export; barcode/QR |
| 7. Expenses, CRM, service | Done on macOS, not verified on Windows (all three screens are UI unverified at runtime; service logic, accounting and printing to PDF are tested) | Expense vouchers with approvals; leads to customers; service jobs consuming spare parts that post inventory and accounts |
| 8. Reports, dashboard data, import/export, backup | Not started | Reports from real postings, exportable and printable; real dashboard figures; CSV/Excel import/export with formula-injection protection; encrypted, verifiable backup with a tested restore |
| 9. Security, full test pass, installer, release | Not started | Session timeout, optional encryption, DPAPI secrets; full Windows QA pass; Inno Setup or WiX installer; signed release script; KNOWN_LIMITATIONS.md |

**Keeping this guide current:** after every phase, update this guide before stopping:
- add the new screens to the status table;
- add test cases for every new function in the same format;
- re-classify anything that changed.

Never mark something FULLY FUNCTIONAL unless the code is wired to a real service and tests cover its logic. `Design/WorkingGuideTests` fails the build if a sidebar item or Settings section has no row in section 1.
