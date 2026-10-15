using Nexora.Accounting;
using Nexora.Application.Billing;
using Nexora.Domain.Billing;
using Nexora.Domain.Inventory;
using Nexora.Reporting.Reports;
using Nexora.Tests.Billing;

namespace Nexora.Tests.Phase9;

/// <summary>
/// The whole chain the brief names: purchase → stock → sale → cost of goods sold → reports, under FIFO, with a return,
/// a payment and a receipt. Every report figure is checked against the ledger it comes from.
/// </summary>
public sealed class EndToEndTests : BillingTestBase
{
    protected override CostingMethod Costing => CostingMethod.Fifo;

    [Fact]
    public async Task Purchase_stock_sale_cogs_and_reports_agree_end_to_end()
    {
        // The fixture received 20 LED-18W at 100. Buy 10 more at 130: FIFO layers 20 @ 100, then 10 @ 130.
        var (bill, _) = await BuyAsync("PW/77", new BillingLineInput(Led, 10, Rate: 130));
        Assert.Equal(30m, (await Stock.BalancesAsync()).Where(b => b.ProductId == Led).Sum(b => b.Quantity));

        // Sell 25: 20 from the first layer and 5 from the second, so cost = 2,000 + 650.
        var (invoice, _) = await SellAsync(Local, new BillingLineInput(Led, 25));
        Assert.Equal(2_650m, (await Billing.GetAsync(invoice)).CostOfGoods);

        // One comes back at its share of the cost the line left with: 2,650 / 25 = 106 (see ACCOUNTING_MAP, goods returned to stock).
        var (credit, _) = await ReturnAsync(invoice, (1, 1));
        Assert.Equal(106m, (await Billing.GetAsync(credit)).CostOfGoods);

        await Settlements.PostAsync(new SettlementInput(SettlementKind.CustomerReceipt, October, Local, CashAccount, 1_000, PaymentMethod.Cash, [new AllocationInput(invoice, 1_000)]));
        await Settlements.PostAsync(new SettlementInput(SettlementKind.SupplierPayment, October, Vendor, CashAccount, 500, PaymentMethod.Cash, [new AllocationInput(bill, 500)]));

        var cogs = await LedgerAsync(SystemAccounts.CostOfGoodsSold, October);
        Assert.Equal(2_650m - 106m, cogs);
        var stockLeft = 2_000m + 1_300m - 2_650m + 106m;
        Assert.Equal(stockLeft, await LedgerAsync(SystemAccounts.Inventory, October));
        Assert.True((await Stock.ReconcileAsync(October)).IsReconciled);

        var reports = Db.Get<ReportService>();
        var month = new ReportRequest { From = new DateOnly(2026, 10, 1), To = new DateOnly(2026, 10, 31), AsOf = new DateOnly(2026, 10, 31), PageSize = 0 };
        decimal Total(ReportResult r, string key) => (decimal)r.Totals![r.Columns.ToList().FindIndex(c => c.Key == key)]!;

        var valuation = await reports.RunAsync("inventory.valuation", month);
        Assert.Equal(stockLeft, Total(valuation, "value"));
        Assert.Equal(6m, Total(valuation, "quantity"));

        var sales = -(await LedgerAsync(SystemAccounts.Sales, October)) - await LedgerAsync(SystemAccounts.SalesReturns, October);
        Assert.Equal(25 * 150m - 150m, sales);
        Assert.Equal(sales, Total(await reports.RunAsync("sales.register", month), "taxable"));
        var products = await reports.RunAsync("sales.products", month);
        Assert.Equal(cogs, Total(products, "cost"));
        Assert.Equal(sales - cogs, Total(products, "margin"));

        var profit = await reports.RunAsync("profit.periods", month);
        Assert.Equal(sales - cogs, Total(profit, "gross"));
        var pl = await Db.Get<LedgerService>().ProfitAndLossAsync(month.From!.Value, month.To!.Value);
        Assert.Equal(pl.NetProfit, Total(profit, "net"));

        var debtors = await reports.RunAsync("ageing.debtors", month);
        Assert.Equal(await PartyLedgerAsync(Local) + await PartyLedgerAsync(Bengaluru), Total(debtors, "balance"));
        var creditors = await reports.RunAsync("ageing.creditors", month);
        Assert.Equal(await PartyLedgerAsync(Vendor), Total(creditors, "balance"));   // party balances are shown as amounts owed

        var tb = await Db.Get<LedgerService>().TrialBalanceAsync(October);
        Assert.True(tb.IsBalanced);
        var dashboard = (await Db.Workspace.GetDashboardAsync()).Metrics.ToDictionary(m => m.Label, m => m.Value);
        Assert.Equal(sales, dashboard["Sales"]);
        Assert.Equal(stockLeft, dashboard["Inventory value"]);
        await AssertBooksBalanceAsync();
    }

    [Fact]
    public async Task Invoices_posted_at_the_same_time_get_unique_gapless_numbers_and_never_oversell()
    {
        // 8 invoices of 3 each race for 20 in stock: at most 6 can post, and their numbers have no gaps.
        var drafts = new List<Guid>();
        for (var i = 0; i < 8; i++) drafts.Add(await DraftAsync(BillingDocumentKind.SalesInvoice, Local, new BillingLineInput(Led, 3)));

        var outcomes = await Task.WhenAll(drafts.Select(id => Task.Run(async () =>
        {
            try
            {
                return await Billing.PostAsync(id);
            }
            catch (Nexora.Application.WorkspaceValidationException)
            {
                return null;
            }
        })));

        var numbers = outcomes.Where(n => n is not null).Select(n => n!).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(6, numbers.Count);
        Assert.Equal(numbers.Count, numbers.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 6).Select(n => $"INV/2026-27/{n:0000}"), numbers);
        Assert.Equal(2m, (await Stock.BalancesAsync()).Where(b => b.ProductId == Led).Sum(b => b.Quantity));
        Assert.True((await Stock.ReconcileAsync(October)).IsReconciled);
        await AssertBooksBalanceAsync();
    }
}
