namespace Nexora.Application.Abstractions;

/// <summary>Accounts that modules outside Accounting post to, resolved to each company's system accounts.</summary>
public enum LedgerAccountRole
{
    Inventory,
    InventoryAdjustment,
    OpeningBalanceDifference,
    CostOfGoodsSold,
}

public sealed record LedgerPostingLine(Guid AccountId, decimal Debit, decimal Credit, string? Narration = null);

/// <summary>An accounting entry requested by another module, for example a stock receipt.</summary>
public sealed record LedgerPosting(DateOnly Date, string Narration, string SourceType, Guid SourceId, IReadOnlyList<LedgerPostingLine> Lines);

/// <summary>
/// Posts accounting vouchers on behalf of other modules, inside the caller's unit of work, so the business document
/// and its accounting entry are stored in one transaction. Implemented by the accounting engine.
/// </summary>
public interface ILedgerPoster
{
    /// <summary>Serialises postings with manual vouchers. Hold it for the whole unit of work.</summary>
    Task<IDisposable> AcquireAsync(CancellationToken cancellationToken);

    Task<Guid> AccountAsync(IUnitOfWork unit, LedgerAccountRole role, CancellationToken cancellationToken);

    /// <summary>Posts the entry as a numbered journal voucher and returns its id.</summary>
    Task<Guid> PostAsync(IUnitOfWork unit, LedgerPosting posting, CancellationToken cancellationToken);

    /// <summary>Posts the reversal of a voucher created by <see cref="PostAsync"/> and returns the reversal's id.</summary>
    Task<Guid> ReverseAsync(IUnitOfWork unit, Guid voucherId, DateOnly date, string narration, CancellationToken cancellationToken);
}
