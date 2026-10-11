using Nexora.Application;
using Nexora.Application.Abstractions;
using Nexora.Application.Security;
using Nexora.Domain.Accounting;
using Nexora.Domain.Companies;
using Nexora.Domain.Security;

namespace Nexora.Accounting;

public sealed record FinancialYearRow(Guid Id, int StartYear, string Label, bool IsClosed, DateTime? ClosedUtc, int DraftCount);

/// <summary>Ledgers, trial balance and financial statements, plus financial-year opening and closing.</summary>
public sealed class LedgerService(IUnitOfWorkFactory units, IAuthorizer authorizer, IClock clock)
{
    public async Task<LedgerStatement> StatementAsync(Guid accountId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.AccountsView);
        if (to < from) throw new WorkspaceValidationException("The end date must be on or after the start date.");
        if (!FinancialYear.Containing(from).Contains(to))
            throw new WorkspaceValidationException("Choose dates within one financial year (1 April to 31 March).");

        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var account = await unit.FindAsync<Account>(accountId, cancellationToken)
            ?? throw new WorkspaceValidationException("That account no longer exists.");
        if (account.IsGroup) throw new WorkspaceValidationException("Choose a ledger account, not a group.");

        var accounts = await unit.ListAsync<Account>(cancellationToken: cancellationToken);
        var isRetained = account.SystemKey == SystemAccounts.RetainedEarnings;
        var entries = await unit.ListAsync<LedgerEntry>(e => e.AccountId == accountId && e.Date >= from && e.Date <= to, cancellationToken);
        var voucherIds = entries.Select(e => e.VoucherId).Distinct().ToList();
        var vouchers = (await unit.ListAsync<Voucher>(v => voucherIds.Contains(v.Id), cancellationToken)).ToDictionary(v => v.Id);
        var year = FinancialYear.Containing(from);
        // Opening balance summed in the database: all earlier postings for balance-sheet accounts, this year's for income and expense.
        var openingFrom = account.Nature.IsBalanceSheet() ? DateOnly.MinValue : year.StartDate;
        var opening = (await unit.QueryAsync(q => q.Set<LedgerEntry>().Where(e => e.AccountId == accountId && e.Date < from && e.Date >= openingFrom)
            .GroupBy(e => 1).Select(g => g.Sum(e => e.Debit - e.Credit)), cancellationToken)).SingleOrDefault();
        var carried = opening
            + (isRetained ? LedgerCalculator.PriorYearsProfitNet(accounts, await DailyTotalsAsync(unit, year.StartDate.AddDays(-1), cancellationToken), year) : 0);
        return LedgerCalculator.Statement(account, entries, vouchers, from, to, carried);
    }

    public async Task<TrialBalance> TrialBalanceAsync(DateOnly asOf, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.AccountsView);
        var (accounts, entries) = await LoadAsync(asOf, cancellationToken);
        return LedgerCalculator.TrialBalance(accounts, entries, asOf, RetainedEarnings(accounts));
    }

    public async Task<ProfitAndLoss> ProfitAndLossAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.AccountsView);
        if (to < from) throw new WorkspaceValidationException("The end date must be on or after the start date.");
        var (accounts, entries) = await LoadAsync(to, cancellationToken);
        return LedgerCalculator.ProfitAndLoss(accounts, entries, from, to);
    }

    public async Task<BalanceSheet> BalanceSheetAsync(DateOnly asOf, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.AccountsView);
        var (accounts, entries) = await LoadAsync(asOf, cancellationToken);
        return LedgerCalculator.BalanceSheet(accounts, entries, asOf, RetainedEarnings(accounts));
    }

    public async Task<IReadOnlyList<FinancialYearRow>> ListYearsAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.AccountsView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var years = await unit.ListAsync<FinancialYearPeriod>(cancellationToken: cancellationToken);
        var drafts = await unit.ListAsync<Voucher>(v => v.Status == VoucherStatus.Draft, cancellationToken);
        return years.OrderByDescending(y => y.StartYear)
            .Select(y => new FinancialYearRow(y.Id, y.StartYear, y.Year.Label, y.IsClosed, y.ClosedUtc, drafts.Count(d => y.Year.Contains(d.Date))))
            .ToList();
    }

    /// <summary>Opens the year after the latest one. Balances carry forward automatically; no entries are posted.</summary>
    public async Task<int> OpenNextYearAsync(CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.FinancialYearsManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var latest = (await unit.ListAsync<FinancialYearPeriod>(cancellationToken: cancellationToken)).Max(y => (int?)y.StartYear)
            ?? throw new WorkspaceValidationException("No financial year exists for this company.");
        var next = new FinancialYearPeriod(Guid.NewGuid(), session.CompanyId, latest + 1);
        unit.Add(next);
        await unit.CommitAsync(cancellationToken);
        return next.StartYear;
    }

    /// <summary>Closes a year so nothing more can be posted or reversed into it. Drafts dated in it must be dealt with first.</summary>
    public async Task CloseYearAsync(int startYear, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.FinancialYearsManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var period = (await unit.ListAsync<FinancialYearPeriod>(p => p.StartYear == startYear, cancellationToken)).SingleOrDefault()
            ?? throw new WorkspaceValidationException("That financial year does not exist.");
        var start = period.Year.StartDate;
        var end = period.Year.EndDate;
        var drafts = (await unit.ListAsync<Voucher>(v => v.Status == VoucherStatus.Draft && v.Date >= start && v.Date <= end, cancellationToken)).Count;
        if (drafts > 0) throw new WorkspaceValidationException($"{drafts} draft voucher(s) are dated in FY {period.Year.Label}. Post or delete them before closing the year.");
        period.Close(clock.UtcNow);
        await unit.CommitAsync(cancellationToken);
    }

    public async Task ReopenYearAsync(int startYear, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.FinancialYearsManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var period = (await unit.ListAsync<FinancialYearPeriod>(p => p.StartYear == startYear, cancellationToken)).SingleOrDefault()
            ?? throw new WorkspaceValidationException("That financial year does not exist.");
        period.Reopen();
        await unit.CommitAsync(cancellationToken);
    }

    private async Task<(List<Account> Accounts, List<Posting> Entries)> LoadAsync(DateOnly asOf, CancellationToken cancellationToken)
    {
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var accounts = await unit.ListAsync<Account>(cancellationToken: cancellationToken);
        return (accounts, await DailyTotalsAsync(unit, asOf, cancellationToken));
    }

    /// <summary>Each account's debits and credits per day up to a date, summed in the database (the reports' only input).</summary>
    internal static Task<List<Posting>> DailyTotalsAsync(IUnitOfWork unit, DateOnly asOf, CancellationToken cancellationToken, DateOnly? from = null) =>
        unit.QueryAsync(q => q.Set<LedgerEntry>()
            .Where(e => e.Date <= asOf && (from == null || e.Date >= from))
            .GroupBy(e => new { e.AccountId, e.Date })
            .Select(g => new Posting(g.Key.AccountId, g.Key.Date, g.Sum(e => e.Debit), g.Sum(e => e.Credit))), cancellationToken);

    private static Guid RetainedEarnings(IEnumerable<Account> accounts) =>
        accounts.Single(a => a.SystemKey == SystemAccounts.RetainedEarnings).Id;
}
