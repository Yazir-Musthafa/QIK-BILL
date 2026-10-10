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
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };
        var account = (await unit.ListAsync<Account>(a => a.SystemKey == key, cancellationToken)).SingleOrDefault()
            ?? throw new WorkspaceValidationException($"The system account \"{key}\" is missing from the chart of accounts.");
        return account.Id;
    }

    public async Task<Guid> PostAsync(IUnitOfWork unit, LedgerPosting posting, CancellationToken cancellationToken)
    {
        var session = authorizer.RequireSession();
        var ids = posting.Lines.Select(l => l.AccountId).Distinct().ToList();
        var accounts = (await unit.ListAsync<Account>(a => ids.Contains(a.Id), cancellationToken)).ToDictionary(a => a.Id);
        bool CashOrBank(LedgerPostingLine l) => accounts.TryGetValue(l.AccountId, out var a) && a.IsCashOrBank;
        // Choose the type the entry really is, so the usual voucher-type rules still apply.
        var type = posting.Lines.Any(l => l.Credit > 0 && CashOrBank(l)) ? VoucherType.Payment
            : posting.Lines.Any(l => l.Debit > 0 && CashOrBank(l)) ? VoucherType.Receipt
            : VoucherType.Journal;

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
