using Nexora.Application;
using Nexora.Application.Abstractions;
using Nexora.Application.Formatting;
using Nexora.Application.Security;
using Nexora.Domain.Accounting;
using Nexora.Domain.Billing;
using Nexora.Domain.Companies;
using Nexora.Domain.Expenses;
using Nexora.Domain.Inventory;
using Nexora.Domain.Reports;
using Nexora.Domain.Security;
using Nexora.Reporting.Reports.Generators;

namespace Nexora.Reporting.Dashboard;

/// <summary>
/// The dashboard of a real company, from posted data with the same rules as the reports. Each figure appears only for
/// roles that may see it: sales figures need Sales: View, purchases Purchases: View, balances Accounts: View, stock
/// Inventory: View, and profit and inventory value also Inventory: View cost. A company with nothing posted shows the
/// empty state.
/// </summary>
public sealed class DashboardSource(IUnitOfWorkFactory units, IAuthorizer authorizer, IClock clock) : IDashboardSource
{
    public async Task<DashboardSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        var session = authorizer.RequireSession();
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        if (!await unit.AnyAsync<LedgerEntry>(e => true, cancellationToken)) return DemonstrationDashboard.Empty();

        var today = DateOnly.FromDateTime(clock.UtcNow.ToLocalTime());
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var lastMonthStart = monthStart.AddMonths(-1);
        var lastMonthSameDay = DateOnly.FromDayNumber(Math.Min(lastMonthStart.AddDays(today.Day - 1).DayNumber, monthStart.AddDays(-1).DayNumber));
        bool Can(params string[] permissions) => permissions.All(session.Has);
        var canCost = Can(Permissions.InventoryViewCost);

        var metrics = new List<DashboardMetric>();
        if (Can(Permissions.SalesView))
        {
            var now = await NetAsync(unit, BillingFacts.Sales, monthStart, today, cancellationToken);
            var before = await NetAsync(unit, BillingFacts.Sales, lastMonthStart, lastMonthSameDay, cancellationToken);
            metrics.Add(new DashboardMetric("Sales", now, Delta(now, before)));
        }

        if (Can(Permissions.PurchasesView))
        {
            var now = await NetAsync(unit, BillingFacts.Purchases, monthStart, today, cancellationToken);
            var before = await NetAsync(unit, BillingFacts.Purchases, lastMonthStart, lastMonthSameDay, cancellationToken);
            metrics.Add(new DashboardMetric("Purchases", now, Delta(now, before)));
        }

        var accounts = await Lookups.AccountsAsync(unit, cancellationToken);
        if (Can(Permissions.SalesView))
        {
            var (parties, items) = await OpenItemsAsOf.LoadAsync(unit, PartyKind.Customer, today, cancellationToken);
            metrics.Add(new DashboardMetric("Receivables", parties.Sum(p => p.Balance), Count(items.Count(i => i.IsCharge), "open invoice")));
        }

        if (Can(Permissions.PurchasesView))
        {
            var (parties, items) = await OpenItemsAsOf.LoadAsync(unit, PartyKind.Supplier, today, cancellationToken);
            metrics.Add(new DashboardMetric("Payables", parties.Sum(p => p.Balance), Count(items.Count(i => i.IsCharge), "open bill")));
        }

        if (Can(Permissions.AccountsView))
        {
            var cashIds = accounts.Values.Where(a => a.IsCashOrBank).Select(a => a.Id).ToList();
            var cash = (await unit.QueryAsync(q => q.Set<LedgerEntry>().Where(e => cashIds.Contains(e.AccountId) && e.Date <= today).GroupBy(e => 1)
                .Select(g => g.Sum(e => e.Debit - e.Credit)), cancellationToken)).SingleOrDefault();
            metrics.Add(new DashboardMetric("Cash and bank", cash, Count(cashIds.Count, "account")));
        }

        if (Can(Permissions.InventoryView) && canCost)
        {
            var value = (await unit.QueryAsync(q => q.Set<StockMovement>().Where(m => m.Date <= today).GroupBy(m => 1).Select(g => g.Sum(m => m.Value)),
                cancellationToken)).SingleOrDefault();
            var method = (await unit.ListAsync<InventorySettings>(cancellationToken: cancellationToken)).SingleOrDefault()?.CostingMethod;
            metrics.Add(new DashboardMetric("Inventory value", value, method == CostingMethod.Fifo ? "At FIFO cost" : "At weighted average cost"));
        }

        var trend = new List<TrendPoint>();
        if (Can(Permissions.AccountsView) && canCost)
        {
            var (revenue, cost, income, expense) = await ProfitAsync(unit, accounts, monthStart, today, cancellationToken);
            var gross = revenue - cost;
            var net = revenue + income - cost - expense;
            metrics.Add(new DashboardMetric("Gross profit", gross, Margin(gross, revenue)));
            metrics.Add(new DashboardMetric("Net profit", net, Margin(net, revenue)));
            for (var m = 5; m >= 0; m--)
            {
                var start = monthStart.AddMonths(-m);
                var end = m == 0 ? today : start.AddMonths(1).AddDays(-1);
                var (r, c, i, e) = await ProfitAsync(unit, accounts, start, end, cancellationToken);
                trend.Add(new TrendPoint(start.ToString("MMM", System.Globalization.CultureInfo.InvariantCulture), r + i, c + e));
            }
        }

        var top = new List<ProductPerformance>();
        var ageing = new List<AgeingBucket>();
        if (Can(Permissions.SalesView))
        {
            top = await TopProductsAsync(unit, monthStart, today, cancellationToken);
            var settings = (await unit.ListAsync<ReportSettings>(cancellationToken: cancellationToken)).SingleOrDefault();
            var buckets = settings?.Buckets ?? ReportSettings.DefaultBuckets;
            var (_, items) = await OpenItemsAsOf.LoadAsync(unit, PartyKind.Customer, today, cancellationToken);
            var totals = new decimal[buckets.Count + 1];
            foreach (var item in items.Where(i => i.IsCharge)) totals[AgeingGenerator.Bucket(today.DayNumber - item.Date.DayNumber, buckets)] += item.Open;
            for (var b = 0; b <= buckets.Count; b++)
                ageing.Add(new AgeingBucket(b == 0 ? $"0–{buckets[0]} days" : b < buckets.Count ? $"{buckets[b - 1] + 1}–{buckets[b]} days" : $"Over {buckets[^1]} days", totals[b]));
        }

        var lowStock = Can(Permissions.InventoryView) ? await LowStockAsync(unit, cancellationToken) : [];
        var activity = await ActivityAsync(unit, session, cancellationToken);
        return new DashboardSnapshot(false, $"Figures from posted transactions up to {today:dd-MM-yyyy}. This month means {monthStart:dd-MM-yyyy} to today.",
            metrics, trend, activity, lowStock, top, ageing, HasData: true);
    }

    private static async Task<decimal> NetAsync(IUnitOfWork unit, BillingDocumentKind[] kinds, DateOnly start, DateOnly end, CancellationToken cancellationToken) =>
        (await unit.QueryAsync(q => BillingFacts.Query(q, kinds, start, end).GroupBy(f => new { f.Kind, f.IsCancellation })
            .Select(g => new { g.Key.Kind, g.Key.IsCancellation, Taxable = g.Sum(f => f.Taxable) }), cancellationToken))
        .Sum(g => BillingFacts.Sign(g.Kind, g.IsCancellation) * g.Taxable);

    /// <summary>Revenue (sales accounts and direct income), cost of sales (purchase accounts and direct expenses), other income and other expenses, from the ledger.</summary>
    private static async Task<(decimal Revenue, decimal Cost, decimal Income, decimal Expense)> ProfitAsync(IUnitOfWork unit, IReadOnlyDictionary<Guid, Account> accounts,
        DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        var totals = await unit.QueryAsync(q => q.Set<LedgerEntry>().Where(e => e.Date >= start && e.Date <= end).GroupBy(e => e.AccountId)
            .Select(g => new { AccountId = g.Key, Net = g.Sum(e => e.Debit - e.Credit) }), cancellationToken);
        Guid Group(string code) => accounts.Values.Single(a => a.IsGroup && a.Code == code).Id;
        var (sales, direct, purchases, directExpenses) = (Group("4100"), Group("4200"), Group("5100"), Group("5200"));
        decimal revenue = 0, cost = 0, income = 0, expense = 0;
        foreach (var t in totals)
        {
            if (!accounts.TryGetValue(t.AccountId, out var a)) continue;
            if (a.Nature == AccountNature.Income)
            {
                if (Lookups.Under(a, sales, accounts) || Lookups.Under(a, direct, accounts)) revenue -= t.Net;
                else income -= t.Net;
            }
            else if (a.Nature == AccountNature.Expense)
            {
                if (Lookups.Under(a, purchases, accounts) || Lookups.Under(a, directExpenses, accounts)) cost += t.Net;
                else expense += t.Net;
            }
        }

        return (revenue, cost, income, expense);
    }

    private static async Task<List<ProductPerformance>> TopProductsAsync(IUnitOfWork unit, DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        var kinds = BillingFacts.Sales;
        var lines = await unit.QueryAsync(q =>
            from f in BillingFacts.Query(q, kinds, start, end)
            join l in q.Set<BillingLine>() on f.Id equals l.DocumentId
            where l.ProductId != null
            group l by new { f.Kind, f.IsCancellation, l.ProductId } into g
            select new { g.Key.Kind, g.Key.IsCancellation, ProductId = g.Key.ProductId!.Value, Taxable = g.Sum(l => l.TaxableAmount) }, cancellationToken);
        var names = await ProductNames.LoadAsync(unit, cancellationToken);
        return lines.GroupBy(l => l.ProductId).Select(g => new ProductPerformance(names.TryGetValue(g.Key, out var p) ? p.Name : "?",
                g.Sum(l => BillingFacts.Sign(l.Kind, l.IsCancellation) * l.Taxable)))
            .Where(p => p.Revenue > 0).OrderByDescending(p => p.Revenue).Take(5).ToList();
    }

    private static async Task<List<StockAlert>> LowStockAsync(IUnitOfWork unit, CancellationToken cancellationToken)
    {
        var available = (await unit.QueryAsync(q => q.Set<StockMovement>().Where(m => m.State == StockState.Good).GroupBy(m => m.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(m => m.Quantity) }), cancellationToken)).ToDictionary(r => r.ProductId, r => r.Quantity);
        return (await ProductNames.LoadAsync(unit, cancellationToken)).Values
            .Where(p => !p.IsArchived && !p.HasChildren && p.ReorderLevel > 0 && available.GetValueOrDefault(p.Id) <= p.ReorderLevel)
            .OrderBy(p => available.GetValueOrDefault(p.Id) - p.ReorderLevel).Take(6)
            .Select(p => new StockAlert(p.Sku, p.Name, available.GetValueOrDefault(p.Id), p.ReorderLevel)).ToList();
    }

    /// <summary>The latest posted documents the user may see.</summary>
    private static async Task<List<ActivityItem>> ActivityAsync(IUnitOfWork unit, SessionInfo session, CancellationToken cancellationToken)
    {
        var kinds = new List<BillingDocumentKind>();
        if (session.Has(Permissions.SalesView)) kinds.AddRange(BillingFacts.Sales);
        if (session.Has(Permissions.PurchasesView)) kinds.AddRange(BillingFacts.Purchases);
        var items = new List<ActivityItem>();
        if (kinds.Count > 0)
        {
            var documents = await unit.QueryAsync(q => q.Set<BillingDocument>().Where(d => d.Status == BillingStatus.Posted && kinds.Contains(d.Kind) && d.PostedUtc != null)
                .OrderByDescending(d => d.PostedUtc).Take(8)
                .Select(d => new { d.Kind, d.Number, d.PartyName, d.Total, d.PostedUtc }), cancellationToken);
            items.AddRange(documents.Select(d => new ActivityItem(new DateTimeOffset(DateTime.SpecifyKind(d.PostedUtc!.Value, DateTimeKind.Utc)),
                $"{d.Kind.Label()} {d.Number}", $"{(d.PartyName.Length > 0 ? d.PartyName : "Walk-in")} · {IndianNumberFormat.Amount(d.Total, 2)}")));
        }

        if (session.Has(Permissions.ExpensesView))
        {
            var expenses = await unit.QueryAsync(q => q.Set<Expense>().Where(e => e.Status == ExpenseStatus.Posted && e.PostedUtc != null)
                .OrderByDescending(e => e.PostedUtc).Take(4).Select(e => new { e.Number, e.Total, e.PostedUtc }), cancellationToken);
            items.AddRange(expenses.Select(e => new ActivityItem(new DateTimeOffset(DateTime.SpecifyKind(e.PostedUtc!.Value, DateTimeKind.Utc)),
                $"Expense {e.Number}", IndianNumberFormat.Amount(e.Total, 2))));
        }

        return items.OrderByDescending(i => i.OccurredAt).Take(8).ToList();
    }

    private static string Delta(decimal now, decimal before) =>
        before == 0 ? "This month · nothing last month to compare" : $"This month · {(now - before) * 100 / Math.Abs(before):+0.0;-0.0;0.0}% vs same days last month";

    private static string Margin(decimal profit, decimal revenue) => revenue == 0 ? "This month" : $"This month · {profit * 100 / revenue:0.0}% of revenue";

    private static string Count(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";
}
