# PROJECT: QIK SUITE (formerly Nexora Business Suite)
A commercial Windows desktop application for billing, inventory, accounting, CRM and service management, sold to multiple business customers in India.

## 0. ROLE AND WORKING METHOD

Act as an autonomous senior engineering team: architect, WPF developer, database engineer, accounting specialist, UI designer, QA engineer, security engineer, deployment engineer.

Work directly in this project folder. Create real source files, build, run tests, read errors, fix them, and continue. Do not deliver plans, mockups, screenshots or loose snippets.

**Work in phases. Finish and verify each phase before starting the next. After each phase, write a short status note to `/docs/PROGRESS.md` covering what works, what is tested, and what is NOT verified.**

- Phase 1: Solution structure, application shell, design system, theme engine, DI, logging, database setup, migrations, login, first-run wizard.
- Phase 2: Company, user, role and permission management. Audit trail. Data isolation.
- Phase 3: Accounting engine (chart of accounts, vouchers, ledgers, double-entry posting, financial-year handling) with unit tests.
- Phase 4: Products, inventory, warehouses, batches/serials, costing.
- Phase 5: Sales and purchase workflows with GST, tied to accounting and inventory.
- Phase 6: Printer Settings, Print Centre, invoice/receipt/label templates, PDF export, barcode/QR.
- Phase 7: Expenses, CRM, service management.
- Phase 8: Reports, dashboard data, import/export, backup/restore.
- Phase 9: Security hardening, full test pass, installer, release scripts, documentation.

**Environment honesty rules**
- At the start, detect the OS. If it is not Windows, set `EnableWindowsTargeting=true` so the WPF projects compile, and state clearly that the UI, printing and installer could not be run or tested here.
- Never claim something works unless it was actually built, run or tested. Mark everything else "unverified" in PROGRESS.md.
- Provide a `build-windows.ps1` script and a GitHub Actions workflow (`windows-latest`) that restores, builds, tests, publishes a self-contained `win-x64` build and produces the installer.
- If a library or API is uncertain, check the package documentation instead of guessing.

## A. TECHNICAL REQUIREMENTS

- C# on the current .NET LTS version available in the environment.
- WPF with MVVM (CommunityToolkit.Mvvm or equivalent).
- Entity Framework Core with SQLite for the first release. Isolate the provider behind an abstraction so SQL Server or PostgreSQL can be added later without changing the domain or application layers.
- Maintained PDF library, maintained barcode/QR library, Windows printing APIs with installed printer drivers.
- xUnit for unit and integration tests, with an assertion library whose licence permits commercial use.
- Verify the licence of every dependency for commercial redistribution. List them in `/docs/THIRD_PARTY_LICENSES.md`. Flag any that need a paid licence at the expected revenue level.
- Not a web app and not Electron. Native Windows desktop only.

Solution layout:
- `Nexora.Desktop` (WPF, views, view models, design system)
- `Nexora.Domain` (entities, value objects, business rules, no framework dependencies)
- `Nexora.Application` (services, use cases, validation, DTOs)
- `Nexora.Accounting` (posting engine, ledgers, reports logic)
- `Nexora.Infrastructure` (EF Core, repositories, backup, file storage, import/export)
- `Nexora.Printing` (printer enumeration, templates, rendering, PDF, labels)
- `Nexora.Tests` (unit and integration)

Use dependency injection, async where appropriate, structured logging to rolling files, input validation, and consistent exception handling with user-friendly messages.

## B. VISUAL DESIGN: IMPORTANT

The product must feel like a carefully made professional tool, not like a generated template. It must look different from Tally, but equally efficient.

**Design direction: "calm, dense, precise."** Think of a well-made accounting tool or code editor, not a marketing page.

**Colour**
- Mostly neutral. Warm-grey surfaces, near-black text, hairline borders.
- ONE restrained accent colour, used only for primary actions, selection and focus. Suggested: deep teal `#0F5F63` (light theme) and a lighter tint of it in dark theme. Do not use any other accent.
- Semantic colours (success, warning, danger) are muted and used only for status text, small badges and chart series where meaning requires it.
- Light theme: background `#F6F5F2`, surface `#FFFFFF`, border `#E3E1DB`, text `#1C1D1F`, secondary text `#6B6E73`.
- Dark theme: background `#16171A`, surface `#1E2024`, border `#2D3036`, text `#E8E9EB`, secondary text `#9A9DA3`. Must be a proper design, not simply inverted.
- Define all colours as resource-dictionary tokens. No hard-coded colours in views.

**Things to avoid (these make an app look AI-generated)**
- No purple/blue/pink gradients. No gradient buttons, headers or backgrounds.
- No glassmorphism, neon glows, heavy drop shadows or oversized rounded cards.
- No emoji or clip-art icons. No rainbow-coloured dashboard tiles.
- No generic "hero" banners, no decorative blobs, no filler illustrations.
- No giant padding or oversized fonts.

**Typography and icons**
- Segoe UI Variable (with Segoe UI fallback) for the UI. Optionally a tabular-figure font for numbers so amount columns align. Right-align all amounts.
- Scale: 12px table text, 13px body, 15px section headings, 20px page titles. Use weight and spacing for hierarchy, not colour.
- One consistent line-icon set (Segoe Fluent Icons or a single embedded SVG icon library). Same stroke weight everywhere.

**Shape and spacing**
- 4px corner radius on controls, 6px on dialogs and panels. 8px spacing grid. Borders instead of shadows. One subtle shadow level for popups and dialogs only.
- Data tables are the hero: compact rows (28-32px), sticky headers, zebra striping off by default, column resize/reorder/hide, sorting, filtering, keyboard row navigation, totals row, saved column layouts.

**Motion (polished, purposeful, restrained)**
- 120-200ms with ease-out curves. Used for: sidebar collapse, dialog open/close, page transitions (short fade plus 8px slide), list item insert/remove, toast notifications, theme switching, chart first-load draw, skeleton loaders, button press feedback, focus rings.
- No bouncing, spinning, parallax or looping decorative animation.
- Respect the Windows "reduce animations" setting.
- All animations must keep the UI responsive; never block input.

**Application shell**
1. Collapsible sidebar (icons plus labels, grouped sections, active-item indicator, collapses to icons).
2. Top bar: global search (Ctrl+K command palette), company switcher, notifications, user menu, theme toggle.
3. Dashboard: sales, purchases, receivables, payables, cash and bank position, inventory valuation, gross and net profit. Use clean KPI figures with small deltas rather than coloured tiles. Charts for revenue vs expenses, sales trend, top products, receivables ageing. Quick actions for new invoice, purchase, payment, receipt, product, customer. Recent activity and low-stock alerts.
4. List views, forms, dialogs, document previews, all from one reusable component library.
5. Light and dark themes.
6. Full keyboard operation: shortcuts documented in a Help screen, F-key billing, Tab order tuned for rapid data entry.
7. Consistent loading (skeletons), empty (short helpful text plus one action), error (what happened, what to do) and confirmation states.
8. Works from 1366x768 up to 4K, with DPI-aware layouts.

Build the shell, design system and dashboard first and make them genuinely polished before any other module. Create a `/docs/DESIGN_SYSTEM.md` and a hidden "component gallery" screen showing every control and state.

## C. COMPANY AND USER MANAGEMENT

- Multiple company profiles with fast switching.
- Company logo and branding, address, contacts, PAN, GSTIN, financial-year settings.
- Branches and warehouses.
- Company-specific invoice numbering and preferences.
- Authentication with secure password hashing.
- Roles: Administrator, Accountant, Billing Operator, Inventory Manager, Service Staff, Read-only.
- Granular permissions (per module and per action, such as price override, discount limit, cancel, reverse, export, view cost price).
- Audit history of who changed what, when, with old and new values.
- Import and export. Backup and restoration.
- Enforce data isolation between companies in application services AND at the database level (company key on every business table, global query filters, tests that prove one company cannot read another's data).
- First-run setup wizard. A clearly labelled demonstration company with sample data that can be deleted.

## D. PRODUCTS AND INVENTORY

- Create, edit, archive, search products.
- SKU, barcode, QR identifier, name, description, category, brand, images.
- Units and unit conversions, variants (size, colour, model).
- Purchase price, selling price, MRP, discount, configurable tax category.
- Opening stock and opening value.
- Stock receipts, issues, adjustments, transfers, physical counts, reconciliation.
- Multiple warehouses. Reservations and available stock.
- Reorder levels and low-stock alerts.
- Batch, serial, expiry and manufacturing dates where applicable.
- Damaged, expired, returned and obsolete stock states.
- Full stock movement history.
- Inventory valuation using an explicitly selected costing method per company (FIFO, weighted average; document the choice and its effect).
- CSV and Excel import/export.
- Stock balances must be derived from the movement ledger or updated transactionally with consistency checks. Block negative stock unless an authorised company setting allows it.

## E. SALES, BILLING AND INVOICING

- Enquiries, quotations, sales orders, delivery notes, proforma invoices, tax invoices, cash bills, credit sales.
- Sales returns, credit notes, refunds, recurring invoices.
- Customer-specific prices and discounts.
- Partial, advance and split payments, with payment allocation.
- Outstanding tracking, payment reminders, salesperson assignment.
- Workflow states: draft, hold, resume, post, cancel, reprint.
- Fast invoice entry: product search, barcode scan, quantity editing, permission-controlled price override, discounts, item-wise tax, additional charges, configurable payment methods.
- Calculate subtotal, taxable value, discounts, tax, rounding, other charges, paid amount and balance due consistently. Use `decimal` for all money. Define and unit-test the rounding policy. Never trust totals sent from the UI; always recalculate in the application layer.
- Unique configurable invoice numbering. Prevent duplicate posting (idempotency).

## F. PURCHASES AND SUPPLIERS

- Supplier profiles, purchase orders, goods receipt notes, purchase invoices.
- Purchase returns, debit notes, supplier advances.
- Partial and full supplier payments, outstanding balances, purchase history.
- Purchase expenses, supplier statements, approval workflow.
- Posting a purchase must update stock and accounts atomically in a single database transaction.

## G. INDIAN GST

- CGST, SGST, IGST, Cess, HSN/SAC codes.
- Tax-inclusive and tax-exclusive pricing.
- Intra-state vs inter-state determined by place of supply.
- Registered and unregistered customers. Tax invoices and bills of supply.
- Sales and purchase registers. Credit and debit notes.
- Tax summaries and reconciliation reports.
- Configurable exemptions and reverse charge.
- Exportable GST data.
- Tax rates and rules are data, not code. Store effective dates, and keep the rate used on every posted line so historical documents never change.
- Do not claim or imply e-invoicing, e-way bill or GST portal filing. No government API connectivity unless properly implemented and tested with authorised access.

## H. ACCOUNTING ENGINE

Genuine double-entry accounting:

- Chart of accounts, groups, classifications.
- General ledger, customer and supplier sub-ledgers, sundry debtors and creditors.
- Cash book, bank book.
- Vouchers: journal, payment, receipt, contra, sales, purchase, expense, income, credit note, debit note.
- Opening balances. Receivables and payables. Ageing.
- Trial balance, profit and loss, balance sheet, cash flow.
- Bank reconciliation. Cost centres and profit centres.
- Financial-year closing and opening-balance carry-forward.
- Every posted journal must balance (debits = credits). Enforce in the domain layer and with a test.
- Define the account mappings for inventory, sales, purchases, tax, discounts, returns, receipts, payments, expenses and cost of goods sold. Document them in `/docs/ACCOUNTING_MAP.md`.
- Profit is never "cash received minus cash spent". COGS comes from the selected costing method.
- Posted transactions are immutable. Changes only through authorised reversal, cancellation or correction, with full audit history.
- Trial balance, balance sheet, ledgers, inventory valuation and P&L must reconcile to the same underlying transactions. Add automated reconciliation tests.

## I. EXPENSES

Categories, expense vouchers, attachments, supplier and employee links, cash and bank payments, recurring expenses, tax information, approval status, and reports by category, date, branch, department and payment method.

## J. CRM

Leads, prospects, customers and contacts, enquiries, follow-up tasks and reminders, call notes and activity timeline, lead conversion, purchase history, credit limits, customer pricing, payment history, communication history, service history.

## K. SERVICE MANAGEMENT

Service tickets and repair job cards, customer and product details, serial numbers, warranty info, technician assignment, status workflow, estimated completion date, spare-part consumption (which must correctly reduce inventory and post accounting), labour charges, service invoices, maintenance schedules, service history.

## L. BARCODE AND QR

- Generate Code 128 and valid EAN-13 (with check digit validation).
- QR generation with configurable, documented content format.
- Unique product identifiers and duplicate-code detection.
- Product lookup from scans. USB scanners work as keyboard input (handle fast-typed input ending in Enter, without stealing focus from other fields).
- Label templates with preview. Product name, SKU, price, barcode or QR. Configurable label size, margins, spacing, quantity. Batch printing.
- Do not imply a QR code makes a GST-compliant document.

## M. PRINTER MANAGEMENT AND PRINT CENTRE (CORE FEATURE)

Build a dedicated Printer Settings and Print Centre:

1. List all Windows-installed printers with default flag.
2. Show status, paper sizes and capabilities where the driver exposes them. Do not assume every printer provides the same information.
3. Choose default printer.
4. Separate preferences for invoices, receipts, reports and labels.
5. Test print, plus a verified workflow: print a test page, then ask the user to confirm it came out correctly. Record the result. Do not assume success because Windows lists the printer.
6. Print preview. Direct printing to selected printer. USB, network and any driver-supported printer.
7. A4, A5, and thermal 58 mm and 80 mm. Configurable paper size, orientation, margins, scaling, font sizes.
8. Copies, reprint tracking (who, when, how many times, marked "DUPLICATE" if configured).
9. PDF export.
10. Barcode and QR label printing.
11. Professional templates: invoice, receipt, delivery note, purchase order, customer and supplier statements.
12. Clear printer error messages, diagnostics, and recovery guidance for offline, disconnected or unavailable printers.
13. Default to installed Windows drivers. Add ESC/POS or raw printing only for hardware that is actually supported and tested, and keep it optional.

Templates should look like real business documents: restrained typography, clear tables, aligned amounts, no decorative colour.

## N. REPORTS AND ANALYTICS

Searchable, filterable, exportable (PDF, Excel, CSV) and printable reports: daily/monthly/yearly sales, purchases, gross and net profit, inventory valuation, stock movement, low stock, expiry and batch, product-wise sales, customer-wise sales, supplier-wise purchases, receivables, payables, debtor and creditor ageing, cash and bank summaries, expenses, tax summaries, ledger statements, trial balance, P&L, balance sheet, cash flow, service performance, salesperson performance.

All reports use real persisted data and the same business rules. Demo data is always clearly labelled.

## O. SECURITY

- Passwords hashed with Argon2id or PBKDF2 (high iteration count, unique salt). Account lockout and password-change policy.
- Role and permission checks in the application layer, not just by hiding buttons.
- Optional database encryption (SQLCipher or equivalent). Secrets stored with Windows DPAPI.
- Encrypted, versioned, verifiable backups with restore tested in an integration test. Automatic scheduled backups.
- Parameterised queries only. Validate all imports. Protect against CSV/Excel formula injection on export.
- No sensitive data in logs. Tamper-evident audit log.
- Session timeout and auto-lock.
- Code-signing support in the release script (certificate supplied by me later).

## P. TESTING

- Unit tests: rounding, tax calculation, costing, double-entry balance, numbering, permissions.
- Integration tests: full flows (purchase -> stock -> sale -> COGS -> reports), company isolation, backup/restore, concurrent posting.
- Reconciliation tests: reports match ledgers.
- Report test coverage honestly. Do not hide failures.

## Q. DEPLOYMENT AND DELIVERY

- Self-contained `win-x64` publish.
- Windows installer (Inno Setup or WiX) with desktop shortcut, uninstall, per-user data folder, upgrade-safe database migrations.
- `README.md` (build/run), `/docs` (architecture, design system, accounting map, user guide, QA checklist for manual Windows and printer testing).
- A final `/docs/KNOWN_LIMITATIONS.md` listing everything not verified in this environment.

Begin with Phase 1. Do not ask me for confirmation between small steps. Stop only at the end of each phase to report status.