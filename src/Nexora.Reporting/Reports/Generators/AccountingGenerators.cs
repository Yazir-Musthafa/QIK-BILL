using Nexora.Accounting;
using Nexora.Application;
using Nexora.Domain.Accounting;

namespace Nexora.Reporting.Reports.Generators;

/// <summary>Revenue, cost of sales, gross profit, other income, other expenses and net profit by period, from the ledger.</summary>
internal sealed class ProfitPeriodsGenerator() : Generator("profit.periods")
{
    public override async Task<ReportData> RunAsync(ReportContext c, CancellationToken cancellationToken)
    {
        c.EnsureRange();
        var (start, end) = (c.From, c.To);
        var accounts = await Lookups.AccountsAsync(c.Unit, cancellationToken);
        var totals = await c.Unit.QueryAsync(q => q.Set<LedgerEntry>().Where(e => e.Date >= start && e.Date <= end).GroupBy(e => new { e.AccountId, e.Date })
            .Select(g => new { g.Key.AccountId, g.Key.Date, Net = g.Sum(e => e.Debit - e.Credit) }), cancellationToken);
        Guid Group(string code) => accounts.Values.Single(a => a.IsGroup && a.Code == code).Id;
        var trading = new[] { Group("4100"), Group("4200") };
        var costOfSales = new[] { Group("5100"), Group("5200") };

        var period = c.Request.Period;
        var rows = totals.Where(t => accounts.TryGetValue(t.AccountId, out var a) && !a.Nature.IsBalanceSheet())
            .GroupBy(t => Periods.Start(t.Date, period)).OrderBy(g => g.Key).Select(g =>
            {
                decimal revenue = 0, cost = 0, otherIncome = 0, otherExpense = 0;
                foreach (var t in g)
                {
                    var account = accounts[t.AccountId];
                    if (account.Nature == AccountNature.Income)
                    {
                        if (trading.Any(id => Lookups.Under(account, id, accounts))) revenue -= t.Net;
                        else otherIncome -= t.Net;
                    }
                    else if (costOfSales.Any(id => Lookups.Under(account, id, accounts))) cost += t.Net;
                    else otherExpense += t.Net;
                }

                var gross = revenue - cost;
                return new ReportRow([Periods.Label(g.Key, period), revenue, cost, gross, Margin(revenue, cost), otherIncome, otherExpense, gross + otherIncome - otherExpense]);
            }).ToList();

        return new ReportData(
        [
            Text("period", "Period"), Money("revenue", "Revenue (sales accounts and direct income)"), Money("cost", "Cost of sales", cost: true),
            Money("gross", "Gross profit", cost: true), Percent("grosspct", "Gross margin %", cost: true), Money("otherincome", "Other income"),
            Money("expenses", "Other expenses"), Money("net", "Net profit", cost: true),
        ], rows, c.RangeText, Notes: ["Cost of sales is everything under Purchase Accounts and Direct Expenses, including Cost of Goods Sold at the company's costing method."]);
    }
}

/// <summary>Daily receipts, payments and balance of cash or bank accounts.</summary>
internal sealed class CashBookGenerator(string key, AccountKind kind) : Generator(key)
{
    public override async Task<ReportData> RunAsync(ReportContext c, CancellationToken cancellationToken)
    {
        c.EnsureRange();
        var (start, end) = (c.From, c.To);
        var chosen = kind == AccountKind.Bank ? c.Request.AccountId : null;
        var accounts = (await c.Unit.ListAsync<Account>(a => a.Kind == kind && (chosen == null || a.Id == chosen), cancellationToken)).ToList();
        if (chosen is not null && accounts.Count == 0) throw new WorkspaceValidationException("Choose a bank account.");
        var ids = accounts.Select(a => a.Id).ToList();
        var opening = (await c.Unit.QueryAsync(q => q.Set<LedgerEntry>().Where(e => ids.Contains(e.AccountId) && e.Date < start).GroupBy(e => 1)
            .Select(g => g.Sum(e => e.Debit - e.Credit)), cancellationToken)).SingleOrDefault();
        var days = await c.Unit.QueryAsync(q => q.Set<LedgerEntry>().Where(e => ids.Contains(e.AccountId) && e.Date >= start && e.Date <= end).GroupBy(e => e.Date)
            .Select(g => new { Date = g.Key, In = g.Sum(e => e.Debit), Out = g.Sum(e => e.Credit), Count = g.Count() }), cancellationToken);

        var balance = opening;
        var rows = new List<ReportRow> { new([start, "Opening balance", null, null, null, opening], Emphasis: true) };
        foreach (var day in days.OrderBy(d => d.Date))
        {
            var dayOpening = balance;
            balance += day.In - day.Out;
            rows.Add(new ReportRow([day.Date, $"{day.Count} entries", dayOpening, day.In, day.Out, balance],
                accounts.Count == 1 ? new DrillTarget(DrillKind.Ledger, accounts[0].Id) : null));
        }

        rows.Add(new ReportRow([end, "Closing balance", null, null, null, balance], Emphasis: true));
        var receipts = days.Sum(d => d.In);
        var payments = days.Sum(d => d.Out);
        var names = accounts.Count == 1 ? accounts[0].Name : $"{accounts.Count} {(kind == AccountKind.Bank ? "bank" : "cash")} accounts";
        return new ReportData(
        [
            Date("date", "Date"), Text("detail", "Detail"), Money("opening", "Opening", total: false), Money("receipts", "Receipts"), Money("payments", "Payments"),
            Money("closing", "Closing", total: false),
        ], rows, $"{c.RangeText} · {names}", [null, null, null, receipts, payments, null]);
    }
}

internal sealed class BankReconciliationGenerator(BankReconciliationService bank) : Generator("bank.reconciliation")
{
    public override async Task<ReportData> RunAsync(ReportContext c, CancellationToken cancellationToken)
    {
        if (c.Request.AccountId is not { } accountId) throw new WorkspaceValidationException("Choose a bank account.");
        var r = await bank.GetAsync(accountId, c.AsOf, cancellationToken: cancellationToken);
        var rows = new List<ReportRow> { new([null, "Balance as per books", null, null, r.BookBalance], Emphasis: true) };
        rows.AddRange(r.Entries.Where(e => !e.IsCleared || e.ClearedOn > c.AsOf).Select(e =>
            new ReportRow([e.Date, $"{e.Voucher} · {e.Narration}", e.Deposit == 0 ? null : -e.Deposit, e.Withdrawal == 0 ? null : e.Withdrawal, null],
                new DrillTarget(DrillKind.Voucher, e.VoucherId))));
        rows.Add(new ReportRow([null, "Less deposits not yet on the statement", -r.UnclearedDeposits, null, null], Emphasis: true));
        rows.Add(new ReportRow([null, "Add withdrawals not yet on the statement", null, r.UnclearedWithdrawals, null], Emphasis: true));
        rows.Add(new ReportRow([null, "Balance as per bank (worked out)", null, null, r.BankBalance], Emphasis: true));
        return new ReportData([Date("date", "Date"), Text("detail", "Detail"), Money("deposits", "Deposits", total: false), Money("withdrawals", "Withdrawals", total: false),
            Money("balance", "Balance", total: false)], rows, $"{c.AsOfText} · {r.Account}",
            Notes: ["Compare the worked-out bank balance with your bank statement. Mark entries as cleared under Accounts → Bank reconciliation."]);
    }
}

/// <summary>One account's postings with a running balance, paged in the database.</summary>
internal sealed class LedgerGenerator(LedgerService ledgers) : Generator("accounts.ledger")
{
    public override async Task<ReportData> RunAsync(ReportContext c, CancellationToken cancellationToken)
    {
        c.EnsureRange();
        if (c.Request.AccountId is not { } accountId) throw new WorkspaceValidationException("Choose a ledger account.");
        var account = await c.Unit.FindAsync<Account>(accountId, cancellationToken) ?? throw new WorkspaceValidationException("That account no longer exists.");
        if (account.IsGroup) throw new WorkspaceValidationException("Choose a ledger account, not a group.");
        var (start, end) = (c.From, c.To);
        var columns = new List<ReportColumn> { Date("date", "Date"), Text("voucher", "Voucher"), Text("type", "Type"), Text("narration", "Narration"),
            Money("debit", "Debit"), Money("credit", "Credit"), Money("balance", "Balance", total: false) };

        // Income, expense and Retained Earnings restart each year, so their statements stay inside one financial year.
        if (!account.Nature.IsBalanceSheet() || account.SystemKey == SystemAccounts.RetainedEarnings)
        {
            var statement = await ledgers.StatementAsync(accountId, start, end, cancellationToken);
            var all = statement.Lines.Select(l => new ReportRow([l.Date, l.VoucherNumber, l.VoucherType.Label(), l.Narration, l.Debit, l.Credit, l.Balance],
                new DrillTarget(DrillKind.Voucher, l.VoucherId))).ToList();
            var pageRows = c.Request.PageSize > 0 ? all.Skip((c.Request.Page - 1) * c.Request.PageSize).Take(c.Request.PageSize).ToList() : all;
            pageRows.Insert(0, new ReportRow([start, "", "", "Opening balance", null, null, statement.OpeningBalance], Emphasis: true));
            return new ReportData(columns, pageRows, $"{account.Code} · {account.Name} · {c.RangeText}",
                [null, null, null, null, statement.Lines.Sum(l => l.Debit), statement.Lines.Sum(l => l.Credit), statement.ClosingBalance], all.Count);
        }

        var opening = (await c.Unit.QueryAsync(q => q.Set<LedgerEntry>().Where(e => e.AccountId == accountId && e.Date < start).GroupBy(e => 1)
            .Select(g => g.Sum(e => e.Debit - e.Credit)), cancellationToken)).SingleOrDefault();
        IQueryable<Line> Ordered(Application.Abstractions.IQuerySource q) =>
            from e in q.Set<LedgerEntry>()
            join v in q.Set<Voucher>() on e.VoucherId equals v.Id
            where e.AccountId == accountId && e.Date >= start && e.Date <= end
            orderby e.Date, v.PostedUtc, e.LineNumber, e.Id
            select new Line { Date = e.Date, VoucherId = v.Id, Number = v.Number ?? "", Type = v.Type, Narration = v.Narration, Debit = e.Debit, Credit = e.Credit };
        var summary = (await c.Unit.QueryAsync(q => Ordered(q).GroupBy(l => 1).Select(g => new { Count = g.Count(), Debit = g.Sum(l => l.Debit), Credit = g.Sum(l => l.Credit) }),
            cancellationToken)).SingleOrDefault();
        var (page, size) = (c.Request.Page, c.Request.PageSize);
        var skip = size > 0 ? (page - 1) * size : 0;
        var before = skip == 0 ? 0 : (await c.Unit.QueryAsync(q => Ordered(q).Take(skip).GroupBy(l => 1).Select(g => g.Sum(l => l.Debit - l.Credit)), cancellationToken)).SingleOrDefault();
        var lines = await c.Unit.QueryAsync(q => size > 0 ? Ordered(q).Skip(skip).Take(size) : Ordered(q), cancellationToken);

        var balance = opening + before;
        var rows = new List<ReportRow> { new([start, "", "", page == 1 ? "Opening balance" : "Brought forward", null, null, balance], Emphasis: true) };
        foreach (var l in lines)
        {
            balance += l.Debit - l.Credit;
            rows.Add(new ReportRow([l.Date, l.Number, l.Type.Label(), l.Narration, l.Debit, l.Credit, balance], new DrillTarget(DrillKind.Voucher, l.VoucherId)));
        }

        var closing = opening + (summary?.Debit ?? 0) - (summary?.Credit ?? 0);
        return new ReportData(columns, rows, $"{account.Code} · {account.Name} · {c.RangeText}",
            [null, null, null, null, summary?.Debit ?? 0, summary?.Credit ?? 0, closing], summary?.Count ?? 0);
    }

    internal sealed class Line
    {
        public DateOnly Date { get; init; }
        public Guid VoucherId { get; init; }
        public string Number { get; init; } = "";
        public VoucherType Type { get; init; }
        public string Narration { get; init; } = "";
        public decimal Debit { get; init; }
        public decimal Credit { get; init; }
    }
}

internal sealed class TrialBalanceGenerator(LedgerService ledgers) : Generator("accounts.trialbalance")
{
    public override async Task<ReportData> RunAsync(ReportContext c, CancellationToken cancellationToken)
    {
        var tb = await ledgers.TrialBalanceAsync(c.AsOf, cancellationToken);
        var rows = tb.Rows.Select(r => new ReportRow([r.Code, r.Name, r.Nature.ToString(), r.Debit, r.Credit], new DrillTarget(DrillKind.Ledger, r.AccountId))).ToList();
        return new ReportData([Text("code", "Code"), Text("account", "Account"), Text("nature", "Nature"), Money("debit", "Debit"), Money("credit", "Credit")], rows, c.AsOfText,
            Notes: tb.IsBalanced ? null : ["The trial balance does not balance. This should never happen; report it."]);
    }
}

internal sealed class ProfitAndLossGenerator(LedgerService ledgers) : Generator("accounts.pl")
{
    public override async Task<ReportData> RunAsync(ReportContext c, CancellationToken cancellationToken)
    {
        c.EnsureRange();
        var pl = await ledgers.ProfitAndLossAsync(c.From, c.To, cancellationToken);
        var rows = new List<ReportRow> { new(["Income", "", null], Emphasis: true) };
        rows.AddRange(pl.Income.Select(l => new ReportRow([l.Name, l.GroupName, l.Amount], new DrillTarget(DrillKind.Ledger, l.AccountId), Indent: 1)));
        rows.Add(new ReportRow(["Total income", "", pl.TotalIncome], Emphasis: true));
        rows.Add(new ReportRow(["Expenses", "", null], Emphasis: true));
        rows.AddRange(pl.Expenses.Select(l => new ReportRow([l.Name, l.GroupName, l.Amount], new DrillTarget(DrillKind.Ledger, l.AccountId), Indent: 1)));
        rows.Add(new ReportRow(["Total expenses", "", pl.TotalExpenses], Emphasis: true));
        rows.Add(new ReportRow([pl.NetProfit >= 0 ? "Net profit" : "Net loss", "", pl.NetProfit], Emphasis: true));
        return new ReportData([Text("account", "Account"), Text("group", "Group"), Money("amount", "Amount", total: false)], rows, c.RangeText);
    }
}

internal sealed class BalanceSheetGenerator(LedgerService ledgers) : Generator("accounts.balancesheet")
{
    public override async Task<ReportData> RunAsync(ReportContext c, CancellationToken cancellationToken)
    {
        var bs = await ledgers.BalanceSheetAsync(c.AsOf, cancellationToken);
        var rows = new List<ReportRow>();
        void Section(string title, IReadOnlyList<StatementLine> lines)
        {
            rows.Add(new ReportRow([title, "", null], Emphasis: true));
            rows.AddRange(lines.Select(l => new ReportRow([l.Name, l.GroupName, l.Amount], new DrillTarget(DrillKind.Ledger, l.AccountId), Indent: 1)));
        }

        Section("Assets", bs.Assets);
        rows.Add(new ReportRow(["Total assets", "", bs.TotalAssets], Emphasis: true));
        Section("Liabilities", bs.Liabilities);
        Section("Equity", bs.Equity);
        rows.Add(new ReportRow(["Profit for the year to date", "", bs.CurrentYearProfit], Indent: 1));
        rows.Add(new ReportRow(["Total liabilities and equity", "", bs.TotalLiabilitiesAndEquity], Emphasis: true));
        return new ReportData([Text("account", "Account"), Text("group", "Group"), Money("amount", "Amount", total: false)], rows, c.AsOfText,
            Notes: bs.IsBalanced ? null : ["The balance sheet does not balance. This should never happen; report it."]);
    }
}

/// <summary>
/// Cash flow by the direct method: every voucher that moves cash or bank is split by its other accounts. Fixed assets
/// are investing; loans taken and equity are financing; everything else is operating. Transfers between cash and bank
/// accounts cancel out.
/// </summary>
internal sealed class CashFlowGenerator() : Generator("accounts.cashflow")
{
    public override async Task<ReportData> RunAsync(ReportContext c, CancellationToken cancellationToken)
    {
        c.EnsureRange();
        var (start, end) = (c.From, c.To);
        var accounts = await Lookups.AccountsAsync(c.Unit, cancellationToken);
        var cashIds = accounts.Values.Where(a => a.IsCashOrBank).Select(a => a.Id).ToList();
        var opening = (await c.Unit.QueryAsync(q => q.Set<LedgerEntry>().Where(e => cashIds.Contains(e.AccountId) && e.Date < start).GroupBy(e => 1)
            .Select(g => g.Sum(e => e.Debit - e.Credit)), cancellationToken)).SingleOrDefault();
        var counter = await c.Unit.QueryAsync(q =>
            from e in q.Set<LedgerEntry>()
            where e.Date >= start && e.Date <= end && !cashIds.Contains(e.AccountId)
                && q.Set<LedgerEntry>().Any(x => x.VoucherId == e.VoucherId && cashIds.Contains(x.AccountId))
            group e by e.AccountId into g
            select new { AccountId = g.Key, Effect = g.Sum(e => e.Credit - e.Debit) }, cancellationToken);

        Guid Group(string code) => accounts.Values.Single(a => a.IsGroup && a.Code == code).Id;
        var (fixedAssets, loans, equity) = (Group("1200"), Group("2200"), Group("3000"));
        string Activity(Account a) => Lookups.Under(a, fixedAssets, accounts) ? "Investing" : Lookups.Under(a, loans, accounts) || Lookups.Under(a, equity, accounts) ? "Financing" : "Operating";

        var rows = new List<ReportRow> { new(["Cash and bank at the start", "", opening], Emphasis: true) };
        var net = 0m;
        foreach (var activity in new[] { "Operating", "Investing", "Financing" })
        {
            var lines = counter.Where(x => x.Effect != 0 && Activity(accounts[x.AccountId]) == activity).OrderBy(x => accounts[x.AccountId].Code, StringComparer.Ordinal).ToList();
            rows.Add(new ReportRow([$"{activity} activities", "", null], Emphasis: true));
            rows.AddRange(lines.Select(x => new ReportRow([accounts[x.AccountId].Name, x.Effect >= 0 ? "Inflow" : "Outflow", x.Effect],
                new DrillTarget(DrillKind.Ledger, x.AccountId), Indent: 1)));
            var subtotal = lines.Sum(x => x.Effect);
            net += subtotal;
            rows.Add(new ReportRow([$"Net cash from {activity.ToLowerInvariant()} activities", "", subtotal], Emphasis: true));
        }

        rows.Add(new ReportRow(["Net change in cash and bank", "", net], Emphasis: true));
        rows.Add(new ReportRow(["Cash and bank at the end", "", opening + net], Emphasis: true));
        return new ReportData([Text("line", "Line"), Text("direction", "Direction"), Money("amount", "Amount", total: false)], rows, c.RangeText);
    }
}

/// <summary>Income, expenses and result of each cost or profit centre in the range.</summary>
internal sealed class CostCentreGenerator() : Generator("accounts.costcentres")
{
    public override async Task<ReportData> RunAsync(ReportContext c, CancellationToken cancellationToken)
    {
        c.EnsureRange();
        var (start, end) = (c.From, c.To);
        var figures = await c.Unit.QueryAsync(q =>
            from e in q.Set<LedgerEntry>()
            join l in q.Set<VoucherLine>() on new { e.VoucherId, e.LineNumber } equals new { l.VoucherId, l.LineNumber }
            join a in q.Set<Account>() on e.AccountId equals a.Id
            where l.CostCentreId != null && e.Date >= start && e.Date <= end
            group e by new { l.CostCentreId, a.Nature } into g
            select new { g.Key.CostCentreId, g.Key.Nature, Net = g.Sum(e => e.Debit - e.Credit) }, cancellationToken);
        var centres = await c.Unit.ListAsync<CostCentre>(cancellationToken: cancellationToken);
        var rows = centres.OrderBy(x => x.Code, StringComparer.Ordinal).Select(centre =>
        {
            var income = -figures.Where(f => f.CostCentreId == centre.Id && f.Nature == AccountNature.Income).Sum(f => f.Net);
            var expense = figures.Where(f => f.CostCentreId == centre.Id && f.Nature == AccountNature.Expense).Sum(f => f.Net);
            return new ReportRow([centre.Code, centre.Name, centre.Kind == CostCentreKind.Profit ? "Profit centre" : "Cost centre", income, expense, income - expense]);
        }).Where(r => (decimal)r.Cells[3]! != 0 || (decimal)r.Cells[4]! != 0).ToList();
        return new ReportData([Text("code", "Code"), Text("centre", "Centre"), Text("kind", "Kind"), Money("income", "Income"), Money("expenses", "Expenses"),
            Money("result", "Result")], rows, c.RangeText, Notes: ["Only voucher lines tagged with a centre are counted."]);
    }
}
