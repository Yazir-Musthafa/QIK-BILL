namespace Nexora.Domain.Security;

public sealed record PermissionInfo(string Key, string Module, string Action, string Description);

/// <summary>
/// The permission catalogue. Keys are stable strings stored with roles; never rename one without a migration.
/// Application services check these keys before every protected operation.
/// </summary>
public static class Permissions
{
    public const string CompanyView = "company.view";
    public const string CompanyManage = "company.manage";
    public const string CompanyCreate = "company.create";
    public const string UsersView = "users.view";
    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";
    public const string AuditView = "audit.view";

    public const string AccountsView = "accounts.view";
    public const string AccountsManage = "accounts.manage";
    public const string VouchersView = "vouchers.view";
    public const string VouchersCreate = "vouchers.create";
    public const string VouchersPost = "vouchers.post";
    public const string VouchersReverse = "vouchers.reverse";
    public const string FinancialYearsManage = "financial-years.manage";

    public const string ReportsView = "reports.view";
    public const string DataExport = "data.export";

    public const string SalesView = "sales.view";
    public const string SalesCreate = "sales.create";
    public const string SalesCancel = "sales.cancel";
    public const string SalesPriceOverride = "sales.price-override";
    public const string SalesDiscount = "sales.discount";
    public const string SalesCreditOverride = "sales.credit-override";
    public const string PurchasesView = "purchases.view";
    public const string PurchasesCreate = "purchases.create";
    public const string PurchasesCancel = "purchases.cancel";
    public const string InventoryView = "inventory.view";
    public const string InventoryManage = "inventory.manage";
    public const string InventoryAdjust = "inventory.adjust";
    public const string InventoryViewCost = "inventory.view-cost";
    public const string ExpensesView = "expenses.view";
    public const string ExpensesManage = "expenses.manage";
    public const string ExpensesApprove = "expenses.approve";
    public const string CrmView = "crm.view";
    public const string CrmManage = "crm.manage";
    public const string ServiceView = "service.view";
    public const string ServiceManage = "service.manage";
    public const string PrintingManage = "printing.manage";
    public const string BackupManage = "backup.manage";

    public static IReadOnlyList<PermissionInfo> All { get; } =
    [
        new(CompanyView, "Company", "View", "See the company profile, branches and warehouses."),
        new(CompanyManage, "Company", "Manage", "Edit the company profile, branches, warehouses and numbering."),
        new(CompanyCreate, "Company", "Create", "Create new companies on this computer."),
        new(UsersView, "Users", "View", "See users and their roles."),
        new(UsersManage, "Users", "Manage", "Add users, change roles, reset passwords, deactivate access."),
        new(RolesManage, "Users", "Manage roles", "Create roles and change their permissions."),
        new(AuditView, "Users", "View audit trail", "See who changed what and verify the audit trail."),
        new(AccountsView, "Accounts", "View", "See the chart of accounts, ledgers and trial balance."),
        new(AccountsManage, "Accounts", "Manage", "Create and edit accounts and opening balances."),
        new(VouchersView, "Vouchers", "View", "See vouchers."),
        new(VouchersCreate, "Vouchers", "Create", "Create and edit draft vouchers."),
        new(VouchersPost, "Vouchers", "Post", "Post vouchers to the ledgers."),
        new(VouchersReverse, "Vouchers", "Reverse", "Reverse posted vouchers."),
        new(FinancialYearsManage, "Accounts", "Financial years", "Open, close and reopen financial years."),
        new(ReportsView, "Reports", "View", "See reports."),
        new(DataExport, "Reports", "Export", "Export data and reports to files."),
        new(SalesView, "Sales", "View", "See sales documents."),
        new(SalesCreate, "Sales", "Create", "Create sales documents."),
        new(SalesCancel, "Sales", "Cancel", "Cancel sales documents."),
        new(SalesPriceOverride, "Sales", "Override price", "Change the selling price on a document."),
        new(SalesDiscount, "Sales", "Discount", "Give discounts up to the role's discount limit."),
        new(SalesCreditOverride, "Sales", "Override credit limit", "Post a credit sale that takes the customer above its credit limit."),
        new(PurchasesView, "Purchases", "View", "See purchase documents."),
        new(PurchasesCreate, "Purchases", "Create", "Create purchase documents."),
        new(PurchasesCancel, "Purchases", "Cancel", "Cancel purchase documents."),
        new(InventoryView, "Inventory", "View", "See products and stock."),
        new(InventoryManage, "Inventory", "Manage", "Create and edit products."),
        new(InventoryAdjust, "Inventory", "Adjust", "Record stock adjustments and transfers."),
        new(InventoryViewCost, "Inventory", "View cost", "See purchase cost and stock valuation."),
        new(ExpensesView, "Expenses", "View", "See expenses."),
        new(ExpensesManage, "Expenses", "Manage", "Record, submit and post expenses, and manage categories and recurring expenses."),
        new(ExpensesApprove, "Expenses", "Approve", "Approve or reject expenses submitted for approval."),
        new(CrmView, "CRM", "View", "See leads and contacts."),
        new(CrmManage, "CRM", "Manage", "Edit leads, contacts and follow-ups."),
        new(ServiceView, "Service", "View", "See service jobs."),
        new(ServiceManage, "Service", "Manage", "Create and update service jobs."),
        new(PrintingManage, "System", "Printing", "Change printer settings and templates."),
        new(BackupManage, "System", "Backup", "Create and restore backups."),
    ];

    private static readonly HashSet<string> Keys = All.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);

    public static bool IsKnown(string key) => Keys.Contains(key);
}
