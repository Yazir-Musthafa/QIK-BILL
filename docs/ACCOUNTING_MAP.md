# Accounting map

Status: Phase 7, 11 October 2026.

The posting engine, chart of accounts and reports are implemented and tested (`Nexora.Accounting`). **Inventory postings are implemented (Phase 4)**: stock documents post their vouchers automatically (see "Inventory" below). **Sales, purchases, returns, receipts and payments post automatically (Phase 5)**; see "Sales and purchases" below. **Expenses, service spare parts, service invoices and the credit-limit check are implemented (Phase 7)**; see "Expenses and service" below. Nothing here is a statement of GST compliance.

## Principles

1. **Double entry everywhere.** A voucher posts only if total debits equal total credits, every line has exactly one positive side, there are at least two lines and at least two different accounts. `Voucher.EnsureBalanced` enforces this in the domain, and it is tested.
2. **Money is `decimal`, in paise.** Stored amounts have at most two decimal places, and more precision is rejected rather than silently rounded. In the database, amounts are stored as whole paise (64-bit integers).
3. **Rounding policy.** Calculated amounts (tax, discounts, quantity × rate) round half away from zero to the paisa with `Money.Round`, for example ₹0.005 → ₹0.01. Rounding a document total to whole rupees posts the difference to **Round Off** as its own line.
4. **Posted means immutable.** Posted vouchers, their lines and the ledger entries cannot be updated or deleted. Three layers enforce this: the domain model, the data context, and SQLite triggers. A correction is a **reversal**: a new voucher with debits and credits swapped, dated on or after the original, linked to it, and allowed at most once.
5. **One source of truth.** Every report reads the same `LedgerEntries` rows with the same rules, so they reconcile:
   - the trial balance;
   - each ledger statement;
   - profit and loss;
   - the balance sheet.
   A test posts a mixed set of vouchers and checks they agree.
6. **Profit is never "cash in minus cash out".** Income and expense accounts decide profit. Cost of goods sold comes from the company's costing method and is posted to **Cost of Goods Sold** when a sale moves goods out of stock (Phase 5).

## Financial years

- Years run from 1 April to 31 March. A company has a first year, and years are opened one after another (`OpenNextYearAsync`).
- Posting is allowed only on dates inside an **open** year, on or after the start of the company's books.
- **Closing** a year blocks posting and reversals dated inside it. It is refused while draft vouchers are dated in that year. An authorised user can reopen it; reopening is audited.
- **Carry-forward is computed, not posted.**
  - Asset, liability and equity balances accumulate across years.
  - Income and expense accounts start each year at zero.
  - The net profit of all earlier years appears in **Retained Earnings**.
  - No closing entries are needed, so a report for any date is always derived from the original postings.
- **Opening balances** are posted as an *Opening* voucher dated the first day of a year, for balance-sheet accounts only. Any difference between debits and credits goes to **Opening Balance Difference**, so the books always balance and the difference stays visible until it is corrected.

## System accounts

These accounts are created for every company. They have stable keys (`SystemAccounts` in code) and cannot be deactivated. Users can rename them, add accounts under groups, and create their own groups.

| Key | Code | Account | Nature | Kind |
|---|---|---|---|---|
| `cash` | 1111 | Cash | Asset | Cash |
| `bank-accounts` | 1120 | Bank Accounts (group) | Asset | Group |
| `sundry-debtors` | 1130 | Sundry Debtors (group, one ledger per customer) | Asset | Group |
| `inventory` | 1141 | Inventory | Asset | Inventory |
| `input-cgst`, `input-sgst`, `input-igst`, `input-cess` | 1151–1154 | Input CGST, SGST, IGST and Cess | Asset | Tax |
| `sundry-creditors` | 2110 | Sundry Creditors (group, one ledger per supplier) | Liability | Group |
| `output-cgst`, `output-sgst`, `output-igst`, `output-cess` | 2121–2124 | Output CGST, SGST, IGST and Cess | Liability | Tax |
| `rcm-payable` | 2125 | GST Payable under Reverse Charge | Liability | Tax |
| `capital` | 3101 | Owner's Capital | Equity | General |
| `retained-earnings` | 3201 | Retained Earnings | Equity | General |
| `opening-difference` | 3202 | Opening Balance Difference | Equity | General |
| `sales` | 4101 | Sales | Income | General |
| `sales-returns` | 4102 | Sales Returns | Income (debit-side) | General |
| `discount-received` | 4301 | Discount Received | Income | General |
| `purchases` | 5101 | Purchases (non-stock) | Expense | General |
| `purchase-returns` | 5102 | Purchase Returns (non-stock) | Expense (credit-side) | General |
| `cogs` | 5201 | Cost of Goods Sold | Expense | General |
| `freight-inward` | 5202 | Freight Inward | Expense | General |
| `discount-allowed` | 5301 | Discount Allowed | Expense | General |
| `round-off` | 5302 | Round Off | Expense (either side) | General |
| `bank-charges` | 5303 | Bank Charges | Expense | General |
| `inventory-adjustment` | 5304 | Inventory Adjustments | Expense | General |
| `warranty-expense` | 5309 | Warranty and Service Costs (Phase 7; added to older companies the first time a service part is issued, under the same group as Round Off, with the next free code from 5309) | Expense | General |

## Voucher types and account rules

| Type | Rule enforced at posting | Numbering (default) |
|---|---|---|
| Journal | Must not touch cash or bank accounts | `JV/{FY}/0001` |
| Payment | Must credit a cash or bank account | `PV/{FY}/0001` |
| Receipt | Must debit a cash or bank account | `RV/{FY}/0001` |
| Contra | Cash and bank accounts only | `CV/{FY}/0001` |
| Sales, Purchase, Expense, Income, Credit note, Debit note | Balance rules only. Sales invoices, purchase bills, credit notes and debit notes post these types with their own rules (Phase 5) | `SV/`, `PU/`, `EV/`, `IV/`, `CN/`, `DN/` |
| Opening | Balance-sheet accounts only; created by the opening balances function | `OB/{FY}/001` |
| Reversal | Created only by reversing a posted voucher | `RJ/{FY}/0001` |

Numbers come from the company's numbering rules (Settings → Document numbering). They're allocated inside the posting transaction, so they are unique and gapless per type and period, and concurrent posting is tested. Drafts have no number.

## Business event mappings

The table uses perpetual inventory. *Party* means the customer's or supplier's own ledger under Sundry Debtors or Sundry Creditors, created with the party. Intra-state supplies use CGST + SGST; inter-state supplies use IGST; the party's state (place of supply) decides. Rows marked **(Phase 5)** are implemented; the exact postings are under "Sales and purchases". Rows without a mark are not automated yet (enter them as vouchers).

| Event | Debit | Credit |
|---|---|---|
| Credit sale (taxable) (Phase 5) | Party (Sundry Debtors) — invoice total | Sales — taxable value; Output CGST/SGST or IGST (+ Cess) — tax; Round Off if rounded |
| Cash sale (Phase 5) | Cash or Bank — total | Sales; Output GST |
| Cost of goods sold (on each sale) (Phase 5) | Cost of Goods Sold — cost at the company's costing method | Inventory |
| Sales return / credit note (Phase 5) | Sales Returns — taxable value; Output GST — tax reversed | Party or Cash/Bank |
| Return to stock (with a sales return) (Phase 5) | Inventory — cost | Cost of Goods Sold |
| Discount allowed (settlement, after the invoice) | Discount Allowed | Party |
| Trade discount (on the invoice) | — (reduces taxable value; not posted separately) | — |
| Receipt from customer (Phase 5) | Cash or Bank | Party |
| Purchase of stock (credit) (Phase 5) | Inventory — taxable value; Input CGST/SGST or IGST (+ Cess) — eligible tax | Party (Sundry Creditors) |
| Purchase with ineligible input tax | Inventory — taxable value plus the ineligible tax | Party |
| Purchase of services or non-stock items | Purchases (or the specific expense) ; Input GST | Party or Cash/Bank |
| Freight on purchases | Freight Inward, or Inventory if capitalised (the costing decision is Phase 4) | Party or Cash/Bank |
| Purchase return / debit note (Phase 5) | Party | Inventory — cost; Input GST reversed |
| Reverse-charge purchase | Expense or Inventory; Input GST (if eligible) | Party; GST Payable under Reverse Charge |
| Discount received | Party | Discount Received |
| Payment to supplier (Phase 5) | Party | Cash or Bank |
| Expense paid (Phase 7) | The category's expense ledger, plus GST that cannot be claimed; Input CGST/SGST or IGST if claimable | Cash or Bank (expenses on credit are purchase bills) |
| Stock adjustment: shortage, damage, expiry (implemented, Phase 4) | Inventory Adjustments | Inventory |
| Stock adjustment: excess found (implemented, Phase 4) | Inventory | Inventory Adjustments |
| Service job spare parts, chargeable job (Phase 7) | Cost of Goods Sold — cost at the costing method | Inventory |
| Service job spare parts, warranty job (Phase 7) | Warranty and Service Costs — cost | Inventory |
| Service invoice: parts and labour (Phase 7) | Party or Cash/Bank — total | Sales — taxable value of parts and labour; Output GST (no Cost of Goods Sold: it was posted when the parts were issued) |
| Bank charges | Bank Charges | Bank |
| Owner introduces capital | Cash or Bank | Owner's Capital |
| Opening balances | Assets | Liabilities, Capital; any difference to Opening Balance Difference |

## Inventory (implemented in Phase 4)

### Costing method

Each company chooses **weighted average** (the default) or **FIFO** under Settings → Inventory. The choice cannot change once any stock has moved, because it decides every stored value.

- **Cost pool:** cost is pooled per product across all warehouses and states. Transfers between warehouses and changes of state (good, damaged, expired, returned, obsolete) move quantity only; they have no value and post no voucher.
- **Weighted average:**
  - A receipt adds its quantity and value to the pool.
  - An issue takes the running average per unit, rounded half away from zero to the paisa (`Money.Round`).
  - The last unit in stock takes exactly the remaining value, so no rounding residue is left behind.
- **FIFO:**
  - Each receipt becomes a layer.
  - Issues consume the oldest layers first. A whole layer leaves at its exact remaining value; a partial layer leaves at that layer's own rate, rounded to the paisa.
- **Value of stock arriving without a typed cost** (adjustments in, count gains): the product's current unit cost, which is the average, or the last cost when there is no stock.
- **Typed unit costs** are per the unit entered (for example per BOX), not per base unit.
- **Negative stock** is blocked unless the company allows it, and allowing it is possible only with weighted average. Batch and serial stock can never go negative.
  - With no stock left, issues use the last known unit cost, an estimate.
  - When stock is next received, the received units first **settle** the missing units. Their estimated value leaves the pool, and any further units enter at the receipt's own cost, so stock never keeps value without quantity.
  - The difference between what the settled units really cost and their estimate is a cost adjustment: Inventory Adjustments is debited (or credited), and the supplier is still credited the full amount.
  - The stock movement records the value that entered stock, so stock value still equals the Inventory account.
- **Posting order and dates:** costing replays a product's movements in posting order. Stock may not be dated before a product's last movement, so date order and costing order always agree.
- **Reversals:** a stock document can be reversed only while it is the latest live activity for each of its products. Documents that have themselves been reversed don't count, so a chain can be reversed newest first. A reversal can't be dated before the product's latest movement. Reversed documents and their reversals are left out of costing together, which restores costs exactly. Otherwise, post an adjustment.
- **Physical counts** may not create a serial that is already in stock anywhere, and a serial may appear only once per document.

### Stock ledger and reconciliation

- **Stock ledger:** every stock quantity and value is a sum of `StockMovements`, an append-only ledger. Rows can't be updated or deleted: the domain, the data context and SQLite triggers all refuse it.
- **Atomic posting:** each value-changing stock document posts its voucher through `ILedgerPoster`, in the same transaction as the movements. The voucher records its source document, and it can be reversed only by reversing that document under Stock.
- **No manual Inventory postings:** manual vouchers and the opening balances screen cannot use accounts of kind Inventory. Opening stock is entered as an Opening stock document instead.
- **Reconciliation:** as a result, **stock value (sum of movement values) always equals the Inventory account balance**. Stock → Valuation and reconciliation shows both and their difference. A reconciliation test posts every document type and checks stock valuation, the stock ledger, the trial balance and the balance sheet agree.

### Inventory postings

| Stock document | Debit | Credit | Voucher type |
|---|---|---|---|
| Opening stock | Inventory | Opening Balance Difference | Journal |
| Receipt (goods in without a purchase invoice) | Inventory | The chosen account: supplier ledger, Cash or Bank | Journal; Payment if Cash or Bank |
| Issue (internal use, samples, consumption) | The chosen account, or Inventory Adjustments if none is chosen | Inventory | Journal |
| Adjustment in; physical count gain | Inventory | Inventory Adjustments | Journal |
| Receipt that settles negative stock | Inventory (value entering stock); Inventory Adjustments (the cost difference, either side) | The chosen account, for the full receipt value | Journal; Payment if Cash or Bank |
| Adjustment out (for example writing off damaged or expired stock); physical count loss | Inventory Adjustments | Inventory | Journal |
| Transfer; state change | — (no value change) | — | None |
| Reversal of a stock document | The reverse of the original voucher | | Reversal |

Sales (Phase 5) post **Cost of Goods Sold / Inventory** at the cost the pool gives, and purchase bills post **Inventory / supplier** directly, in the sales or purchase voucher (below). Stock receipts entered under Stock without a bill stay separate: a later bill for the same goods would count them twice, so record bought goods either as a purchase bill or as a stock receipt, not both.

## Sales and purchases (implemented in Phase 5)

Every sales invoice, credit note, purchase bill and debit note posts **one voucher** together with its stock movements, in the same transaction as its number. The voucher types are Sales, Credit note, Purchase and Debit note. The voucher records the document as its source and can only be reversed by cancelling the document. Settlements post Receipt or Payment vouchers.

**Calculation** (`BillingCalculator`, unit-tested):
- gross = quantity × rate, rounded to the paisa;
- discount = a percentage of gross (rounded) or a fixed amount;
- taxable = gross − discount. With tax-inclusive rates, taxable is derived so the line total equals the price charged;
- tax per line on the taxable value, at the product's tax category rate. CGST and SGST are each half the rate, rounded separately; IGST is the full rate;
- the total may be rounded to whole rupees, and the difference posts to **Round Off**.

The tax rate is configured data (Settings → Inventory → tax categories) and is stored on every posted line. Totals are always recalculated in the application layer, including when a draft is posted.

| Document | Debit | Credit |
|---|---|---|
| Sales invoice, credit | Party: total | Sales: taxable value; Output CGST + SGST, or Output IGST; Round Off if rounded up (debit if rounded down) |
| Sales invoice, cash | Cash or bank account: total | As above |
| Cost of goods (every sales invoice) | Cost of Goods Sold: stock value issued at the company's costing method | Inventory |
| Credit note (sales return) | Sales Returns: taxable value; Output GST: tax reversed; Round Off if rounded up | Party (credit mode) or the cash/bank account (cash mode): total |
| Goods returned to stock | Inventory: the cost the goods left with (in proportion; the last return of a line takes exactly what remains) | Cost of Goods Sold |
| Purchase bill | Inventory: taxable value (the stock value); Input CGST + SGST or Input IGST; Round Off if rounded up | Party (credit) or cash/bank account (cash): total |
| Debit note (purchase return) | Party or cash/bank account: total | Inventory: book cost of the goods (costing method); Input GST reversed; Inventory Adjustments: taxable value charged back minus book cost (debit if negative) |
| Customer receipt | Cash or bank account | Party |
| Customer refund | Party | Cash or bank account |
| Supplier payment | Party | Cash or bank account |
| Supplier refund | Cash or bank account | Party |
| Any of the above when stock was negative | Inventory Adjustments: cost difference of the settled units (see "Negative stock") | |

Amounts on the same account are netted into one line.

**Cancelling** a document reverses its voucher and its stock movement (an exact stock reversal), and records the reason. It is refused in these cases:
- the document is not the latest stock activity of its products;
- a receipt, payment or refund is allocated to it;
- returns exist against it.

Use a return instead.

**Open items and allocation.** Invoices and bills (and refunds) are *charges*. Receipts, payments and returns are *credits*. An allocation applies part of a credit to a charge of the same party:
- allocations are append-only;
- an allocation stops counting when either side is cancelled or reversed;
- a credit-mode return is allocated to its original document automatically, up to what is outstanding;
- money not allocated stays as an advance or unapplied credit.

Payment status (Unpaid, Partly paid, Paid) is derived from the open amount. A party's ledger balance equals its open charges minus its open credits, unless manual vouchers are posted to the party ledger. Tests check this.

**Not implemented:** dated GST rates, cess, reverse charge, ineligible input tax, freight and other charges, settlement discounts (Discount Allowed and Discount Received are not posted automatically), purchase of services or non-stock items through bills (use Purchase vouchers), and TDS/TCS.

## Expenses and service (implemented in Phase 7)

### Expenses

An expense is approved before it posts (`ExpenseService`). Posting creates **one voucher of type Expense** through the ledger poster, in the same transaction as its number (`EXP/{FY}/`). The source is the expense, so the voucher cannot be reversed from Vouchers.

| Line | Debit | Credit |
|---|---|---|
| Each line, GST claimable | The category's expense ledger: amount before GST | |
| | Input CGST + Input SGST (supplier in the company's state, or no supplier), or Input IGST (supplier in another state): the line's tax | |
| Each line, GST not claimable ("Claim GST" unticked) | The category's expense ledger: amount **plus** the tax | |
| The expense | | The cash or bank account it was paid from: total |

- Tax is calculated per line by the service from the line's tax category (CGST and SGST each half the rate, rounded separately; IGST the full rate). UI totals are never used.
- Amounts on the same account are netted into one line, and the service checks that debits equal the total before posting.
- **Reversal** is the only correction. It posts the exact reverse of the voucher on a date on or after the expense, once, with a reason. Posted expenses, their lines and their attachments cannot change (domain, data context and SQLite triggers).
- **Recurring expenses** create drafts only. They post through the same approval and posting path.

### Service jobs

| Event | Debit | Credit |
|---|---|---|
| Part issued, chargeable job | Cost of Goods Sold: the part's stock value at the costing method | Inventory |
| Part issued, warranty job | Warranty and Service Costs: the part's stock value | Inventory |
| Part returned to stock (unbilled parts only) | Inventory: the stock value received | The account the part was charged to (Cost of Goods Sold or Warranty and Service Costs): the cost it left with. Any difference from the pool's stock value goes to Inventory Adjustments, as for other receipts. |
| Service invoice (posted from Sales invoices) | Party (credit) or cash/bank (cash): total | Sales: taxable value of the issued parts and the labour; Output CGST + SGST or IGST by place of supply; Round Off if rounded |

- **Parts move stock once.** The issue is a `ServiceIssue` stock document posted through the stock engine (negative-stock, batch and serial rules apply), with its voucher in the same transaction. On the invoice, parts are *issued-part* lines: they carry the selling price and tax but post no stock movement and no Cost of Goods Sold. Labour is *service* lines (no product, no stock, SAC code as entered).
- **Warranty jobs** are not invoiced (zero charge to the customer). Their parts' cost stays in Warranty and Service Costs, which appears in profit and loss under Indirect Expenses.
- A part's charge (chargeable or warranty) is fixed when it is issued; the job's charge cannot change once parts are issued.
- Service stock documents and their vouchers cannot be posted or reversed from Stock or Vouchers. Corrections are part returns (before invoicing) or a credit note on the posted invoice.
- **Credit limit (Phase 7):** before a credit sales invoice posts, the customer's ledger balance plus the invoice total is compared with the credit limit (0 = no limit). Above the limit, posting is refused unless the user holds "Sales: Override credit limit" and confirms. The override is recorded in the audit trail as `CreditLimitOverridden`. It does not change any posting.
- **Customer prices (Phase 7)** only change the rate used when a line's rate is left blank. The posting rules above are unchanged.

## Not implemented yet

Several items from section H of the brief are not built yet:

- **Phase 8 reporting work:** cash flow statement, bank reconciliation, cost and profit centres, receivable and payable ageing, bill-wise allocation, and ledger export.
- **Phase 5 (done):** customer and supplier ledgers are created automatically with each party. Ledgers created by hand under Sundry Debtors or Creditors still work in vouchers but are not parties.
- **Performance at scale:** reports aggregate in memory over the company's ledger entries. Amounts are stored as integer paise, so they can move to SQL aggregation (`SUM`) when volumes require it.
