namespace Nexora.Desktop.ViewModels;

/// <summary>Short task instructions shared by the shell and Help; no business action depends on these hints.</summary>
public static class ScreenGuidance
{
    public static string For(string key) => key switch
    {
        "dashboard" => "Start with Products and Add Supplier, then record a Purchase to bring stock in. Use Sales invoices to sell it. The figures here come from posted transactions.",
        "sales.invoices" => "Choose a customer for credit or a registered business sale. For a walk-in consumer choose Cash under Payment. Select items and quantities, save a draft to check totals, then Save invoice. Receipts and returns are separate tabs.",
        "purchases.invoices" => "Choose a saved supplier, the bill date and warehouse, then enter items, quantities and purchase rates. Save a draft to check totals; Save purchase brings stock in. Use Payments to pay a credit bill.",
        "sales.customers" => "Choose New customer, enter a name and save. Contact details are optional. Add a valid GSTIN for B2B invoices; consumers can leave it blank. Select a saved customer to view balances and history.",
        "purchases.add-supplier" => "Enter the supplier name and save. The supplier number is automatic. Contact details are optional; add GSTIN and state when applicable. The supplier will then appear in Purchase.",
        "purchases.suppliers" => "Select a supplier. Profile shows their details; Bills and payments shows activity; Ledger shows the running balance; Journal shows the accounting entries your role can access.",
        "inventory.products" => "Choose New product and enter a code, name, unit and prices. Set GST and HSN/SAC when applicable. Optional details expand below. Save the product, then record a Purchase or opening stock to add quantities.",
        "inventory.stock" => "Balances shows what is in each warehouse. New document handles opening stock, transfers, counts and adjustments. Use Purchase for supplier bills and Sales invoices for sales. Documents and Movement ledger keep the history.",
        "accounts.chart" => "Select an account to review it. To add one, choose its parent group, enter a unique code and name, then Save. Groups organise accounts; transactions use ledger accounts.",
        "accounts.vouchers" => "Choose New voucher, type and date. Add accounts and debit/credit amounts until totals match, then save a draft or Post. Use sales, purchase and expense screens for their normal transactions. Optional cost centres expand per line.",
        "accounts.ledgers" => "Choose an account and dates within one financial year, then Show. Debit and credit entries explain the opening and closing balance.",
        "accounts.trial-balance" => "Choose an as-of date and Show. This lists every account's balance; the debit and credit totals should agree.",
        "accounts.statements" => "Choose dates and Show. Profit and loss explains income and expenses. The balance sheet shows assets, liabilities and equity at the end date.",
        "accounts.bank" => "Choose your bank and statement date. Enter the balance printed on the bank statement and match entries that have cleared. Include cleared entries to review or undo a match.",
        "accounts.costcentres" => "Create centres for departments or activities, then select them on income or expense voucher lines. Reports shows each centre's result.",
        "accounts.opening" => "Enter the balances you are bringing into this year's books, then review and post. Enter product quantities through Stock → Opening stock. An unmatched amount goes to Opening Balance Difference.",
        "accounts.expenses" => "Open New expense, choose date, payment account and method, then add category and amount. Save a draft or submit for approval. Select a saved expense to attach receipts, approve or post. Categories and Recurring remain available.",
        "crm" => "Record a lead's name and contact details, then Save lead. Use Follow-ups for the next task and due date. Convert a successful lead to a customer; existing matches can be linked. Enquiries and Customer view hold the wider history.",
        "service" => "Choose New job card, select the customer and describe the item and fault. Save it, then select the job to update its status. Spare parts and Labour expand when needed. Create an invoice and post it in Sales invoices before delivery.",
        "reports" => "Choose a report, set dates and Run. Preview, Print and export use the last completed run. GSTR-1 uses the full month or quarter and all customers; its JSON needs review before portal filing.",
        "printing" => "Select a printer, print a test page and record what came out. Preferences chooses printers and paper for each purpose. Labels supports product labels; History keeps previous print jobs.",
        "settings" => "Choose a section on the left. Start with Company profile, warehouses and Inventory. Users and Roles control access. Back up your workspace before importing data or restoring a backup.",
        "help" => "Use the task guide below or Ctrl+K to find a page. Expand optional sections as needed. Show all tools exposes the advanced pages allowed by your role.",
        "change-password" => "Enter your current password, choose a new password and repeat it to confirm. Use at least 10 characters and keep your username out of the password.",
        "gallery" => "This is the component reference. Check controls in both themes, including keyboard focus and expanded/collapsed sections.",
        _ => NavCatalog.Find(key).Description,
    };
}
