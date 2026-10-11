# QIK Suite implementation progress

## Phase 7: expenses, CRM and service management, complete on macOS (11 October 2026)

**Starting point:** `b906500` (end of Phase 6) with a clean tree. Baseline: locked restore OK, build 0 warnings and 0 errors, **806 tests passed**.
- **Windows notes:** the owner's note was left as the template placeholder, so I took it as "Phase 5 and 6 screens not tested yet". No runtime problems were reported, so none were fixed.
- **Phase 5 and 6 gaps that Phase 7 needed**, and only those, were added:
  - service lines (no product, no stock) and issued-part lines (no second stock movement) on sales invoices;
  - customer-specific prices;
  - the credit-limit check with an override permission;
  - deleting a draft invoice clears a service job's link to it;
  - the job card print template.
  Quotations, orders and Phase 8 reports were not started.
- **Commits** are prefixed `claude:`.

**Windows:** the Expenses, CRM and Service jobs screens, the dashboard follow-ups and notifications, and the credit-limit question are **UI unverified at runtime**.

### Verified by build or test on macOS

| Area | Evidence |
|---|---|
| Build | 0 warnings, 0 errors after a clean build (all `bin`/`obj` deleted, locked restore). Three migrations: `ServiceLinesCreditLimitPrices` (billing lines can have no product; customer prices), `RestoreBillingLineTriggers` (EF's SQLite table rebuild dropped the billing-line triggers; they are recreated and a test checks them), and `ExpensesCrmService` (18 tables, product warranty months, immutability triggers). The model has no pending changes. |
| Tests | **873 passed, 0 failed**: 553 behavioural (504 earlier + 49 Phase 7), 288 XAML design-rule, 31 guide-coverage, 1 binding check. |
| Expenses | Posting debits the category ledgers and claimable input CGST/SGST (or IGST for a supplier in another state) and credits cash or bank, through the ledger poster with voucher type Expense; unclaimable GST is added to the expense. Draft → pending → approved/rejected → posted; approving needs "Expenses: Approve"; nobody approves their own expense unless the company setting allows it; a rejection needs a reason. Posted expenses cannot change (domain, data context, SQLite triggers) and are reversed once, with a reason, not before their date. The expense voucher cannot be reversed from Vouchers. Recurring templates create drafts only, once per due date (a unique index backs this). Attachments are checked by content (PDF, PNG, JPEG), name and size (5 MB, 5 per expense), stored with SHA-256 and verified on open; renamed files are refused. Totals by category, branch, department, date and method. |
| CRM | Leads with status, source, owner and lost reason. Conversion creates the customer and its ledger in one transaction; a customer with the same name, phone (last 10 digits) or email stops it and is offered to link to instead. One primary contact per party. Enquiries for a lead or customer move to the customer on conversion. Follow-ups: open, overdue, done; "mine" covers assigned to me or unassigned ones I created; the summary drives the dashboard and the bell. Activities are append-only. The customer view reads balances, invoices, payments and service from their own records and hides sales figures without Sales: View. |
| Credit limit and prices | A credit invoice above the limit (ledger balance plus the invoice) is refused unless the user holds "Sales: Override credit limit" and asks to override; the override is audited. A customer price replaces the selling price when the rate is blank; setting it needs "Sales: Override price". |
| Service | Workflow transitions are enforced in the domain; closed jobs cannot change (domain and triggers). Warranty comes from the latest posted, uncancelled sale of the serial plus the product's warranty months. Parts go through the stock engine (negative stock, batch by ID or number, serial) and post Cost of Goods Sold for chargeable jobs or Warranty and Service Costs for warranty jobs. Unbilled parts return at their cost. The service invoice carries parts as issued-part lines and labour as service lines; posting it moves no stock and posts no Cost of Goods Sold again. A job's invoice draft cannot be re-saved as ordinary goods. Delivery of a chargeable job needs its invoice posted; cancelling needs a reason and no parts on the job. Maintenance schedules create one follow-up per due date. History per customer and per serial. |
| Printing | The job card and the service invoice render to PDF through the Print centre with DUPLICATE marking on reprint. Both were rendered to images and inspected. |
| Permissions | Service Staff run jobs and issue parts but cannot invoice; Billing Operators cannot see service; Read-only users cannot change anything. Service and CRM users can pick customers (name and phone only) without Sales: View. |
| Isolation and immutability | Another company cannot read or change any Phase 7 row by ID. The isolation tests cover the new tables automatically. SQLite rejects changes to posted expenses, locked lines, attachments of posted expenses, reversals, activities, part movements and closed jobs. |
| Upgrade | Existing companies receive expense and service settings and numbering at start-up, and the Warranty and Service Costs account the first time a part is issued under warranty. |
| Mutation checks | Ten rules were broken one at a time: self-approval, unclaimable GST, recurring duplicates, conversion without the duplicate check, the credit limit, the workflow (Received → Ready), warranty parts to COGS, issued parts moving stock on the invoice, the attachment type check, and re-saving a service invoice draft as goods. Each made at least one test fail. The recurring and attachment mutations first survived; the recurring one needed both guards removed to reach the unique index, and a renamed-file test was added for the attachment check. |
| Bindings | The binding test was checked to fail on a misspelt binding in the new Service view. |

### Built but unverified (needs Windows)

- **Screens:** Expenses (list, editor, categories, recurring, totals, settings), CRM (leads, follow-ups, enquiries, customers) and Service jobs (jobs, job card editor, maintenance, history, settings).
- **Elsewhere:** the dashboard's "My follow-ups" panel, the notifications list, the credit-limit question when posting, the Products warranty field.
- **Desktop behaviour:** opening attachments with the Windows default app, and job card printing on paper.

They compile, pass the design-rule tests, and their page-level bindings resolve.

### Known gaps (Phase 7)

See `WORKING_GUIDE.md` section 4, "Expenses, CRM and service specifics". In short:
- expenses are paid from cash or bank only;
- there is one approval level;
- no messaging (e-mail, SMS, WhatsApp);
- service invoices are posted from Sales invoices and cannot be edited there;
- existing companies' Accountant roles need "Expenses: Approve" ticked by hand;
- no default labour SAC code is supplied.

## Phase 6: printing, labels, PDF, barcodes and QR, complete on macOS (11 October 2026)

**Starting point:** `d405020` (end of Phase 5) with a clean tree. Baseline: locked restore OK, build 0 warnings and 0 errors, **700 tests passed**.
- **Windows notes:** the owner's note was left as the template placeholder, so I took it as "Phase 5 screens not tested yet". No runtime problems were reported, so none were fixed.
- **Phase 5 gaps for printing:** two pieces of document data were missing and were added:
  - a settlement detail view (`SettlementService.GetAsync`);
  - party statements across financial years (`PartyService.StatementAsync`).
  Quotations and orders were not started.
- **Commits** are prefixed `claude:`.

**Windows:** nothing has been printed, previewed or scanned on Windows. Every Windows printing path is **unverified at runtime**.

### Verified by build or test on macOS

| Area | Evidence |
|---|---|
| Build | 0 warnings, 0 errors. `Nexora.Printing` is now `net10.0` (decision recorded). Migration `Printing` adds 5 tables and 2 triggers; the model has no pending changes. |
| Tests | **806 passed, 0 failed**: 504 behavioural (410 earlier + 94 printing), 270 XAML design-rule, 31 guide-coverage, 1 binding check. |
| Barcodes | EAN-13 check digits match GS1 examples, and the code is 95 modules with guards. Every leading digit 0–9 decodes back to the same digits with ZXing's reader. Code 128 uses code set C for digits; its checksum is the weighted sum mod 103; 7 varied strings decode back exactly; non-ASCII text is refused. A product code uses EAN-13 only when it is a valid EAN-13. QR codes (ZXing encoder, UTF-8, level M) decode back, including ₹. |
| QR content format | Placeholders are filled case-insensitively. Unknown fields, unbalanced braces and document fields on labels are refused when settings are saved. |
| Layout | ISO A4 and A5 sizes in points; landscape swaps sides; 58 and 80 mm rolls have no page height and grow with content. Scale shrinks from the margin corner. Page breaks never put text below the bottom margin, the continuation header repeats, and pages read "Page n of N". Wrapping never exceeds the width or drops words. The bundled font has ₹. |
| Templates | The invoice prints the stored figures: taxable, CGST, SGST, round-off, total, amount in words. It shows company name, address and GSTIN, the party, place of supply and signature. The tax summary groups stored line taxes and adds up to the header. 60 lines on A5 paginate onto 3 pages, with totals only on the last page and totals kept with the signature. Cash bills fit one 80 mm page. Delivery notes have no prices. Purchase orders print only from draft bills. Draft and cancelled documents carry banners. Credit notes, purchase bills and debit notes have their own titles. Receipts show allocations, the advance and words. Statements show opening, lines, closing (Dr/Cr) and open documents. The invoice QR follows the configured format, is labelled "Not an e-invoice QR", and decodes. |
| PDF | Produced from the same pages; Noto Sans is embedded as a subset. Exports are counted as prints. I rendered sample A4 invoice, 80 mm cash bill, label sheet, statement and test-page PDFs to images and looked at them. That review caught an arrow glyph missing from the font on the test page and a label's price crowding its barcode; both are fixed. |
| Reprints | The second print gets print number 2 and DUPLICATE; the first does not. With marking turned off, prints are still counted. A failed print is not recorded and returns recovery advice. Jobs use the profile's printer, paper, orientation, copies, scale and font size. |
| Test prints | Recorded as Sent; they pass only when someone confirms. Confirming twice is refused, a problem needs notes, and a failed send can't be confirmed. Printers keep the capabilities the driver reported, without inventing any. |
| Labels | Sheet positions (columns, rows, gaps, margins, page breaks, skipped labels). Everything stays inside the label. The label barcode scans, with modules wider than 0.7 pt. QR labels keep text clear of the code. Layouts that don't fit are refused, and inactive layouts can't print. |
| ESC/POS (experimental) | Initialise, feed and cut bytes; lines and pairs fit the column count; Code 128 and QR commands carry correct lengths; non-ASCII is replaced. Raw bytes are sent only when enabled for receipts on thermal paper. |
| Scanner | Fast keys ending in Enter count as a scan; slow typing, short codes and a late Enter don't; a scan right after typing is recognised. Codes find products by barcode, QR identifier or SKU. |
| Permissions | A Billing Operator prints sales documents but can't print purchase bills, change preferences, run test pages, change settings or see the full history. An Inventory Manager prints labels but not sales invoices. Settings, layouts and test pages need printing.manage. |
| Isolation and immutability | Another company can't render a document by ID and sees none of the print history. The isolation tests cover the 5 new tables automatically. SQLite rejects UPDATE and DELETE on print records. |
| Upgrade | Existing companies receive print settings, four preferences and two label layouts once. |
| Mutation checks | Breaking each of these failed 1–8 printing tests: EAN-13 parity, Code 128 checksum, DUPLICATE marking, not recording failed prints, page breaking, confirming unsent test pages, label column spacing. The EAN-13 parity mutation first survived, so the all-leading-digits test was added. |

### Built but unverified (needs Windows)

- **Print centre:** the screen, the print preview window and the print buttons on Sales invoices, Purchase bills, Receipts, Customers, Suppliers and Products.
- **`WindowsPrinterGateway`:** printer listing, capability reading, status mapping, XPS printing with tickets, and raw spooler output.
- **`WpfDocumentBuilder`:** drawing the pages with WPF.
- **Window-level scanner hook.**

They compile, pass the design-rule tests, and their page-level bindings resolve.

### Known gaps (Phase 6)

See `WORKING_GUIDE.md` section 4, "Printing, labels and scanners specifics". In short:
- nothing has been printed on Windows;
- ESC/POS is untested on hardware;
- printer preferences are per company, not per computer;
- there is no template designer, no batch invoice printing and no e-mail.

## Phase 5: sales, purchases and payments, complete on macOS (11 October 2026)

**Starting point:** the request said GPT-6 Astra had worked on Phase 5. The repository showed no such work:
- `git log` ended at the Phase 4 commit `926a72e`;
- the tree was clean, with no stashes, extra branches or worktrees.

Baseline before any change: build 0 warnings, 0 errors; **605 tests passed, 0 failed**. Phase 5 commits are prefixed `cla:`.

**Windows:** only "the app opens" has been confirmed. The four new screens are **unverified at runtime**. Nothing was printed (Phase 6) and no installer was built.

### Verified by build or test on macOS

| Area | Evidence |
|---|---|
| Build | 0 warnings, 0 errors. Migration `Billing` adds 8 tables and 12 triggers. `dotnet ef migrations has-pending-model-changes`: none. |
| Tests | **700 passed, 0 failed**: 410 behavioural (327 earlier + 72 billing + 11 inventory edge cases), 258 XAML design-rule, 31 guide-coverage, 1 binding check. |
| Edge case 1: replenishing negative stock | Before the fix, a receipt that brought weighted-average stock back to zero left value without quantity (for example +40.00 at quantity 0). Received units now settle the shortfall: the estimated value leaves the pool, extra units enter at the receipt cost, and the cost difference is posted to Inventory Adjustments. The supplier is still credited in full. Stock still equals the ledger. Covered for full, beyond and partial settlement and adjustment-in, and through a purchase bill. |
| Edge case 2: duplicate serials in counts | A count gain could create a serial already in stock in another warehouse, and a document could list a serial twice. Both are refused now (in every document type). Recording a missing serial and finding a new one still works. |
| Edge case 3: reversing a chain | Reversing the newest document and then the earlier one was refused, because the later, already-reversed movements still counted. Reversed pairs are now ignored, and costing after a reversed chain is exact. A reversal can't be dated before the product's latest movement (previously possible in a chain). Reversing an earlier document while a later live one exists is still refused. |
| Edge-case tests fail without the fixes | Running the 11 edge-case tests against the old code: 8 fail; the other 3 guard behaviour that was already correct. |
| Calculation | `BillingCalculatorTests` cover each rule: rounding, inclusive and exclusive tax, CGST/SGST/IGST, discounts, invalid input, round-off half away from zero, document totals, proportional returns that add up exactly, and cost portions. |
| Sales | Credit and cash sales, inter-state IGST, round-off, alternate units with inclusive rates and discounts. Drafts move neither stock nor the ledger. Posting twice is refused. A failed post rolls everything back, including the number. Posted documents keep their tax rate after the category changes. Untaxed products work. FIFO cost of goods. Serials must be in stock. Credit sales need a customer. Stock created by an invoice can't be reversed or posted from Stock. Lists show status and support search. |
| Returns | Partial returns reverse tax, cost and stock and reduce the outstanding amount. A full return in parts reverses exactly. Returns beyond the quantity sold are refused, including two drafts posted one after the other. Credit beyond the outstanding amount becomes unapplied and can be refunded. Cash-sale returns are refunded from cash. Serials come back once. A return needs a posted original. |
| Cancellation | Reverses stock and ledger, keeps the number, and is refused twice. Refused while a receipt or return is attached, and allowed once the receipt is reversed. A reason is required; cancellation is refused when later stock moved. |
| Purchases | A bill receives stock at its taxable value and claims input tax; it creates batches with expiry. A supplier bill number is accepted once (again after cancelling). Debit notes leave at book cost with the difference to Inventory Adjustments. Returns are limited by the bill and by stock. Cash purchases work. A bill settles negative stock. |
| Settlements | One receipt settles several invoices partly or fully. Over-allocation (amount or outstanding, another party, cash invoices, zero amount, a non-cash account) is refused with nothing posted. Advances can be applied later. Reversal reopens invoices and restores the ledger, and is refused twice. Supplier payments with debit notes work, as do supplier refunds of debit notes. Party ledger balance = outstanding − unapplied after mixed activity. |
| Parties | Ledgers under Sundry Debtors or Creditors. A customer and supplier with the same name get distinct ledgers; duplicates are refused. An invalid GSTIN, state or PIN leaves no stray ledger. Renaming renames the ledger but not posted documents. Inactive parties can't be billed. |
| Permissions | A Billing Operator sells within the 5% discount limit but can't override prices, buy, cancel, see suppliers or purchase bills, or see cost. Limits are re-checked when someone else posts a draft. Read-only can view but not change. An Inventory Manager buys but can't see sales or cancel. |
| Isolation and immutability | Another company's documents and parties can't be read, cancelled or used. SQLite triggers reject UPDATE and DELETE of posted documents, locked lines, settlements and allocations, while drafts stay editable. |
| Upgrade | A database without billing settings or billing numbering receives them once on start. New companies get both. |
| Mutation checks | Breaking each of these made 1–37 tests fail: COGS posting, the outstanding check, the return-quantity limit, the price-override check, return auto-allocation, inter-state detection. |

### Built but unverified (needs Windows)

Sales invoices, Purchase bills, Customers and Suppliers screens (`Views/Billing`). They compile (including XAML markup compilation), pass the design-rule tests, and every page-level binding resolves. Their view models have no automated tests.

### Known gaps (Phase 5)

- **Not built:** quotations, sales and purchase orders, delivery notes, goods receipt notes, proforma invoices, hold and resume, barcode or POS entry, additional charges, customer-specific prices, salesperson, recurring invoices, reminders, approvals, statements, and a billing settings screen.
- **GST:** dated rate history, cess, reverse charge, exempt or nil-rated distinctions, bills of supply, ineligible input tax and GST registers. The rate is the tax category's configured rate, as the owner asked; no rates were added or changed.
- **Behaviour:** see `WORKING_GUIDE.md` section 4 "Sales, purchases and payments specifics".

## Phase 4: products and inventory, complete on macOS (11 October 2026)

**Windows:** the owner reports the app **opens** on Windows. No other screen has been confirmed there, so the WPF UI, printing and installer remain **unverified at runtime**. No runtime problems were reported, so none were fixed before this phase. (The report template's "what I saw" field was left unfilled.)

### Verified by build or test on macOS

| Area | Evidence |
|---|---|
| Build | Clean build: 0 warnings, 0 errors. Migration `Inventory` (8 tables, voucher source columns, 4 triggers). The model has no unmigrated changes. |
| Tests | **605 passed, 0 failed**: 327 behavioural, 246 XAML design-rule, 31 guide-coverage, 1 binding check. |
| Data table logic | Saved layouts merge onto changed columns (drops removed, appends new, keeps one visible, clamps widths). The quick filter needs every word. Totals ignore non-numbers. Layouts are stored per user. (`TableLayoutTests`, 18 tests.) |
| Products | Create and edit; SKU, barcode and QR identifier are unique. EAN-13/EAN-8 check digits (published examples), MRP ≥ selling, HSN 4/6/8 digits. Variants hold stock and their parent cannot. Archiving requires zero stock. Unit and tracking are fixed after creation. Alternate units convert to the base unit. |
| Costing | Weighted average (running average, no rounding residue, last-cost negative issues) and FIFO (oldest layers first, never negative). Replaying the ledger reproduces it exactly and skips reversed pairs. The costing method is fixed once stock moves; negative stock with FIFO is refused. |
| Stock documents | Opening, receipt, issue, adjustment in/out, transfer (no value, no voucher), physical count (gains and losses against the book), and state change (damaged stock is unavailable; write-off posts to Inventory Adjustments). Batches need a number and expiry, and can't go negative. Serials are one per line and unique in stock. Dates can't precede a product's last movement. |
| Negative stock | Blocked by default with a clear message; allowed only when enabled (weighted average). |
| Reversal | Restores quantity, value and ledger. Allowed only for a product's latest document, and only once. Stock vouchers can't be reversed from Vouchers. |
| Reconciliation | One test posts every document type (opening, receipts, batch receipt, issue, transfer, state change, write-off, adjustment in, physical count, reversal). Stock valuation = stock ledger = Inventory in the trial balance = Inventory on the balance sheet, and both balance. Every stock test also asserts reconciliation. |
| Manual Inventory blocked | Manual vouchers and opening balances can't post to Inventory, which keeps reconciliation exact. |
| Concurrency | 8 parallel issues of 2 against 10 in stock: exactly 5 succeed, stock ends at 0, and it still reconciles. |
| Permissions | Billing operators get no cost figures (purchase price, movement and document values are null; valuation and reconciliation are refused). Read-only can't change products or stock. Inventory managers post but can't change costing policy. Accountants view but can't post. Editing a product without view-cost keeps the stored purchase price. |
| Isolation | Another company's products, stock and documents are invisible and unusable. SQLite rejects a movement referencing another company's product. The isolation test automatically covers all 8 new tables. |
| Immutability | The data context refuses changes to stock movements and documents; SQLite triggers block UPDATE and DELETE (4 cases). A demonstration company with stock can still be deleted. |
| Upgrade | A Phase 1 database receives inventory settings and 10 units on startup. |
| Bugs found by tests | A typed unit cost for an alternate unit (per BOX) was applied per base unit, giving 28,800 instead of 2,400; fixed. The default cost for adjustments and count gains could have more than 4 decimals and would have been rejected; fixed. |
| Mutation checks | Disabling each of these failed 1–11 tests: the negative-stock check, stock voucher posting, FIFO layer order, the manual-Inventory block, cost withholding, latest-only reversal. |

### Built but unverified (needs Windows)

The Products, Stock and Settings → Inventory screens, and the shared data table control (filter box, column menu, totals row alignment, saved layouts restored on reopen). They compile, pass the design-rule tests, and every page-level binding resolves.

### Known gaps (Phase 4)

- **Deferred to Phase 5:** reservations and available-to-promise, and COGS posting from sales.
- **Deferred to Phase 8:** CSV/Excel import/export.
- **Not built:** product images; editing or deactivating units and tax categories in the UI; per-column filters.
- **Tax rates:** tax category rates are reference values only; current GST rates were not verified.
- **Cost and value tracking:** cost is pooled per product across warehouses; value is tracked per product, quantity per warehouse, state and batch.

## Working-guide audit (10 October 2026)

`docs/WORKING_GUIDE.md` classifies every screen and contains a Windows test script. The audit added tests for service paths that had none (chart editing, warehouses, branch deactivation, numbering edits, logo, role change), plus `ViewBindingTests` (every page-level XAML binding exists on its view model) and `WorkingGuideTests` (the guide covers every sidebar item and Settings section). It also fixed a voucher-editor bug where a failed Post created a new draft on each retry, and relabelled a misleading numbering column. Tests: **502 passed, 0 failed** (228 design-rule, 30 guide, 1 binding, 243 behavioural).

## Rename: Nexora Business Suite → QIK Suite (10 October 2026)

The product name users see is now QIK Suite, and the executable is `QIKSuite.exe`. The data folder is `%LocalAppData%\QIKSuite`, with a tested, staged copy from the old `Nexora\BusinessSuite` folder. Internal namespaces and project names stay `Nexora.*`; see `DECISIONS.md`. A test checks that every WPF `;component` resource URI matches the assembly name. Entries below dated before the rename refer to the old name where they describe what was built at the time.

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
| Self-contained publish | `dotnet publish -r win-x64 --self-contained` produced `Nexora.exe` (now `QIKSuite.exe`; see the rename note at the top), the native `e_sqlite3.dll` and the WPF runtime (149 MB). The output was not run. |
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
