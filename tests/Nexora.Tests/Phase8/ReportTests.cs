using Nexora.Accounting;
using Nexora.Application.Billing;
using Nexora.Application.Security;
using Nexora.Domain.Billing;
using Nexora.Reporting.Reports;
using Nexora.Reporting.Reports.Generators;
using Nexora.Tests.Phase7;

namespace Nexora.Tests.Phase8;

/// <summary>
/// A month of business on the billing fixture: credit sales in and out of state, a cash sale, a return, a cancelled
/// invoice, a purchase, a part-paid invoice, a customer advance, a supplier payment and a posted expense.
/// </summary>
public abstract class ReportTestBase : ExpenseTestBase
{
    protected static readonly DateOnly MonthStart = new(2026, 10, 1);
    protected static readonly DateOnly MonthEnd = new(2026, 10, 31);

    protected ReportService Reports => Db.Get<ReportService>();

    protected Guid InvoiceA { get; private set; }

    protected Guid Bill { get; private set; }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        (InvoiceA, _) = await SellAsync(Local, new BillingLineInput(Led, 4));
        await SellAsync(Bengaluru, new BillingLineInput(Led, 2));
        await CashSaleAsync(new BillingLineInput(Led, 1));
        await ReturnAsync(InvoiceA, (1, 1));
        var (cancelled, _) = await SellAsync(Local, new BillingLineInput(Led, 1));
        await Billing.CancelAsync(cancelled, October, "Wrong customer");
        (Bill, _) = await BuyAsync("PW/1", new BillingLineInput(Led, 10, Rate: 90));
        await Settlements.PostAsync(new SettlementInput(SettlementKind.CustomerReceipt, October, Local, CashAccount, 300, PaymentMethod.Cash, [new AllocationInput(InvoiceA, 300)]));
        await Settlements.PostAsync(new SettlementInput(SettlementKind.CustomerReceipt, October, Bengaluru, CashAccount, 100, PaymentMethod.Cash, []));
        await Settlements.PostAsync(new SettlementInput(SettlementKind.SupplierPayment, October, Vendor, CashAccount, 500, PaymentMethod.Cash, [new AllocationInput(Bill, 500)]));
        await Expenses.PostAsync(await ApprovedAsync(1_000));
    }

    protected Task<ReportResult> RunAsync(string key, ReportRequest? request = null) =>
        Reports.RunAsync(key, request ?? new ReportRequest { From = MonthStart, To = MonthEnd, AsOf = MonthEnd, PageSize = 0 });

    protected static decimal Total(ReportResult result, string column) => (decimal)result.Totals![Index(result, column)]!;

    protected static int Index(ReportResult result, string column) => result.Columns.ToList().FindIndex(c => c.Key == column) is var i and >= 0
        ? i : throw new InvalidOperationException($"No column {column} in {result.Definition.Key}.");

    protected static decimal Cell(ReportRow row, ReportResult result, string column) => (decimal)row.Cells[Index(result, column)]!;

    protected Task<decimal> LedgerNetAsync(string systemKey, DateOnly asOf) => LedgerAsync(systemKey, asOf);
}

public sealed class ReportCatalogueTests : ReportTestBase
{
    public static TheoryData<string> Keys()
    {
        var data = new TheoryData<string>();
        foreach (var d in ReportCatalog.All) data.Add(d.Key);
        return data;
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public async Task Every_report_runs_on_real_data(string key)
    {
        var bank = (await Db.Get<ChartOfAccountsService>().ListAsync()).Single(a => a.Name == "Primary Bank Account").Id;
        var definition = ReportCatalog.Find(key);
        var account = definition.Has(ReportParameter.BankAccount) ? bank : definition.Has(ReportParameter.Account) ? CashAccount : (Guid?)null;
        var result = await RunAsync(key, new ReportRequest { From = MonthStart, To = MonthEnd, AsOf = MonthEnd, AccountId = account, PageSize = 0 });

        Assert.Equal(definition.Title, result.Definition.Title);
        Assert.NotEmpty(result.Columns);
        Assert.All(result.Rows, r => Assert.Equal(result.Columns.Count, r.Cells.Count));
        if (definition.IsGst) Assert.Contains(ReportService.GstNote, result.Notes);
    }

    [Fact]
    public void Every_catalogue_entry_has_a_generator()
    {
        Assert.Equal(ReportCatalog.All.Count, Reports.Available().Count);
    }
}

public sealed class ReportReconciliationTests : ReportTestBase
{
    [Fact]
    public async Task Sales_register_total_equals_the_sales_ledgers()
    {
        var register = await RunAsync("sales.register");
        var ledger = -(await LedgerNetAsync(SystemAccounts.Sales, MonthEnd)) - await LedgerNetAsync(SystemAccounts.SalesReturns, MonthEnd);
        Assert.Equal(ledger, Total(register, "taxable"));
        Assert.Equal(-(await LedgerNetAsync(SystemAccounts.OutputCgst, MonthEnd)), Total(register, "cgst"));
        Assert.Equal(-(await LedgerNetAsync(SystemAccounts.OutputIgst, MonthEnd)), Total(register, "igst"));
        // The cancelled invoice shows twice (posted, then cancelled) and nets to nothing.
        var cancellation = Assert.Single(register.Rows, r => ((string)r.Cells[2]!).EndsWith("cancelled", StringComparison.Ordinal));
        Assert.Contains(register.Rows, r => r != cancellation && r.Cells[1]!.Equals(cancellation.Cells[1]) && Cell(r, register, "total") == -Cell(cancellation, register, "total"));

        var summary = await RunAsync("sales.summary");
        Assert.Equal(ledger, Total(summary, "net"));
        var customers = await RunAsync("sales.customers");
        Assert.Equal(ledger, Total(customers, "taxable"));
        var products = await RunAsync("sales.products");
        Assert.Equal(ledger, Total(products, "value"));
    }

    [Fact]
    public async Task Purchase_register_and_gst_summary_match_the_tax_ledgers()
    {
        var register = await RunAsync("purchases.register");
        Assert.Equal(900m, Total(register, "taxable"));
        var gst = await RunAsync("gst.summary");
        var output = gst.Rows.Single(r => ((string)r.Cells[0]!).StartsWith("Total output", StringComparison.Ordinal));
        var input = gst.Rows.Single(r => ((string)r.Cells[0]!).StartsWith("Total input", StringComparison.Ordinal));
        Assert.Equal(-(await LedgerNetAsync(SystemAccounts.OutputCgst, MonthEnd)), Cell(output, gst, "cgst"));
        Assert.Equal(-(await LedgerNetAsync(SystemAccounts.OutputIgst, MonthEnd)), Cell(output, gst, "igst"));
        Assert.Equal(await LedgerNetAsync(SystemAccounts.InputCgst, MonthEnd), Cell(input, gst, "cgst"));   // bill plus the claimable expense GST
        Assert.Equal(await LedgerNetAsync(SystemAccounts.InputSgst, MonthEnd), Cell(input, gst, "sgst"));

        var hsn = await RunAsync("gst.hsn");
        Assert.Equal(Cell(output, gst, "taxable"), hsn.Rows.Where(r => (string)r.Cells[0]! == "Outward").Sum(r => Cell(r, hsn, "taxable")));
    }

    [Fact]
    public async Task Valuation_equals_the_inventory_ledger()
    {
        var valuation = await RunAsync("inventory.valuation");
        Assert.Equal(await LedgerNetAsync(SystemAccounts.Inventory, MonthEnd), Total(valuation, "value"));
        Assert.Equal((await Stock.BalancesAsync()).Sum(b => b.Quantity), Total(valuation, "quantity"));
    }

    [Theory]
    [InlineData(2026, 10, 31)]
    [InlineData(2027, 1, 15)]
    public async Task Ageing_totals_equal_the_debtor_and_creditor_control_accounts(int year, int month, int day)
    {
        var asOf = new DateOnly(year, month, day);
        var request = new ReportRequest { AsOf = asOf, PageSize = 0 };
        var debtors = await RunAsync("ageing.debtors", request);
        var creditors = await RunAsync("ageing.creditors", request);
        var tb = await Db.Get<LedgerService>().TrialBalanceAsync(asOf);
        var parties = await Parties.ListAsync(PartyKind.Customer);
        var suppliers = await Parties.ListAsync(PartyKind.Supplier);

        Assert.Equal(tb.Rows.Where(r => parties.Any(p => p.AccountId == r.AccountId)).Sum(r => r.Net), Total(debtors, "balance"));
        Assert.Equal(-tb.Rows.Where(r => suppliers.Any(p => p.AccountId == r.AccountId)).Sum(r => r.Net), Total(creditors, "balance"));
        Assert.Equal(-100m, Total(debtors, "unapplied"));                      // Kaveri's advance
        Assert.Equal(0m, Total(debtors, "other"));
        var receivables = await RunAsync("parties.receivables", request);
        Assert.Equal(Total(debtors, "balance"), Total(receivables, "balance"));
        // On 15 January the 10 October invoices are 97 days old: the 91–180 column.
        var oldest = asOf.Month == 1 ? "b3" : "b0";
        Assert.Equal(Total(debtors, "balance") + 100m, Total(debtors, oldest));
    }

    [Fact]
    public async Task Statements_agree_with_the_trial_balance()
    {
        var tb = await RunAsync("accounts.trialbalance");
        Assert.Equal(Total(tb, "debit"), Total(tb, "credit"));
        var pl = await RunAsync("accounts.pl");
        var net = (decimal)pl.Rows.Last().Cells[2]!;
        var periods = await RunAsync("profit.periods");
        Assert.Equal(net, Total(periods, "net"));
        var bs = await RunAsync("accounts.balancesheet");
        var assets = (decimal)bs.Rows.Single(r => (string)r.Cells[0]! == "Total assets").Cells[2]!;
        var other = (decimal)bs.Rows.Single(r => (string)r.Cells[0]! == "Total liabilities and equity").Cells[2]!;
        Assert.Equal(assets, other);
        Assert.Equal(net, (decimal)bs.Rows.Single(r => (string)r.Cells[0]! == "Profit for the year to date").Cells[2]!);
    }

    [Fact]
    public async Task Cash_book_and_cash_flow_end_at_the_cash_and_bank_balances()
    {
        var cash = await RunAsync("cash.book");
        Assert.Equal(await LedgerNetAsync(SystemAccounts.Cash, MonthEnd), (decimal)cash.Rows.Last().Cells[5]!);
        var flow = await RunAsync("accounts.cashflow");
        var end = (decimal)flow.Rows.Last().Cells[2]!;
        var tb = await Db.Get<LedgerService>().TrialBalanceAsync(MonthEnd);
        var accounts = (await Db.Get<ChartOfAccountsService>().ListAsync()).Where(a => a.Kind is Nexora.Domain.Accounting.AccountKind.Cash or Nexora.Domain.Accounting.AccountKind.Bank)
            .Select(a => a.Id).ToHashSet();
        Assert.Equal(tb.Rows.Where(r => accounts.Contains(r.AccountId)).Sum(r => r.Net), end);
    }

    [Fact]
    public async Task Expense_report_matches_the_expense_ledgers()
    {
        var byCategory = await RunAsync("expenses.summary");
        Assert.Equal(await NetAsync(RentLedger), Total(byCategory, "amount"));
        var byMethod = await RunAsync("expenses.summary", new ReportRequest { From = MonthStart, To = MonthEnd, Grouping = "method", PageSize = 0 });
        Assert.Equal(Total(byCategory, "total"), Total(byMethod, "total"));
    }
}

public sealed class ReportBehaviourTests : ReportTestBase
{
    [Fact]
    public async Task Register_pages_in_the_database_and_keeps_whole_selection_totals()
    {
        var all = await RunAsync("sales.register");
        var page1 = await RunAsync("sales.register", new ReportRequest { From = MonthStart, To = MonthEnd, PageSize = 2, Page = 1 });
        var page2 = await RunAsync("sales.register", new ReportRequest { From = MonthStart, To = MonthEnd, PageSize = 2, Page = 2 });

        Assert.Equal(all.TotalRows, page1.TotalRows);
        Assert.Equal(2, page1.Rows.Count);
        Assert.Equal(Total(all, "total"), Total(page1, "total"));
        Assert.Equal((int)Math.Ceiling(all.TotalRows / 2.0), page1.PageCount);
        Assert.DoesNotContain(page2.Rows, r => page1.Rows.Any(p => p.Cells[1]!.Equals(r.Cells[1]) && p.Cells[2]!.Equals(r.Cells[2])));

        var kaveri = await RunAsync("sales.register", new ReportRequest { From = MonthStart, To = MonthEnd, Search = "kaveri", PageSize = 0 });
        Assert.All(kaveri.Rows, r => Assert.Equal("Kaveri Lights", r.Cells[3]));
        Assert.Equal(DrillKind.BillingDocument, kaveri.Rows[0].Drill!.Kind);
    }

    [Fact]
    public async Task Search_and_sort_work_on_aggregated_reports()
    {
        var sorted = await RunAsync("sales.customers", new ReportRequest { From = MonthStart, To = MonthEnd, SortKey = "party", PageSize = 0 });
        var names = sorted.Rows.Select(r => (string)r.Cells[0]!).ToList();
        Assert.Equal(names.Order(StringComparer.CurrentCultureIgnoreCase), names);
        var meera = await RunAsync("sales.customers", new ReportRequest { From = MonthStart, To = MonthEnd, Search = "meera", PageSize = 0 });
        Assert.Equal("Meera Electricals", Assert.Single(meera.Rows).Cells[0]);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(30, 0)]
    [InlineData(31, 1)]
    [InlineData(60, 1)]
    [InlineData(61, 2)]
    [InlineData(180, 3)]
    [InlineData(181, 4)]
    [InlineData(5000, 4)]
    public void Ageing_bucket_boundaries_are_inclusive(int days, int bucket)
    {
        Assert.Equal(bucket, AgeingGenerator.Bucket(days, [30, 60, 90, 180]));
    }

    [Fact]
    public async Task Cost_columns_are_removed_for_users_without_view_cost()
    {
        await SignInAsAsync("ravi", "Read-only");
        var products = await RunAsync("sales.products");
        Assert.DoesNotContain(products.Columns, c => c.IsCost);
        Assert.Contains(products.Notes, n => n.Contains("View cost", StringComparison.Ordinal));
        Assert.All(products.Rows, r => Assert.Equal(products.Columns.Count, r.Cells.Count));
    }

    [Fact]
    public async Task Reports_demand_their_permissions()
    {
        await SignInAsAsync("bina", "Billing Operator");
        await Assert.ThrowsAsync<AccessDeniedException>(() => RunAsync("sales.register"));   // no Reports: View
        Assert.Throws<AccessDeniedException>(() => Reports.Available());

        await SignInAsAdministratorAsync();
        await SignInAsAsync("ivan", "Inventory Manager");
        await Assert.ThrowsAsync<AccessDeniedException>(() => RunAsync("sales.register"));   // no Sales: View
        Assert.DoesNotContain(Reports.Available(), d => d.Key == "accounts.trialbalance");
        Assert.NotEmpty((await RunAsync("purchases.register")).Rows);

        await SignInAsAdministratorAsync();
        await SignInAsAsync("ravi", "Read-only");
        await Assert.ThrowsAsync<AccessDeniedException>(() => RunAsync("profit.periods"));   // needs Inventory: View cost
        Assert.NotEmpty((await RunAsync("sales.register")).Rows);

        await SignInAsAdministratorAsync();
        await SignInAsAsync("sam", "Service Staff");
        await Assert.ThrowsAsync<AccessDeniedException>(() => RunAsync("service.performance"));   // no Reports: View
    }

    [Fact]
    public async Task Another_company_sees_none_of_this_data()
    {
        var demo = (await Db.Workspace.GetCompaniesAsync()).Single(c => c.IsDemo);
        await Db.Workspace.SwitchCompanyAsync(demo.Id);
        var register = await RunAsync("sales.register");
        Assert.Empty(register.Rows);
        Assert.True(register.IsDemo);
        Assert.Contains(register.Notes, n => n.StartsWith("Demonstration company", StringComparison.Ordinal));
        Assert.Empty((await RunAsync("inventory.valuation")).Rows);
    }
}
