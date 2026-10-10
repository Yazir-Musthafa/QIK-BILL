namespace Nexora.Desktop.ViewModels;

/// <summary>A sidebar destination. <see cref="Phase"/> is set for modules that are not built yet.</summary>
public sealed record NavItem(string Key, string Label, string Glyph, string Group, int? Phase, string Description)
{
    public bool IsAvailable => Phase is null;
}

public static class NavCatalog
{
    public const string Dashboard = "dashboard";
    public const string Settings = "settings";
    public const string Help = "help";
    public const string Gallery = "gallery";

    // Segoe Fluent Icons / MDL2 code points shared by both fonts.
    public static IReadOnlyList<NavItem> Items { get; } =
    [
        new(Dashboard, "Dashboard", "", "Overview", null, "Business position at a glance."),
        new("sales.invoices", "Sales invoices", "", "Sales", 5, "Tax invoices, cash bills and credit sales with GST."),
        new("sales.quotations", "Quotations", "", "Sales", 5, "Enquiries, quotations, sales orders and delivery notes."),
        new("sales.customers", "Customers", "", "Sales", 5, "Customer profiles, prices, credit limits and outstanding balances."),
        new("purchases.invoices", "Purchase invoices", "", "Purchases", 5, "Purchase orders, goods receipts and supplier invoices."),
        new("purchases.suppliers", "Suppliers", "", "Purchases", 5, "Supplier profiles, statements and advances."),
        new("inventory.products", "Products", "", "Inventory", 4, "Products, variants, units, prices and barcodes."),
        new("inventory.stock", "Stock", "", "Inventory", 4, "Warehouses, movements, batches, serials and valuation."),
        new("accounts.vouchers", "Vouchers", "", "Accounts", 3, "Journal, payment, receipt and contra vouchers with double-entry posting."),
        new("accounts.ledgers", "Ledgers", "", "Accounts", 3, "Chart of accounts, ledgers, trial balance and financial statements."),
        new("accounts.expenses", "Expenses", "", "Accounts", 7, "Expense vouchers, categories, approvals and recurring expenses."),
        new("crm", "CRM", "", "Relationships", 7, "Leads, follow-ups, activity timeline and communication history."),
        new("service", "Service jobs", "", "Relationships", 7, "Service tickets, job cards, warranty and spare-part consumption."),
        new("reports", "Reports", "", "Insights", 8, "Sales, purchase, stock, GST and financial reports."),
        new("printing", "Print centre", "", "System", 6, "Printers, test pages, templates, labels and PDF export."),
        new(Settings, "Settings", "", "System", null, "Appearance and companies."),
        new(Help, "Help", "", "System", null, "Keyboard shortcuts and getting started."),
    ];

    /// <summary>Hidden destination; opened with Ctrl+Shift+G and not listed in the sidebar.</summary>
    public static NavItem GalleryItem { get; } =
        new(Gallery, "Component gallery", "", "System", null, "Every control and state in the design system.");

    public static NavItem Find(string key) =>
        key == Gallery ? GalleryItem : Items.First(i => i.Key == key);
}

public sealed record Shortcut(string Keys, string Action);

public sealed record QuickAction(string Label, string Glyph, string TargetKey);
