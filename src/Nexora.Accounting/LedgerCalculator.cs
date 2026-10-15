using Nexora.Domain.Accounting;
using Nexora.Domain.Companies;

namespace Nexora.Accounting;

public sealed record AccountBalance(Guid AccountId, string Code, string Name, AccountNature Nature, decimal Debit, decimal Credit)
{
    /// <summary>Debit minus credit. Positive is a debit balance.</summary>
    public decimal Net => Debit - Credit;
}

public sealed record TrialBalance(DateOnly AsOf, IReadOnlyList<AccountBalance> Rows, decimal TotalDebit, decimal TotalCredit)
{
    public bool IsBalanced => TotalDebit == TotalCredit;
}

public sealed record StatementLine(Guid AccountId, string Code, string Name, Guid? GroupId, string GroupName, decimal Amount);

public sealed record ProfitAndLoss(DateOnly From, DateOnly To, IReadOnlyList<StatementLine> Income, IReadOnlyList<StatementLine> Expenses,
    decimal TotalIncome, decimal TotalExpenses)
{
    public decimal NetProfit => TotalIncome - TotalExpenses;
}

public sealed record BalanceSheet(DateOnly AsOf, IReadOnlyList<StatementLine> Assets, IReadOnlyList<StatementLine> Liabilities,
    IReadOnlyList<StatementLine> Equity, decimal CurrentYearProfit, decimal TotalAssets, decimal TotalLiabilitiesAndEquity)
{
    public bool IsBalanced => TotalAssets == TotalLiabilitiesAndEquity;
}

public sealed record LedgerLine(DateOnly Date, Guid VoucherId, string VoucherNumber, VoucherType VoucherType, string Narration,
    decimal Debit, decimal Credit, decimal Balance);

public sealed record LedgerStatement(Guid AccountId, string Code, string Name, AccountNature Nature, DateOnly From, DateOnly To,
    decimal OpeningBalance, IReadOnlyList<LedgerLine> Lines, decimal ClosingBalance);

/// <summary>A ledger amount on an account and date: one entry, or the sum of an account's entries on one day.</summary>
public sealed record Posting(Guid AccountId, DateOnly Date, decimal Debit, decimal Credit);

/// <summary>
/// Pure calculations over posted ledger entries. Every report reads the same entries with the same rules,
/// which is what makes the trial balance, ledgers, profit and loss and balance sheet reconcile.
///
/// Carry-forward rule: balance-sheet accounts accumulate across financial years; income and expense
/// accounts restart at zero each year, and the net of earlier years is carried into Retained Earnings.
/// No closing entries are posted, so reports for any date are always derived from the same postings.
/// </summary>
public static class LedgerCalculator
{
    public static IReadOnlyCollection<Posting> ToPostings(IEnumerable<LedgerEntry> entries) =>
        entries.Select(e => new Posting(e.AccountId, e.Date, e.Debit, e.Credit)).ToList();

    public static TrialBalance TrialBalance(IReadOnlyCollection<Account> accounts, IReadOnlyCollection<LedgerEntry> entries, DateOnly asOf, Guid retainedEarningsId) =>
        TrialBalance(accounts, ToPostings(entries), asOf, retainedEarningsId);

    public static ProfitAndLoss ProfitAndLoss(IReadOnlyCollection<Account> accounts, IReadOnlyCollection<LedgerEntry> entries, DateOnly from, DateOnly to) =>
        ProfitAndLoss(accounts, ToPostings(entries), from, to);

    public static BalanceSheet BalanceSheet(IReadOnlyCollection<Account> accounts, IReadOnlyCollection<LedgerEntry> entries, DateOnly asOf, Guid retainedEarningsId) =>
        BalanceSheet(accounts, ToPostings(entries), asOf, retainedEarningsId);

    public static decimal PriorYearsProfitNet(IReadOnlyCollection<Account> accounts, IReadOnlyCollection<LedgerEntry> entries, FinancialYear year) =>
        PriorYearsProfitNet(accounts, ToPostings(entries), year);

    public static TrialBalance TrialBalance(IReadOnlyCollection<Account> accounts, IReadOnlyCollection<Posting> entries, DateOnly asOf,
        Guid retainedEarningsId)
    {
        var year = FinancialYear.Containing(asOf);
        var rows = new List<AccountBalance>();
        foreach (var account in accounts.Where(a => !a.IsGroup).OrderBy(a => a.Code, StringComparer.Ordinal))
        {
            var net = BalanceAt(account, entries, asOf, year);
            if (account.Id == retainedEarningsId) net += PriorYearsProfitNet(accounts, entries, year);
            if (net == 0) continue;
            rows.Add(net > 0
                ? new AccountBalance(account.Id, account.Code, account.Name, account.Nature, net, 0)
                : new AccountBalance(account.Id, account.Code, account.Name, account.Nature, 0, -net));
        }

        return new TrialBalance(asOf, rows, rows.Sum(r => r.Debit), rows.Sum(r => r.Credit));
    }

    public static ProfitAndLoss ProfitAndLoss(IReadOnlyCollection<Account> accounts, IReadOnlyCollection<Posting> entries, DateOnly from, DateOnly to)
    {
        var groups = accounts.ToDictionary(a => a.Id);
        var inRange = entries.Where(e => e.Date >= from && e.Date <= to).ToList();
        List<StatementLine> Lines(AccountNature nature, bool creditPositive) => accounts
            .Where(a => !a.IsGroup && a.Nature == nature)
            .OrderBy(a => a.Code, StringComparer.Ordinal)
            .Select(a =>
            {
                var net = inRange.Where(e => e.AccountId == a.Id).Sum(e => e.Debit - e.Credit);
                var parent = a.ParentId is { } p && groups.TryGetValue(p, out var g) ? g : null;
                return new StatementLine(a.Id, a.Code, a.Name, parent?.Id, parent?.Name ?? string.Empty, creditPositive ? -net : net);
            })
            .Where(l => l.Amount != 0)
            .ToList();

        var income = Lines(AccountNature.Income, creditPositive: true);
        var expenses = Lines(AccountNature.Expense, creditPositive: false);
        return new ProfitAndLoss(from, to, income, expenses, income.Sum(l => l.Amount), expenses.Sum(l => l.Amount));
    }

    public static BalanceSheet BalanceSheet(IReadOnlyCollection<Account> accounts, IReadOnlyCollection<Posting> entries, DateOnly asOf,
        Guid retainedEarningsId)
    {
        var year = FinancialYear.Containing(asOf);
        var groups = accounts.ToDictionary(a => a.Id);
        List<StatementLine> Lines(AccountNature nature, bool creditPositive) => accounts
            .Where(a => !a.IsGroup && a.Nature == nature)
            .OrderBy(a => a.Code, StringComparer.Ordinal)
            .Select(a =>
            {
                var net = BalanceAt(a, entries, asOf, year);
                if (a.Id == retainedEarningsId) net += PriorYearsProfitNet(accounts, entries, year);
                var parent = a.ParentId is { } p && groups.TryGetValue(p, out var g) ? g : null;
                return new StatementLine(a.Id, a.Code, a.Name, parent?.Id, parent?.Name ?? string.Empty, creditPositive ? -net : net);
            })
            .Where(l => l.Amount != 0)
            .ToList();

        var assets = Lines(AccountNature.Asset, creditPositive: false);
        var liabilities = Lines(AccountNature.Liability, creditPositive: true);
        var equity = Lines(AccountNature.Equity, creditPositive: true);
        var profit = ProfitAndLoss(accounts, entries, year.StartDate, asOf).NetProfit;
        var totalAssets = assets.Sum(l => l.Amount);
        var totalOther = liabilities.Sum(l => l.Amount) + equity.Sum(l => l.Amount) + profit;
        return new BalanceSheet(asOf, assets, liabilities, equity, profit, totalAssets, totalOther);
    }

    /// <summary>
    /// Ledger statement for a range inside one financial year. <paramref name="carriedForward"/> is added to the
    /// opening balance; pass the prior years' profit for Retained Earnings so the statement matches the trial balance.
    /// </summary>
    public static LedgerStatement Statement(Account account, IReadOnlyCollection<LedgerEntry> entries,
        IReadOnlyDictionary<Guid, Voucher> vouchers, DateOnly from, DateOnly to, decimal carriedForward = 0)
    {
        if (account.IsGroup) throw new ArgumentException("Statements are for ledger accounts, not groups.", nameof(account));
        var year = FinancialYear.Containing(from);
        if (!year.Contains(to) || to < from) throw new ArgumentException("A statement must start and end within one financial year.");
        var opening = BalanceAt(account, ToPostings(entries), from.AddDays(-1), year, sameYearOnly: !account.Nature.IsBalanceSheet()) + carriedForward;
        var running = opening;
        var lines = new List<LedgerLine>();
        foreach (var entry in entries.Where(e => e.AccountId == account.Id && e.Date >= from && e.Date <= to)
                     // Fully deterministic order (vouchers posted in the same instant would otherwise come back in any order).
                     .OrderBy(e => e.Date).ThenBy(e => vouchers.TryGetValue(e.VoucherId, out var v) ? v.PostedUtc : null)
                     .ThenBy(e => vouchers.TryGetValue(e.VoucherId, out var n) ? n.Number : null, StringComparer.Ordinal)
                     .ThenBy(e => e.VoucherId).ThenBy(e => e.LineNumber))
        {
            running += entry.Debit - entry.Credit;
            var voucher = vouchers[entry.VoucherId];
            lines.Add(new LedgerLine(entry.Date, voucher.Id, voucher.Number ?? string.Empty, voucher.Type, voucher.Narration, entry.Debit, entry.Credit, running));
        }

        return new LedgerStatement(account.Id, account.Code, account.Name, account.Nature, from, to, opening, lines, running);
    }

    /// <summary>Net (debit − credit) of an account at a date, applying the carry-forward rule.</summary>
    private static decimal BalanceAt(Account account, IReadOnlyCollection<Posting> entries, DateOnly asOf, FinancialYear year,
        bool sameYearOnly = false)
    {
        var restartsEachYear = sameYearOnly || !account.Nature.IsBalanceSheet();
        return entries
            .Where(e => e.AccountId == account.Id && e.Date <= asOf && (!restartsEachYear || e.Date >= year.StartDate))
            .Sum(e => e.Debit - e.Credit);
    }

    /// <summary>Net (debit − credit) of all income and expense postings before the year: the profit carried forward.</summary>
    public static decimal PriorYearsProfitNet(IReadOnlyCollection<Account> accounts, IReadOnlyCollection<Posting> entries, FinancialYear year)
    {
        var profitAndLossAccounts = accounts.Where(a => !a.Nature.IsBalanceSheet()).Select(a => a.Id).ToHashSet();
        return entries.Where(e => e.Date < year.StartDate && profitAndLossAccounts.Contains(e.AccountId)).Sum(e => e.Debit - e.Credit);
    }
}
