using Microsoft.Extensions.Logging;
using Nexora.Application;
using Nexora.Application.Abstractions;
using Nexora.Application.Security;
using Nexora.Domain.Accounting;
using Nexora.Domain.Audit;
using Nexora.Domain.Common;
using Nexora.Domain.Companies;
using Nexora.Domain.Numbering;
using Nexora.Domain.Security;

namespace Nexora.Accounting;

public sealed record VoucherInput(Guid? Id, VoucherType Type, DateOnly Date, string Narration, IReadOnlyList<VoucherLineInput> Lines);

public sealed record VoucherLineRow(int LineNumber, Guid AccountId, string AccountCode, string AccountName, decimal Debit, decimal Credit, string? Narration);

public sealed record VoucherDetails(Guid Id, VoucherType Type, string? Number, DateOnly Date, string Narration, VoucherStatus Status,
    decimal Total, Guid? ReversesVoucherId, Guid? ReversedById, string? ReversedByNumber, IReadOnlyList<VoucherLineRow> Lines);

public sealed record VoucherSummary(Guid Id, VoucherType Type, string? Number, DateOnly Date, string Narration, VoucherStatus Status,
    decimal Total, bool IsReversed);

public sealed record VoucherQuery(DateOnly? From = null, DateOnly? To = null, VoucherType? Type = null, VoucherStatus? Status = null);

/// <summary>
/// The posting engine. Posting happens in one database transaction: checks, number allocation, freezing the
/// voucher, writing ledger entries and the audit event either all happen or none do.
/// </summary>
public sealed class VoucherService(
    IUnitOfWorkFactory units,
    IAuthorizer authorizer,
    IClock clock,
    ILogger<VoucherService> logger)
{
    private static SemaphoreSlim PostingGate => PostingLock.Gate;

    public async Task<IReadOnlyList<VoucherSummary>> ListAsync(VoucherQuery query, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.VouchersView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var from = query.From ?? DateOnly.MinValue;
        var to = query.To ?? DateOnly.MaxValue;
        var type = query.Type;
        var status = query.Status;
        var vouchers = await unit.ListAsync<Voucher>(v => v.Date >= from && v.Date <= to && (type == null || v.Type == type)
            && (status == null || v.Status == status), cancellationToken);
        var ids = vouchers.Select(v => v.Id).ToList();
        var lines = (await unit.ListAsync<VoucherLine>(l => ids.Contains(l.VoucherId), cancellationToken)).ToLookup(l => l.VoucherId);
        var reversed = (await unit.ListAsync<Voucher>(v => v.ReversesVoucherId != null, cancellationToken))
            .Select(v => v.ReversesVoucherId!.Value).ToHashSet();
        return vouchers
            .OrderByDescending(v => v.Date).ThenByDescending(v => v.PostedUtc ?? v.CreatedUtc)
            .Select(v => new VoucherSummary(v.Id, v.Type, v.Number, v.Date, v.Narration, v.Status, lines[v.Id].Sum(l => l.Debit), reversed.Contains(v.Id)))
            .ToList();
    }

    public async Task<VoucherDetails> GetAsync(Guid voucherId, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.VouchersView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var voucher = await unit.FindAsync<Voucher>(voucherId, cancellationToken) ?? throw NotFound();
        var lines = await unit.ListAsync<VoucherLine>(l => l.VoucherId == voucherId, cancellationToken);
        var accountIds = lines.Select(l => l.AccountId).ToList();
        var accounts = (await unit.ListAsync<Account>(a => accountIds.Contains(a.Id), cancellationToken)).ToDictionary(a => a.Id);
        var reversal = (await unit.ListAsync<Voucher>(v => v.ReversesVoucherId == voucherId, cancellationToken)).FirstOrDefault();
        return new VoucherDetails(voucher.Id, voucher.Type, voucher.Number, voucher.Date, voucher.Narration, voucher.Status,
            lines.Sum(l => l.Debit), voucher.ReversesVoucherId, reversal?.Id, reversal?.Number,
            lines.OrderBy(l => l.LineNumber)
                .Select(l => new VoucherLineRow(l.LineNumber, l.AccountId, accounts[l.AccountId].Code, accounts[l.AccountId].Name, l.Debit, l.Credit, l.Narration))
                .ToList());
    }

    /// <summary>Creates or replaces a draft. Drafts do not affect the ledger.</summary>
    public async Task<Guid> SaveDraftAsync(VoucherInput input, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.VouchersCreate);
        if (input.Type is VoucherType.Reversal) throw new WorkspaceValidationException("Reversals are created by reversing a posted voucher.");
        if (input.Type is VoucherType.Opening) throw new WorkspaceValidationException("Enter opening balances on the opening balances screen.");

        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        Voucher voucher;
        if (input.Id is { } id)
        {
            voucher = await LoadWithLinesAsync(unit, id, cancellationToken) ?? throw NotFound();
            if (voucher.Type != input.Type) throw new WorkspaceValidationException("The type of a saved voucher cannot be changed. Create a new voucher instead.");
            foreach (var line in voucher.Lines.ToList()) unit.Remove(line);
        }
        else
        {
            voucher = new Voucher(Guid.NewGuid(), session.CompanyId, input.Type, input.Date, input.Narration, clock.UtcNow, session.UserId);
            unit.Add(voucher);
        }

        voucher.UpdateDraft(input.Date, input.Narration, input.Lines);
        foreach (var line in voucher.Lines) unit.Add(line);
        await unit.CommitAsync(cancellationToken);
        return voucher.Id;
    }

    public async Task DeleteDraftAsync(Guid voucherId, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.VouchersCreate);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var voucher = await LoadWithLinesAsync(unit, voucherId, cancellationToken) ?? throw NotFound();
        if (voucher.IsPosted) throw new WorkspaceValidationException($"Voucher {voucher.Number} is posted and cannot be deleted. Reverse it instead.");
        foreach (var line in voucher.Lines) unit.Remove(line);
        unit.Remove(voucher);
        await unit.CommitAsync(cancellationToken);
    }

    /// <summary>Posts a draft. Posting the same voucher twice fails with a clear message instead of posting twice.</summary>
    public async Task<string> PostAsync(Guid voucherId, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.VouchersPost);
        await PostingGate.WaitAsync(cancellationToken);
        try
        {
            await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
            var voucher = await LoadWithLinesAsync(unit, voucherId, cancellationToken) ?? throw NotFound();
            if (voucher.IsPosted) throw new WorkspaceValidationException($"This voucher was already posted as {voucher.Number}.");
            var number = await PostCoreAsync(unit, voucher, session, cancellationToken);
            await unit.CommitAsync(cancellationToken);
            logger.LogInformation("Voucher posted; user {UserId}; voucher {VoucherId}", session.UserId, voucher.Id);
            return number;
        }
        finally
        {
            PostingGate.Release();
        }
    }

    /// <summary>Saves and posts in one step (needs both create and post permissions).</summary>
    public async Task<(Guid Id, string Number)> SaveAndPostAsync(VoucherInput input, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.VouchersPost);
        var id = await SaveDraftAsync(input, cancellationToken);
        return (id, await PostAsync(id, cancellationToken));
    }

    /// <summary>
    /// Cancels the effect of a posted voucher by posting a reversal with debits and credits swapped.
    /// The original voucher and its ledger entries stay exactly as they were.
    /// </summary>
    public async Task<string> ReverseAsync(Guid voucherId, DateOnly date, string narration, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.VouchersReverse);
        await PostingGate.WaitAsync(cancellationToken);
        try
        {
            await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
            var original = await LoadWithLinesAsync(unit, voucherId, cancellationToken) ?? throw NotFound();
            if (original.SourceType is { } source)
                throw new WorkspaceValidationException(source switch
                {
                    "billing" => $"Voucher {original.Number} was created by a sales or purchase document. Cancel or return that document instead, so documents and accounts stay in step.",
                    "settlement" => $"Voucher {original.Number} was created by a receipt or payment. Reverse it under Receipts or Payments instead.",
                    "expense" => $"Voucher {original.Number} was created by an expense. Reverse the expense under Expenses instead.",
                    "service" => $"Voucher {original.Number} was created by a service job. Return the part on the job instead, so stock and accounts stay in step.",
                    _ => $"Voucher {original.Number} was created by a stock document. Reverse that document under Stock instead, so stock and accounts stay in step.",
                });
            var (_, number) = await ReverseCoreAsync(unit, original, date, narration, session, system: false, cancellationToken);
            await unit.CommitAsync(cancellationToken);
            logger.LogInformation("Voucher reversed; user {UserId}; voucher {VoucherId}", session.UserId, original.Id);
            return number;
        }
        finally
        {
            PostingGate.Release();
        }
    }

    /// <summary>
    /// Posts opening balances for balance-sheet accounts as an Opening voucher dated the start of the given year.
    /// Any difference between debits and credits goes to Opening Balance Difference so the ledger always balances.
    /// </summary>
    public async Task<string> PostOpeningBalancesAsync(int financialYearStart, IReadOnlyList<VoucherLineInput> lines, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.AccountsManage);
        authorizer.Demand(Permissions.VouchersPost);
        await PostingGate.WaitAsync(cancellationToken);
        try
        {
            await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
            var ids = lines.Select(l => l.AccountId).ToList();
            var accounts = (await unit.ListAsync<Account>(a => ids.Contains(a.Id), cancellationToken)).ToDictionary(a => a.Id);
            if (lines.Any(l => !accounts.TryGetValue(l.AccountId, out var a) || !a.Nature.IsBalanceSheet()))
                throw new WorkspaceValidationException("Opening balances can only be entered for asset, liability and equity accounts.");

            var all = lines.ToList();
            var difference = all.Sum(l => l.Debit) - all.Sum(l => l.Credit);
            if (difference != 0)
            {
                var suspense = (await unit.ListAsync<Account>(a => a.SystemKey == SystemAccounts.OpeningBalanceDifference, cancellationToken)).Single();
                all.Add(difference > 0
                    ? new VoucherLineInput(suspense.Id, 0, difference, "Balancing difference")
                    : new VoucherLineInput(suspense.Id, -difference, 0, "Balancing difference"));
            }

            var year = new FinancialYear(financialYearStart);
            var voucher = new Voucher(Guid.NewGuid(), session.CompanyId, VoucherType.Opening, year.StartDate, $"Opening balances FY {year.Label}",
                clock.UtcNow, session.UserId);
            voucher.UpdateDraft(year.StartDate, voucher.Narration, all);
            unit.Add(voucher);
            foreach (var line in voucher.Lines) unit.Add(line);
            var number = await PostCoreAsync(unit, voucher, session, cancellationToken);
            await unit.CommitAsync(cancellationToken);
            return number;
        }
        finally
        {
            PostingGate.Release();
        }
    }

    /// <summary>Reverses a voucher inside the caller's unit of work. Used by manual reversal and by other modules through the ledger poster.</summary>
    internal async Task<(Guid Id, string Number)> ReverseCoreAsync(IUnitOfWork unit, Voucher original, DateOnly date, string narration, SessionInfo session,
        bool system, CancellationToken cancellationToken)
    {
        if (await unit.AnyAsync<Voucher>(v => v.ReversesVoucherId == original.Id, cancellationToken))
            throw new WorkspaceValidationException($"Voucher {original.Number} has already been reversed.");
        var reversal = original.CreateReversal(Guid.NewGuid(), date, narration, clock.UtcNow, session.UserId);
        unit.Add(reversal);
        foreach (var line in reversal.Lines) unit.Add(line);
        var number = await PostCoreAsync(unit, reversal, session, cancellationToken, system);
        unit.Record(new AuditEvent("Reversed", nameof(Voucher), original.Id.ToString(), $"Reversed voucher {original.Number} with {number}"));
        return (reversal.Id, number);
    }

    internal static Task<Voucher?> LoadForReversalAsync(IUnitOfWork unit, Guid voucherId, CancellationToken cancellationToken) =>
        LoadWithLinesAsync(unit, voucherId, cancellationToken);

    /// <summary>
    /// Shared posting steps. <paramref name="system"/> is true only for vouchers created by other modules (stock documents):
    /// manual vouchers may not touch Inventory accounts, so stock value and the ledger always agree.
    /// </summary>
    internal async Task<string> PostCoreAsync(IUnitOfWork unit, Voucher voucher, SessionInfo session, CancellationToken cancellationToken, bool system = false)
    {
        var company = await unit.FindAsync<Company>(session.CompanyId, cancellationToken) ?? throw NotFound();
        var year = FinancialYear.Containing(voucher.Date);
        if (voucher.Date < company.FirstFinancialYear.StartDate)
            throw new WorkspaceValidationException($"The company's books start on {company.FirstFinancialYear.StartDate:dd MMM yyyy}. Choose a later date.");
        var period = (await unit.ListAsync<FinancialYearPeriod>(p => p.StartYear == year.StartYear, cancellationToken)).SingleOrDefault()
            ?? throw new WorkspaceValidationException($"FY {year.Label} has not been opened. Open it under Settings → Financial years first.");
        if (period.IsClosed) throw new WorkspaceValidationException($"FY {year.Label} is closed. Post in an open year or ask an administrator to reopen it.");

        var accountIds = voucher.Lines.Select(l => l.AccountId).Distinct().ToList();
        var accounts = (await unit.ListAsync<Account>(a => accountIds.Contains(a.Id), cancellationToken)).ToDictionary(a => a.Id);
        foreach (var line in voucher.Lines)
        {
            if (!accounts.TryGetValue(line.AccountId, out var account))
                throw new WorkspaceValidationException($"Line {line.LineNumber}: the account no longer exists.");
            if (account.IsGroup) throw new WorkspaceValidationException($"Line {line.LineNumber}: {account.Name} is a group. Choose a ledger account inside it.");
            if (!account.IsActive) throw new WorkspaceValidationException($"Line {line.LineNumber}: {account.Name} is inactive.");
            if (!system && account.Kind == AccountKind.Inventory)
                throw new WorkspaceValidationException($"Line {line.LineNumber}: {account.Name} is kept by stock documents. Record stock changes under Stock so stock value and the ledger stay equal.");
        }

        VoucherRules.Check(voucher, accounts);
        voucher.EnsureBalanced();

        var type = voucher.Type;
        var number = await NumberAllocator.NextAsync(unit, session.CompanyId, type.DocumentType(), year,
            candidate => unit.AnyAsync<Voucher>(v => v.Type == type && v.Number == candidate, cancellationToken), cancellationToken);
        foreach (var entry in voucher.Post(number, year.StartYear, clock.UtcNow, session.UserId)) unit.Add(entry);
        unit.Record(new AuditEvent("Posted", nameof(Voucher), voucher.Id.ToString(),
            $"Posted {voucher.Type.Label().ToLowerInvariant()} voucher {number} for {voucher.TotalDebit:N2}"));
        return number;
    }

    private static async Task<Voucher?> LoadWithLinesAsync(IUnitOfWork unit, Guid voucherId, CancellationToken cancellationToken)
    {
        var voucher = await unit.FindAsync<Voucher>(voucherId, cancellationToken);
        if (voucher is null) return null;
        // Loading the lines through the same unit of work attaches them to the voucher's line collection.
        await unit.ListAsync<VoucherLine>(l => l.VoucherId == voucherId, cancellationToken);
        return voucher;
    }

    private static WorkspaceValidationException NotFound() => new("That voucher no longer exists. Refresh and try again.");
}

/// <summary>Rules about which accounts each voucher type may use.</summary>
public static class VoucherRules
{
    public static void Check(Voucher voucher, IReadOnlyDictionary<Guid, Account> accounts)
    {
        var lines = voucher.Lines.Select(l => (Line: l, Account: accounts[l.AccountId])).ToList();
        switch (voucher.Type)
        {
            case VoucherType.Payment when !lines.Any(x => x.Line.Credit > 0 && x.Account.IsCashOrBank):
                throw new DomainException("A payment voucher must credit a cash or bank account.");
            case VoucherType.Receipt when !lines.Any(x => x.Line.Debit > 0 && x.Account.IsCashOrBank):
                throw new DomainException("A receipt voucher must debit a cash or bank account.");
            case VoucherType.Contra when lines.Any(x => !x.Account.IsCashOrBank):
                throw new DomainException("A contra voucher moves money between cash and bank accounts only.");
            case VoucherType.Journal when lines.Any(x => x.Account.IsCashOrBank):
                throw new DomainException("Journal vouchers do not use cash or bank accounts. Use a payment, receipt or contra voucher.");
            case VoucherType.Opening when lines.Any(x => !x.Account.Nature.IsBalanceSheet()):
                throw new DomainException("Opening balances apply to asset, liability and equity accounts only.");
        }
    }
}
