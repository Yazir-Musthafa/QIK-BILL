using Nexora.Accounting;
using Nexora.Application;
using Nexora.Domain.Billing;
using Nexora.Reporting.Reports;

namespace Nexora.Tests.Phase8;

public sealed class DashboardTests : ReportTestBase
{
    private async Task<Dictionary<string, decimal>> MetricsAsync() => (await Db.Workspace.GetDashboardAsync()).Metrics.ToDictionary(m => m.Label, m => m.Value);

    [Fact]
    public async Task Figures_come_from_posted_data_and_match_the_reports()
    {
        var snapshot = await Db.Workspace.GetDashboardAsync();
        var metrics = snapshot.Metrics.ToDictionary(m => m.Label, m => m.Value);
        var today = new DateOnly(2026, 10, 10);
        var tb = await Db.Get<LedgerService>().TrialBalanceAsync(today);

        Assert.True(snapshot.HasData);
        Assert.False(snapshot.IsDemonstration);
        Assert.Equal(-(await LedgerAsync(SystemAccounts.Sales, today)) - await LedgerAsync(SystemAccounts.SalesReturns, today), metrics["Sales"]);
        Assert.Equal((await Parties.ListAsync(PartyKind.Customer)).Sum(p => p.LedgerBalance), metrics["Receivables"]);
        Assert.Equal((await Parties.ListAsync(PartyKind.Supplier)).Sum(p => p.LedgerBalance), metrics["Payables"]);
        Assert.Equal(await LedgerAsync(SystemAccounts.Inventory, today), metrics["Inventory value"]);
        var pl = await Db.Get<LedgerService>().ProfitAndLossAsync(new DateOnly(2026, 10, 1), today);
        Assert.Equal(pl.NetProfit, metrics["Net profit"]);
        var cash = (await Db.Get<ChartOfAccountsService>().ListAsync()).Where(a => a.Kind is Nexora.Domain.Accounting.AccountKind.Cash or Nexora.Domain.Accounting.AccountKind.Bank).Select(a => a.Id).ToHashSet();
        Assert.Equal(tb.Rows.Where(r => cash.Contains(r.AccountId)).Sum(r => r.Net), metrics["Cash and bank"]);

        var ageing = await Reports.RunAsync("ageing.debtors", new ReportRequest { AsOf = today, PageSize = 0 });
        Assert.Equal(Total(ageing, "b0"), snapshot.ReceivablesAgeing[0].Value);
        Assert.Equal("LED-18W item", Assert.Single(snapshot.TopProducts).Name);
        Assert.Equal(6, snapshot.Trend.Count);
        Assert.Equal(pl.TotalIncome, snapshot.Trend[^1].Revenue);
        Assert.Contains(snapshot.RecentActivity, a => a.Title.StartsWith("Expense EXP/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Each_role_sees_only_the_figures_it_may_see()
    {
        await SignInAsAsync("bina", "Billing Operator");
        var billing = await MetricsAsync();
        Assert.Contains("Sales", billing.Keys);
        Assert.Contains("Receivables", billing.Keys);
        foreach (var hidden in new[] { "Purchases", "Payables", "Cash and bank", "Inventory value", "Gross profit", "Net profit" }) Assert.DoesNotContain(hidden, billing.Keys);
        Assert.Empty((await Db.Workspace.GetDashboardAsync()).Trend);

        await SignInAsAdministratorAsync();
        await SignInAsAsync("ravi", "Read-only");
        var readOnly = await MetricsAsync();
        Assert.Contains("Cash and bank", readOnly.Keys);
        Assert.DoesNotContain("Net profit", readOnly.Keys);          // no Inventory: View cost
        Assert.DoesNotContain("Inventory value", readOnly.Keys);
    }

    [Fact]
    public async Task The_demonstration_company_keeps_its_labelled_sample()
    {
        var demo = (await Db.Workspace.GetCompaniesAsync()).Single(c => c.IsDemo);
        await Db.Workspace.SwitchCompanyAsync(demo.Id);
        var snapshot = await Db.Workspace.GetDashboardAsync();
        Assert.True(snapshot.IsDemonstration);
        Assert.Equal(DemonstrationDashboard.DemoNotice, snapshot.Notice);
    }
}
