using Nexora.Application;
using Nexora.Application.Abstractions;
using Nexora.Application.Security;
using Nexora.Domain.Accounting;

namespace Nexora.Accounting;

/// <summary>Serialises every posting (manual vouchers and other modules' entries) inside this process.</summary>
internal static class PostingLock
{
    public static readonly SemaphoreSlim Gate = new(1, 1);
}

/// <summary>
/// Posts other modules' accounting entries through the same engine as manual vouchers: same balance checks, financial
/// year checks, numbering and audit, inside the caller's transaction. The voucher records its source document.
/// </summary>
internal sealed class LedgerPoster(VoucherService vouchers, IAuthorizer authorizer, IClock clock) : ILedgerPoster
{
    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        await PostingLock.Gate.WaitAsync(cancellationToken);
        return new Release();
    }

    public async Task<Guid> AccountAsync(IUnitOfWork unit, LedgerAccountRole role, CancellationToken cancellationToken)
    {
        var key = role switch
        {
            LedgerAccountRole.Inventory => SystemAccounts.Inventory,
            LedgerAccountRole.InventoryAdjustment => SystemAccounts.InventoryAdjustment,
            LedgerAccountRole.OpeningBalanceDifference => SystemAccounts.OpeningBalanceDifference,
            LedgerAccountRole.CostOfGoodsSold => SystemAccounts.CostOfGoodsSold,
            LedgerAccountRole.Cash => SystemAccounts.Cash,
            LedgerAccountRole.SundryDebtors => SystemAccounts.SundryDebtors,
            LedgerAccountRole.SundryCreditors => SystemAccounts.SundryCreditors,
            LedgerAccountRole.Sales => SystemAccounts.Sales,
            LedgerAccountRole.SalesReturns => SystemAccounts.SalesReturns,
            LedgerAccountRole.OutputCgst => SystemAccounts.OutputCgst,
            LedgerAccountRole.OutputSgst => SystemAccounts.OutputSgst,
            LedgerAccountRole.OutputIgst => SystemAccounts.OutputIgst,
            LedgerAccountRole.InputCgst => SystemAccounts.InputCgst,
            LedgerAccountRole.InputSgst => SystemAccounts.InputSgst,
            LedgerAccountRole.InputIgst => SystemAccounts.InputIgst,
            LedgerAccountRole.RoundOff => SystemAccounts.RoundOff,
            LedgerAccountRole.WarrantyExpense => SystemAccounts.WarrantyExpense,
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };
        var account = (await unit.ListAsync<Account>(a => a.SystemKey == key, cancellationToken)).SingleOrDefault();
        if (account is null && role == LedgerAccountRole.WarrantyExpense) account = await AddWarrantyAccountAsync(unit, cancellationToken);
        return account?.Id ?? throw new WorkspaceValidationException($"The system account \"{key}\" is missing from the chart of accounts.");
    }

    /// <summary>
    /// Companies created before Phase 7 have no Warranty and Service Costs account. It is added on first use, in the
    /// caller's transaction, under the group that holds Round Off (Indirect Expenses), with the next free code.
    /// </summary>
    private async Task<Account> AddWarrantyAccountAsync(IUnitOfWork unit, CancellationToken cancellationToken)
    {
        var session = authorizer.RequireSession();
        var sibling = (await unit.ListAsync<Account>(a => a.SystemKey == SystemAccounts.RoundOff, cancellationToken)).SingleOrDefault()
            ?? throw new WorkspaceValidationException("The Round Off account is missing, so the warranty account cannot be added.");
        var parent = await unit.FindAsync<Account>(sibling.ParentId!.Value, cancellationToken) ?? throw new WorkspaceValidationException("The expense group is missing.");
        var codes = (await unit.ListAsync<Account>(cancellationToken: cancellationToken)).Select(a => a.Code).ToHashSet(StringComparer.Ordinal);
        var names = (await unit.ListAsync<Account>(cancellationToken: cancellationToken)).Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var code = Enumerable.Range(5309, 90).Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture)).First(c => !codes.Contains(c));
        var name = names.Contains("Warranty and Service Costs") ? "Warranty and Service Costs (system)" : "Warranty and Service Costs";
        var account = new Account(Guid.NewGuid(), session.CompanyId, code, name, parent.Nature, AccountKind.General, parent.Id, SystemAccounts.WarrantyExpense);
        unit.Add(account);
        return account;
    }

    public async Task<Guid> PostAsync(IUnitOfWork unit, LedgerPosting posting, CancellationToken cancellationToken)
    {
        var session = authorizer.RequireSession();
        var ids = posting.Lines.Select(l => l.AccountId).Distinct().ToList();
        var accounts = (await unit.ListAsync<Account>(a => ids.Contains(a.Id), cancellationToken)).ToDictionary(a => a.Id);
        bool CashOrBank(LedgerPostingLine l) => accounts.TryGetValue(l.AccountId, out var a) && a.IsCashOrBank;
        // Choose the type the entry really is, so the usual voucher-type rules still apply.
        var type = posting.Type ?? (posting.Lines.Any(l => l.Credit > 0 && CashOrBank(l)) ? VoucherType.Payment
            : posting.Lines.Any(l => l.Debit > 0 && CashOrBank(l)) ? VoucherType.Receipt
            : VoucherType.Journal);

        var voucher = new Voucher(Guid.NewGuid(), session.CompanyId, type, posting.Date, posting.Narration, clock.UtcNow, session.UserId);
        voucher.MarkSource(posting.SourceType, posting.SourceId);
        voucher.UpdateDraft(posting.Date, posting.Narration,
            posting.Lines.Select(l => new VoucherLineInput(l.AccountId, l.Debit, l.Credit, l.Narration)).ToList());
        unit.Add(voucher);
        foreach (var line in voucher.Lines) unit.Add(line);
        await vouchers.PostCoreAsync(unit, voucher, session, cancellationToken, system: true);
        return voucher.Id;
    }

    public async Task<Guid> ReverseAsync(IUnitOfWork unit, Guid voucherId, DateOnly date, string narration, CancellationToken cancellationToken)
    {
        var session = authorizer.RequireSession();
        var original = await VoucherService.LoadForReversalAsync(unit, voucherId, cancellationToken)
            ?? throw new WorkspaceValidationException("The accounting voucher of this document no longer exists.");
        var (id, _) = await vouchers.ReverseCoreAsync(unit, original, date, narration, session, system: true, cancellationToken);
        return id;
    }

    private sealed class Release : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) PostingLock.Gate.Release();
        }
    }
}
