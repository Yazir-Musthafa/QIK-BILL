using Nexora.Application;
using Nexora.Domain.Accounting;
using Nexora.Domain.Companies;

namespace Nexora.Accounting;

/// <summary>
/// Stable keys of the accounts that posting rules depend on. The mapping of business events to these
/// accounts is documented in docs/ACCOUNTING_MAP.md; changing a key requires a data migration.
/// </summary>
public static class SystemAccounts
{
    public const string Cash = "cash";
    public const string BankAccounts = "bank-accounts";
    public const string SundryDebtors = "sundry-debtors";
    public const string SundryCreditors = "sundry-creditors";
    public const string Inventory = "inventory";
    public const string InputCgst = "input-cgst";
    public const string InputSgst = "input-sgst";
    public const string InputIgst = "input-igst";
    public const string InputCess = "input-cess";
    public const string OutputCgst = "output-cgst";
    public const string OutputSgst = "output-sgst";
    public const string OutputIgst = "output-igst";
    public const string OutputCess = "output-cess";
    public const string ReverseChargePayable = "rcm-payable";
    public const string Capital = "capital";
    public const string RetainedEarnings = "retained-earnings";
    public const string OpeningBalanceDifference = "opening-difference";
    public const string Sales = "sales";
    public const string SalesReturns = "sales-returns";
    public const string DiscountReceived = "discount-received";
    public const string Purchases = "purchases";
    public const string PurchaseReturns = "purchase-returns";
    public const string CostOfGoodsSold = "cogs";
    public const string FreightInward = "freight-inward";
    public const string DiscountAllowed = "discount-allowed";
    public const string RoundOff = "round-off";
    public const string BankCharges = "bank-charges";
    public const string InventoryAdjustment = "inventory-adjustment";
    public const string WarrantyExpense = "warranty-expense";
}

/// <summary>The default Indian chart of accounts given to every company.</summary>
public static class ChartOfAccountsTemplate
{
    public sealed record Node(string Code, string Name, AccountNature Nature, AccountKind Kind, string? ParentCode, string? SystemKey = null);

    public static IReadOnlyList<Node> Nodes { get; } =
    [
        G("1000", "Assets", AccountNature.Asset, null),
        G("1100", "Current Assets", AccountNature.Asset, "1000"),
        G("1110", "Cash-in-Hand", AccountNature.Asset, "1100"),
        L("1111", "Cash", AccountNature.Asset, AccountKind.Cash, "1110", SystemAccounts.Cash),
        G("1120", "Bank Accounts", AccountNature.Asset, "1100", SystemAccounts.BankAccounts),
        L("1121", "Primary Bank Account", AccountNature.Asset, AccountKind.Bank, "1120"),
        G("1130", "Sundry Debtors", AccountNature.Asset, "1100", SystemAccounts.SundryDebtors),
        G("1140", "Stock-in-Hand", AccountNature.Asset, "1100"),
        L("1141", "Inventory", AccountNature.Asset, AccountKind.Inventory, "1140", SystemAccounts.Inventory),
        G("1150", "GST Input Credit", AccountNature.Asset, "1100"),
        L("1151", "Input CGST", AccountNature.Asset, AccountKind.Tax, "1150", SystemAccounts.InputCgst),
        L("1152", "Input SGST", AccountNature.Asset, AccountKind.Tax, "1150", SystemAccounts.InputSgst),
        L("1153", "Input IGST", AccountNature.Asset, AccountKind.Tax, "1150", SystemAccounts.InputIgst),
        L("1154", "Input Cess", AccountNature.Asset, AccountKind.Tax, "1150", SystemAccounts.InputCess),
        G("1160", "Loans and Advances (Asset)", AccountNature.Asset, "1100"),
        G("1200", "Fixed Assets", AccountNature.Asset, "1000"),

        G("2000", "Liabilities", AccountNature.Liability, null),
        G("2100", "Current Liabilities", AccountNature.Liability, "2000"),
        G("2110", "Sundry Creditors", AccountNature.Liability, "2100", SystemAccounts.SundryCreditors),
        G("2120", "Duties and Taxes", AccountNature.Liability, "2100"),
        L("2121", "Output CGST", AccountNature.Liability, AccountKind.Tax, "2120", SystemAccounts.OutputCgst),
        L("2122", "Output SGST", AccountNature.Liability, AccountKind.Tax, "2120", SystemAccounts.OutputSgst),
        L("2123", "Output IGST", AccountNature.Liability, AccountKind.Tax, "2120", SystemAccounts.OutputIgst),
        L("2124", "Output Cess", AccountNature.Liability, AccountKind.Tax, "2120", SystemAccounts.OutputCess),
        L("2125", "GST Payable under Reverse Charge", AccountNature.Liability, AccountKind.Tax, "2120", SystemAccounts.ReverseChargePayable),
        G("2130", "Provisions", AccountNature.Liability, "2100"),
        G("2200", "Loans (Liability)", AccountNature.Liability, "2000"),

        G("3000", "Equity", AccountNature.Equity, null),
        G("3100", "Capital Account", AccountNature.Equity, "3000"),
        L("3101", "Owner's Capital", AccountNature.Equity, AccountKind.General, "3100", SystemAccounts.Capital),
        G("3200", "Reserves and Surplus", AccountNature.Equity, "3000"),
        L("3201", "Retained Earnings", AccountNature.Equity, AccountKind.General, "3200", SystemAccounts.RetainedEarnings),
        L("3202", "Opening Balance Difference", AccountNature.Equity, AccountKind.General, "3200", SystemAccounts.OpeningBalanceDifference),

        G("4000", "Income", AccountNature.Income, null),
        G("4100", "Sales Accounts", AccountNature.Income, "4000"),
        L("4101", "Sales", AccountNature.Income, AccountKind.General, "4100", SystemAccounts.Sales),
        L("4102", "Sales Returns", AccountNature.Income, AccountKind.General, "4100", SystemAccounts.SalesReturns),
        G("4200", "Direct Incomes", AccountNature.Income, "4000"),
        G("4300", "Indirect Incomes", AccountNature.Income, "4000"),
        L("4301", "Discount Received", AccountNature.Income, AccountKind.General, "4300", SystemAccounts.DiscountReceived),
        L("4302", "Interest Received", AccountNature.Income, AccountKind.General, "4300"),

        G("5000", "Expenses", AccountNature.Expense, null),
        G("5100", "Purchase Accounts", AccountNature.Expense, "5000"),
        L("5101", "Purchases (non-stock)", AccountNature.Expense, AccountKind.General, "5100", SystemAccounts.Purchases),
        L("5102", "Purchase Returns (non-stock)", AccountNature.Expense, AccountKind.General, "5100", SystemAccounts.PurchaseReturns),
        G("5200", "Direct Expenses", AccountNature.Expense, "5000"),
        L("5201", "Cost of Goods Sold", AccountNature.Expense, AccountKind.General, "5200", SystemAccounts.CostOfGoodsSold),
        L("5202", "Freight Inward", AccountNature.Expense, AccountKind.General, "5200", SystemAccounts.FreightInward),
        G("5300", "Indirect Expenses", AccountNature.Expense, "5000"),
        L("5301", "Discount Allowed", AccountNature.Expense, AccountKind.General, "5300", SystemAccounts.DiscountAllowed),
        L("5302", "Round Off", AccountNature.Expense, AccountKind.General, "5300", SystemAccounts.RoundOff),
        L("5303", "Bank Charges", AccountNature.Expense, AccountKind.General, "5300", SystemAccounts.BankCharges),
        L("5304", "Inventory Adjustments", AccountNature.Expense, AccountKind.General, "5300", SystemAccounts.InventoryAdjustment),
        L("5305", "Rent", AccountNature.Expense, AccountKind.General, "5300"),
        L("5306", "Salaries and Wages", AccountNature.Expense, AccountKind.General, "5300"),
        L("5307", "Electricity", AccountNature.Expense, AccountKind.General, "5300"),
        L("5308", "Office Expenses", AccountNature.Expense, AccountKind.General, "5300"),
        L("5309", "Warranty and Service Costs", AccountNature.Expense, AccountKind.General, "5300", SystemAccounts.WarrantyExpense),
    ];

    public static IReadOnlyList<Account> Create(Guid companyId)
    {
        var byCode = new Dictionary<string, Account>();
        foreach (var node in Nodes)
        {
            Guid? parent = node.ParentCode is null ? null : byCode[node.ParentCode].Id;
            byCode[node.Code] = new Account(Guid.NewGuid(), companyId, node.Code, node.Name, node.Nature, node.Kind, parent, node.SystemKey);
        }

        return byCode.Values.ToList();
    }

    private static Node G(string code, string name, AccountNature nature, string? parent, string? key = null) =>
        new(code, name, nature, AccountKind.Group, parent, key);

    private static Node L(string code, string name, AccountNature nature, AccountKind kind, string parent, string? key = null) =>
        new(code, name, nature, kind, parent, key);
}

/// <summary>Gives each new (or upgraded) company the default chart and its first financial year.</summary>
public sealed class AccountingSeedContributor : ICompanySeedContributor
{
    public Type MarkerType => typeof(Account);

    public IEnumerable<object> Seed(Company company, DateTime nowUtc)
    {
        foreach (var account in ChartOfAccountsTemplate.Create(company.Id)) yield return account;
        yield return new FinancialYearPeriod(Guid.NewGuid(), company.Id, company.FinancialYearStartYear);
    }
}
