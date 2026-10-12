using Nexora.Domain.Security;

namespace Nexora.Reporting.Reports;

public enum ColumnKind
{
    Text,
    Date,
    Amount,
    Quantity,
    Integer,
    Percent,
}

/// <summary>A report column. <see cref="IsCost"/> columns (cost, margin, profit) are removed for users without Inventory: View cost.</summary>
public sealed record ReportColumn(string Key, string Header, ColumnKind Kind, bool Total = false, bool IsCost = false)
{
    public bool IsNumeric => Kind is ColumnKind.Amount or ColumnKind.Quantity or ColumnKind.Integer or ColumnKind.Percent;
}

public enum ReportParameter
{
    DateRange,
    AsOf,
    Period,
    Customer,
    Supplier,
    Product,
    Warehouse,
    Account,
    BankAccount,
    Branch,
    ExpenseCategory,
    ExpenseGrouping,
    CostCentre,
}

public enum ReportPeriod
{
    Day,
    Month,
    Year,
}

/// <summary>What a report is, who may run it and which parameters it takes.</summary>
public sealed record ReportDefinition(string Key, string Title, string Group, string Description, IReadOnlyList<string> Permissions,
    IReadOnlyList<ReportParameter> Parameters, bool PagedAtSource = false, bool IsGst = false)
{
    public bool Has(ReportParameter parameter) => Parameters.Contains(parameter);
}

/// <summary>Parameters of one run. Page is 1-based; a page size of 0 returns every row (used by exports).</summary>
public sealed record ReportRequest
{
    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public DateOnly? AsOf { get; init; }

    public ReportPeriod Period { get; init; } = ReportPeriod.Month;

    public Guid? PartyId { get; init; }

    public Guid? ProductId { get; init; }

    public Guid? WarehouseId { get; init; }

    public Guid? AccountId { get; init; }

    public Guid? BranchId { get; init; }

    public Guid? CategoryId { get; init; }

    public Guid? CostCentreId { get; init; }

    /// <summary>For reports with a choice of grouping, for example expenses by category, branch or payment method.</summary>
    public string? Grouping { get; init; }

    public string? Search { get; init; }

    public string? SortKey { get; init; }

    public bool SortDescending { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 200;
}

/// <summary>Where a row leads when opened: the source document or ledger.</summary>
public enum DrillKind
{
    BillingDocument,
    Settlement,
    Voucher,
    Ledger,
    Product,
    Party,
    Expense,
    ServiceJob,
    StockDocument,
}

public sealed record DrillTarget(DrillKind Kind, Guid Id);

/// <summary>One row: cells in column order (string, DateOnly, decimal, int or null). Emphasised rows are headings or subtotals and are left out of totals.</summary>
public sealed record ReportRow(IReadOnlyList<object?> Cells, DrillTarget? Drill = null, bool Emphasis = false, int Indent = 0);

public sealed record ReportResult(ReportDefinition Definition, string CompanyName, string ParameterText, IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<ReportRow> Rows, IReadOnlyList<object?>? Totals, int TotalRows, int Page, int PageSize, IReadOnlyList<string> Notes, bool IsDemo,
    DateTime GeneratedUtc)
{
    public int PageCount => PageSize <= 0 ? 1 : Math.Max(1, (TotalRows + PageSize - 1) / PageSize);
}

/// <summary>The catalogue of reports, in the order the Reports screen lists them.</summary>
public static class ReportCatalog
{
    private static ReportDefinition D(string key, string title, string group, string description, string[] permissions, ReportParameter[] parameters,
        bool paged = false, bool gst = false) => new(key, title, group, description, permissions, parameters, paged, gst);

    private static readonly ReportParameter[] Range = [ReportParameter.DateRange];

    public static IReadOnlyList<ReportDefinition> All { get; } =
    [
        D("sales.summary", "Sales by period", "Sales", "Daily, monthly or yearly sales after returns.", [Permissions.SalesView], [ReportParameter.DateRange, ReportParameter.Period]),
        D("sales.register", "Sales register", "Sales", "Every posted sales invoice and credit note with GST.", [Permissions.SalesView], [ReportParameter.DateRange, ReportParameter.Customer], paged: true, gst: true),
        D("sales.products", "Product-wise sales", "Sales", "Quantity, value, cost and margin by product.", [Permissions.SalesView], [ReportParameter.DateRange, ReportParameter.Customer]),
        D("sales.customers", "Customer-wise sales", "Sales", "Sales after returns by customer.", [Permissions.SalesView], Range),
        D("sales.salespeople", "Salesperson performance", "Sales", "Sales by the user who posted the invoice.", [Permissions.SalesView], Range),
        D("purchases.summary", "Purchases by period", "Purchases", "Daily, monthly or yearly purchases after returns.", [Permissions.PurchasesView], [ReportParameter.DateRange, ReportParameter.Period]),
        D("purchases.register", "Purchase register", "Purchases", "Every posted purchase bill and debit note with GST.", [Permissions.PurchasesView], [ReportParameter.DateRange, ReportParameter.Supplier], paged: true, gst: true),
        D("purchases.suppliers", "Supplier-wise purchases", "Purchases", "Purchases after returns by supplier.", [Permissions.PurchasesView], Range),
        D("profit.periods", "Gross and net profit", "Profit", "Sales, cost of goods sold, gross profit, other income, expenses and net profit by period, from the ledger.", [Permissions.AccountsView, Permissions.InventoryViewCost], [ReportParameter.DateRange, ReportParameter.Period]),
        D("inventory.valuation", "Inventory valuation", "Inventory", "Quantity and value of stock by product on a date.", [Permissions.InventoryView], [ReportParameter.AsOf, ReportParameter.Warehouse]),
        D("inventory.movements", "Stock movement", "Inventory", "Every stock movement with quantity and value.", [Permissions.InventoryView], [ReportParameter.DateRange, ReportParameter.Product, ReportParameter.Warehouse], paged: true),
        D("inventory.lowstock", "Low stock", "Inventory", "Products at or below their reorder level.", [Permissions.InventoryView], []),
        D("inventory.batches", "Expiry and batch", "Inventory", "Batches in stock with their expiry dates.", [Permissions.InventoryView], [ReportParameter.AsOf]),
        D("parties.receivables", "Receivables", "Receivables and payables", "What each customer owes.", [Permissions.SalesView], [ReportParameter.AsOf]),
        D("parties.payables", "Payables", "Receivables and payables", "What is owed to each supplier.", [Permissions.PurchasesView], [ReportParameter.AsOf]),
        D("ageing.debtors", "Debtor ageing", "Receivables and payables", "Open customer documents by age.", [Permissions.SalesView], [ReportParameter.AsOf]),
        D("ageing.creditors", "Creditor ageing", "Receivables and payables", "Open supplier documents by age.", [Permissions.PurchasesView], [ReportParameter.AsOf]),
        D("cash.book", "Cash book", "Cash and bank", "Receipts, payments and balance of cash accounts by day.", [Permissions.AccountsView], [ReportParameter.DateRange]),
        D("bank.book", "Bank book", "Cash and bank", "Receipts, payments and balance of bank accounts by day.", [Permissions.AccountsView], [ReportParameter.DateRange, ReportParameter.BankAccount]),
        D("bank.reconciliation", "Bank reconciliation statement", "Cash and bank", "Book balance, entries not yet on the statement, and the bank balance.", [Permissions.AccountsView], [ReportParameter.AsOf, ReportParameter.BankAccount]),
        D("expenses.summary", "Expenses", "Expenses", "Posted expenses by category, date, branch, department or payment method.", [Permissions.ExpensesView], [ReportParameter.DateRange, ReportParameter.ExpenseGrouping]),
        D("gst.summary", "GST summary", "GST", "Output and input GST by rate.", [Permissions.SalesView, Permissions.PurchasesView], Range, gst: true),
        D("gst.hsn", "HSN/SAC summary", "GST", "Outward and inward supplies by HSN or SAC code and rate.", [Permissions.SalesView, Permissions.PurchasesView], Range, gst: true),
        D("accounts.ledger", "Ledger statement", "Accounts", "One account's postings with a running balance.", [Permissions.AccountsView], [ReportParameter.DateRange, ReportParameter.Account], paged: true),
        D("accounts.trialbalance", "Trial balance", "Accounts", "Balances of every ledger on a date.", [Permissions.AccountsView], [ReportParameter.AsOf]),
        D("accounts.pl", "Profit and loss", "Accounts", "Income and expenses for a period.", [Permissions.AccountsView, Permissions.InventoryViewCost], Range),
        D("accounts.balancesheet", "Balance sheet", "Accounts", "Assets, liabilities and equity on a date.", [Permissions.AccountsView], [ReportParameter.AsOf]),
        D("accounts.cashflow", "Cash flow", "Accounts", "Cash and bank movements by operating, investing and financing activity (direct method).", [Permissions.AccountsView], Range),
        D("accounts.costcentres", "Cost and profit centres", "Accounts", "Income, expenses and result of each cost or profit centre.", [Permissions.AccountsView], Range),
        D("service.performance", "Service performance", "Service", "Jobs received, completed, turnaround and charges by technician.", [Permissions.ServiceView], Range),
    ];

    public static ReportDefinition Find(string key) =>
        All.FirstOrDefault(d => d.Key == key) ?? throw new Application.WorkspaceValidationException($"There is no report called '{key}'.");
}
