using Nexora.Application;
using Nexora.Application.Abstractions;
using Nexora.Application.Security;
using Nexora.Domain.Accounting;
using Nexora.Domain.Audit;
using Nexora.Domain.Common;
using Nexora.Domain.Security;

namespace Nexora.Accounting;

public sealed record BankEntryRow(Guid EntryId, Guid VoucherId, DateOnly Date, string Voucher, string Narration, decimal Deposit, decimal Withdrawal,
    Guid? ClearingId, DateOnly? ClearedOn, string StatementReference)
{
    public bool IsCleared => ClearingId is not null;
}

/// <summary>
/// Bank reconciliation of one bank account on a statement date. The bank balance is worked out from the books:
/// book balance − deposits not yet on the statement + withdrawals not yet on the statement.
/// </summary>
public sealed record BankReconciliation(Guid AccountId, string Account, DateOnly AsOf, decimal BookBalance, decimal UnclearedDeposits,
    decimal UnclearedWithdrawals, IReadOnlyList<BankEntryRow> Entries)
{
    public decimal BankBalance => BookBalance - UnclearedDeposits + UnclearedWithdrawals;
}

/// <summary>Marks bank ledger entries as cleared on the statement. accounts.view to read, accounts.manage to clear or un-clear.</summary>
public sealed class BankReconciliationService(IUnitOfWorkFactory units, IAuthorizer authorizer, IClock clock)
{
    /// <summary>Entries not cleared by <paramref name="asOf"/>, plus (when <paramref name="clearedFrom"/> is given) those cleared from that date on.</summary>
    public async Task<BankReconciliation> GetAsync(Guid accountId, DateOnly asOf, DateOnly? clearedFrom = null, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.AccountsView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var account = await BankAccountAsync(unit, accountId, cancellationToken);

        var totals = await unit.QueryAsync(q =>
            from e in q.Set<LedgerEntry>()
            where e.AccountId == accountId && e.Date <= asOf
            join c in q.Set<BankClearing>() on e.Id equals c.LedgerEntryId into cs
            from c in cs.DefaultIfEmpty()
            let uncleared = c == null || c.ClearedOn > asOf
            group new { e.Debit, e.Credit, uncleared } by 1 into g
            select new
            {
                Book = g.Sum(x => x.Debit - x.Credit),
                Deposits = g.Sum(x => x.uncleared ? x.Debit : 0),
                Withdrawals = g.Sum(x => x.uncleared ? x.Credit : 0),
            }, cancellationToken);
        var total = totals.SingleOrDefault();

        var rows = await unit.QueryAsync(q =>
            from e in q.Set<LedgerEntry>()
            where e.AccountId == accountId && e.Date <= asOf
            join v in q.Set<Voucher>() on e.VoucherId equals v.Id
            join c in q.Set<BankClearing>() on e.Id equals c.LedgerEntryId into cs
            from c in cs.DefaultIfEmpty()
            where c == null || c.ClearedOn > asOf || (clearedFrom != null && c.ClearedOn >= clearedFrom)
            orderby e.Date, v.Number
            select new BankEntryRow(e.Id, v.Id, e.Date, v.Number ?? string.Empty, v.Narration, e.Debit, e.Credit,
                c == null ? null : c.Id, c == null ? null : c.ClearedOn, c == null ? string.Empty : c.StatementReference), cancellationToken);

        return new BankReconciliation(account.Id, account.Name, asOf, total?.Book ?? 0, total?.Deposits ?? 0, total?.Withdrawals ?? 0, rows);
    }

    public async Task ClearAsync(IReadOnlyCollection<Guid> entryIds, DateOnly clearedOn, string statementReference, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.AccountsManage);
        if (entryIds.Count == 0) throw new WorkspaceValidationException("Choose the entries shown on the bank statement.");
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var ids = entryIds.Distinct().ToList();
        var entries = await unit.ListAsync<LedgerEntry>(e => ids.Contains(e.Id), cancellationToken);
        if (entries.Count != ids.Count) throw new WorkspaceValidationException("Some of those entries no longer exist.");
        foreach (var accountId in entries.Select(e => e.AccountId).Distinct()) await BankAccountAsync(unit, accountId, cancellationToken);
        if (await unit.AnyAsync<BankClearing>(c => ids.Contains(c.LedgerEntryId), cancellationToken))
            throw new WorkspaceValidationException("Some of those entries are already cleared. Refresh the list.");
        foreach (var entry in entries)
        {
            if (clearedOn < entry.Date) throw new WorkspaceValidationException($"An entry of {entry.Date:dd-MM-yyyy} cannot clear on an earlier statement date.");
            unit.Add(Guard(() => new BankClearing(Guid.NewGuid(), session.CompanyId, entry.Id, entry.AccountId, clearedOn, statementReference, clock.UtcNow, session.UserId)));
        }

        unit.Record(new AuditEvent("Cleared", nameof(BankClearing), string.Join(',', ids.Take(5)), $"{entries.Count} bank entries cleared on {clearedOn:dd-MM-yyyy}"));
        await unit.CommitAsync(cancellationToken);
    }

    public async Task UnclearAsync(IReadOnlyCollection<Guid> clearingIds, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.AccountsManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var ids = clearingIds.Distinct().ToList();
        var clearings = await unit.ListAsync<BankClearing>(c => ids.Contains(c.Id), cancellationToken);
        foreach (var clearing in clearings) unit.Remove(clearing);
        unit.Record(new AuditEvent("Uncleared", nameof(BankClearing), string.Join(',', ids.Take(5)), $"{clearings.Count} bank entries marked not cleared"));
        await unit.CommitAsync(cancellationToken);
    }

    private static async Task<Account> BankAccountAsync(IUnitOfWork unit, Guid accountId, CancellationToken cancellationToken)
    {
        var account = await unit.FindAsync<Account>(accountId, cancellationToken) ?? throw new WorkspaceValidationException("That account no longer exists.");
        if (account.Kind != AccountKind.Bank) throw new WorkspaceValidationException($"{account.Name} is not a bank account. Bank reconciliation is for bank ledgers.");
        return account;
    }

    private static T Guard<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (DomainException ex)
        {
            throw new WorkspaceValidationException(ex.Message);
        }
    }
}
