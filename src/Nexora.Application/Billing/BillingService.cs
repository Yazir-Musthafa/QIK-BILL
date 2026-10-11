using Nexora.Application.Abstractions;
using Nexora.Application.Inventory;
using Nexora.Application.Security;
using Nexora.Domain.Accounting;
using Nexora.Domain.Audit;
using Nexora.Domain.Billing;
using Nexora.Domain.Common;
using Nexora.Domain.Companies;
using Nexora.Domain.Inventory;
using Nexora.Domain.Security;

namespace Nexora.Application.Billing;

/// <summary>
/// One line as entered. Quantity and <see cref="Rate"/> are in <see cref="UnitId"/> (the product's base unit when null).
/// Rate null means the product's selling price (sales) or purchase price (purchases). Returns name the
/// <see cref="OriginalLineId"/> and a quantity; their price, discount and tax come from the original line.
/// A <see cref="BillingLineType.Service"/> line (sales only) has no product (<see cref="ProductId"/> empty): it needs a
/// description, a rate and optionally a tax category and SAC code. An <see cref="BillingLineType.IssuedPart"/> line
/// bills a part a service job already issued (<see cref="SourceId"/>): priced like goods, but posting does not move stock.
/// </summary>
public sealed record BillingLineInput(Guid ProductId, decimal Quantity, Guid? UnitId = null, decimal? Rate = null, decimal DiscountPercent = 0,
    decimal DiscountAmount = 0, Guid? BatchId = null, string? BatchNumber = null, DateOnly? ExpiresOn = null, string? SerialNumber = null,
    Guid? OriginalLineId = null, string? Description = null, BillingLineType LineType = BillingLineType.Goods, Guid? TaxCategoryId = null,
    string? HsnCode = null, Guid? SourceId = null);

/// <summary>
/// Posting a credit sale would take the customer's receivable above the credit limit. Posting again with the override
/// needs sales.credit-override.
/// </summary>
public sealed class CreditLimitExceededException(string message, decimal limit, decimal exposure) : WorkspaceValidationException(message)
{
    public decimal Limit { get; } = limit;

    /// <summary>The receivable after this invoice.</summary>
    public decimal Exposure { get; } = exposure;
}

/// <summary>
/// A draft document. For returns, party, mode, place of supply and prices come from <see cref="OriginalDocumentId"/>.
/// Null <see cref="PricesIncludeTax"/> and <see cref="RoundToRupee"/> use the company's billing settings.
/// </summary>
public sealed record BillingDraftInput(Guid? Id, BillingDocumentKind Kind, DateOnly Date, Guid? PartyId, Guid WarehouseId, SettlementMode Mode,
    IReadOnlyList<BillingLineInput> Lines, Guid? CashAccountId = null, Guid? OriginalDocumentId = null, string Reference = "", string Narration = "",
    bool? PricesIncludeTax = null, bool? RoundToRupee = null, string? WalkInName = null);

public enum PaymentStatus
{
    Draft,
    Unpaid,
    PartlyPaid,
    Paid,
    Cancelled,
}

public sealed record BillingListFilter(IReadOnlyList<BillingDocumentKind> Kinds, string? Search = null, DateOnly? From = null, DateOnly? To = null,
    Guid? PartyId = null);

public sealed record BillingDocumentRow(Guid Id, BillingDocumentKind Kind, string Number, DateOnly Date, string PartyName, SettlementMode Mode,
    decimal Taxable, decimal Tax, decimal Total, decimal Outstanding, PaymentStatus Status, string? OriginalNumber, string Reference)
{
    public string KindLabel => Kind.Label();

    public string StatusLabel => Status switch
    {
        PaymentStatus.PartlyPaid => Kind.IsCharge() ? "Partly paid" : "Partly applied",
        PaymentStatus.Paid => Kind.IsCharge() ? "Paid" : "Applied",
        PaymentStatus.Unpaid => Kind.IsCharge() ? "Unpaid" : "Unapplied",
        _ => Status.ToString(),
    };
}

public sealed record BillingLineView(Guid Id, int LineNumber, Guid? ProductId, string Sku, string Description, string HsnCode, Guid? UnitId, string Unit,
    decimal Quantity, decimal Rate, decimal DiscountPercent, decimal Gross, decimal Discount, decimal Taxable, decimal TaxRatePercent, decimal Cgst,
    decimal Sgst, decimal Igst, decimal Total, Guid? BatchId, string Batch, DateOnly? ExpiresOn, string? SerialNumber, Guid? OriginalLineId, decimal? Cost,
    BillingLineType LineType = BillingLineType.Goods, Guid? TaxCategoryId = null, Guid? SourceId = null);

public sealed record AllocationView(Guid OtherId, string OtherType, string OtherNumber, DateOnly Date, decimal Amount);

public sealed record BillingDocumentView(Guid Id, BillingDocumentKind Kind, BillingStatus State, string? Number, DateOnly Date, Guid? PartyId, string PartyName,
    string PartyGstin, string PlaceOfSupply, string PlaceOfSupplyName, bool IsInterState, SettlementMode Mode, Guid? CashAccountId, Guid WarehouseId,
    string Warehouse, Guid? OriginalDocumentId, string? OriginalNumber, string Reference, string Narration, bool PricesIncludeTax, bool RoundToRupee,
    decimal Gross, decimal Discount, decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst, decimal RoundOff, decimal Total, decimal? CostOfGoods,
    decimal Outstanding, PaymentStatus Status, string? CancellationReason, IReadOnlyList<BillingLineView> Lines, IReadOnlyList<AllocationView> Allocations,
    IReadOnlyList<string> Returns, string CompanyName, string CompanyGstin, string CompanyStateCode)
{
    public decimal Tax => Cgst + Sgst + Igst;

    public bool IsCancelled => Status == PaymentStatus.Cancelled;
}

/// <summary>What can still be returned of one invoice or bill line.</summary>
public sealed record ReturnableLine(Guid OriginalLineId, Guid? ProductId, string Sku, string Description, Guid? UnitId, string Unit, decimal Quantity,
    decimal Returned, decimal Rate, Guid? BatchId, string Batch, string? SerialNumber)
{
    public decimal Remaining => Quantity - Returned;
}

public sealed record BillingSettingsView(bool RoundToRupee, bool PricesIncludeTax);

/// <summary>
/// Sales invoices, credit notes (sales returns), purchase bills and debit notes (purchase returns).
/// Posting happens in one transaction: recalculation, number, stock movements (through <see cref="StockService"/>,
/// so costing and stock rules are the inventory engine's) and one voucher through <see cref="ILedgerPoster"/>.
/// Postings are listed in docs/ACCOUNTING_MAP.md.
/// </summary>
public sealed class BillingService(IUnitOfWorkFactory units, IAuthorizer authorizer, ILedgerPoster ledger, StockService stock, IClock clock)
{
    internal const string SourceType = "billing";
    private const string WalkIn = "Cash customer";

    // ----- Drafts -----

    public async Task<Guid> SaveDraftAsync(BillingDraftInput input, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(CreatePermission(input.Kind));
        if (input.Lines.Count == 0) throw new WorkspaceValidationException("Add at least one line.");
        if (input.Lines.Count > 500) throw new WorkspaceValidationException("A document can have at most 500 lines.");
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        BillingDocument document;
        if (input.Id is { } id)
        {
            document = await unit.FindAsync<BillingDocument>(id, cancellationToken) ?? throw new WorkspaceValidationException("That draft no longer exists.");
            if (document.Kind != input.Kind) throw new WorkspaceValidationException("The document type cannot be changed.");
            Guard(document.EnsureDraft);
            foreach (var line in await unit.ListAsync<BillingLine>(l => l.DocumentId == id, cancellationToken)) unit.Remove(line);
        }
        else
        {
            document = new BillingDocument(Guid.NewGuid(), session.CompanyId, input.Kind, input.Date, clock.UtcNow, session.UserId);
            unit.Add(document);
        }

        await ComposeAsync(unit, session, document, input, cancellationToken);
        await unit.CommitAsync(cancellationToken);
        return document.Id;
    }

    public async Task DeleteDraftAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var document = await unit.FindAsync<BillingDocument>(id, cancellationToken) ?? throw new WorkspaceValidationException("That draft no longer exists.");
        authorizer.Demand(CreatePermission(document.Kind));
        Guard(document.EnsureDraft);
        foreach (var line in await unit.ListAsync<BillingLine>(l => l.DocumentId == id, cancellationToken)) unit.Remove(line);
        unit.Remove(document);
        await unit.CommitAsync(cancellationToken);
    }

    /// <summary>A draft return of everything still returnable on <paramref name="originalId"/>, for the user to adjust.</summary>
    public async Task<BillingDraftInput> FullReturnDraftAsync(Guid originalId, DateOnly date, CancellationToken cancellationToken = default)
    {
        var lines = await ReturnableLinesAsync(originalId, cancellationToken);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var original = await unit.FindAsync<BillingDocument>(originalId, cancellationToken) ?? throw new WorkspaceValidationException("That document no longer exists.");
        var kind = original.Kind == BillingDocumentKind.SalesInvoice ? BillingDocumentKind.SalesReturn : BillingDocumentKind.PurchaseReturn;
        var open = lines.Where(l => l.Remaining > 0).Select(l => new BillingLineInput(l.ProductId ?? Guid.Empty, l.Remaining, l.UnitId, OriginalLineId: l.OriginalLineId,
            BatchId: l.BatchId, SerialNumber: l.SerialNumber)).ToList();
        if (open.Count == 0) throw new WorkspaceValidationException($"Everything on {original.Number} has already been returned.");
        return new BillingDraftInput(null, kind, date, original.PartyId, original.WarehouseId, original.Mode, open, original.CashAccountId, original.Id);
    }

    // ----- Posting -----

    /// <summary>
    /// Posts a draft. Posting the same draft twice is refused, so a repeated click cannot post twice. Everything is
    /// recalculated from the stored quantities and rates with the tax rates configured now.
    /// </summary>
    public async Task<string> PostAsync(Guid id, bool overrideCreditLimit = false, CancellationToken cancellationToken = default)
    {
        using var lockHandle = await ledger.AcquireAsync(cancellationToken);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var document = await unit.FindAsync<BillingDocument>(id, cancellationToken) ?? throw new WorkspaceValidationException("That draft no longer exists.");
        var session = authorizer.Demand(CreatePermission(document.Kind));
        if (document.Status == BillingStatus.Posted) throw new WorkspaceValidationException($"{document.Kind.Label()} {document.Number} is already posted.");

        // Rebuild the lines from what was entered, re-checking permissions, prices, tax and return limits now.
        var stored = (await unit.ListAsync<BillingLine>(l => l.DocumentId == id, cancellationToken)).OrderBy(l => l.LineNumber).ToList();
        var input = new BillingDraftInput(id, document.Kind, document.Date, document.PartyId, document.WarehouseId, document.Mode,
            stored.Select(l => new BillingLineInput(l.ProductId ?? Guid.Empty, l.Quantity, l.UnitId, l.Rate, l.DiscountPercent,
                l.DiscountPercent == 0 ? l.DiscountAmount : 0, l.BatchId, l.BatchNumber, l.ExpiresOn, l.SerialNumber, l.OriginalLineId, l.Description,
                l.LineType, l.TaxCategoryId, l.HsnCode, l.SourceId)).ToList(),
            document.CashAccountId, document.OriginalDocumentId, document.Reference, document.Narration, document.PricesIncludeTax, document.RoundToRupee,
            document.PartyId is null ? document.PartyName : null);
        foreach (var line in stored) unit.Remove(line);
        var lines = await ComposeAsync(unit, session, document, input, cancellationToken);
        if (lines.Count == 0) throw new WorkspaceValidationException("Add at least one line.");
        await EnsureNotDuplicateBillAsync(unit, document, cancellationToken);
        await EnsureCreditLimitAsync(unit, session, document, overrideCreditLimit, cancellationToken);

        var year = FinancialYear.Containing(document.Date);
        var kind = document.Kind;
        var number = await NumberAllocator.NextAsync(unit, session.CompanyId, kind.NumberingKey(), year,
            async candidate => await unit.AnyAsync<BillingDocument>(d => d.Kind == kind && d.Number == candidate, cancellationToken)
                || await unit.AnyAsync<StockDocument>(d => d.Number == candidate, cancellationToken), cancellationToken);

        var originalLines = document.OriginalDocumentId is { } originalId
            ? (await unit.ListAsync<BillingLine>(l => l.DocumentId == originalId, cancellationToken)).ToDictionary(l => l.Id)
            : [];
        var priorReturns = await PriorReturnsAsync(unit, document, cancellationToken);
        // Only goods move stock: services have no stock, and parts issued by a service job left stock (with their cost) already.
        var stockBacked = lines.Where(l => l.MovesStock).ToList();
        StockCoreResult? posted = null;
        if (stockBacked.Count > 0)
        {
            var stockLines = stockBacked.Select(l => StockLine(document.Kind, l, originalLines, priorReturns)).ToList();
            posted = await stock.PostCoreAsync(unit, session,
                new StockDocumentInput(kind.StockType(), document.Date, document.WarehouseId, $"{kind.Label()} {number}", stockLines), number, cancellationToken);
        }

        var voucherLines = await VoucherLinesAsync(unit, document, lines, posted?.Lines ?? [], cancellationToken);
        Guid? voucherId = null;
        if (voucherLines.Count > 0)
        {
            var narration = $"{kind.Label()} {number} · {(document.PartyName.Length > 0 ? document.PartyName : WalkIn)}";
            voucherId = await ledger.PostAsync(unit, new LedgerPosting(document.Date, narration, SourceType, document.Id, voucherLines, kind.VoucherType()),
                cancellationToken);
        }

        // Line cost: what goods came in at (bills, returns) or the stock value that left (sales, debit notes).
        decimal cost = 0;
        foreach (var line in lines.Where(l => !l.MovesStock)) line.Lock(0, null);
        for (var i = 0; i < stockBacked.Count; i++)
        {
            var value = posted!.Lines[i];
            var lineCost = Math.Abs(kind is BillingDocumentKind.PurchaseBill or BillingDocumentKind.SalesReturn ? value.EnteredValue : value.StockValue);
            stockBacked[i].Lock(lineCost, value.BatchId);
            cost += lineCost;
        }

        Guard(() => document.MarkPosted(number, voucherId, posted?.Document.Id, cost, clock.UtcNow, session.UserId));
        await AutoAllocateReturnAsync(unit, session, document, cancellationToken);
        unit.Record(new AuditEvent("Posted", nameof(BillingDocument), document.Id.ToString(),
            $"Posted {kind.Label().ToLowerInvariant()} {number} for {document.Total:N2}" + (document.PartyName.Length > 0 ? $" ({document.PartyName})" : string.Empty)));
        await unit.CommitAsync(cancellationToken);
        return number;
    }

    /// <summary>
    /// Cancels a posted document: reverses its voucher and its stock in one transaction and records why. Refused while
    /// receipts or payments are allocated to it or returns exist against it, and when later stock movements of its
    /// products make an exact stock reversal impossible (make a return instead).
    /// </summary>
    public async Task CancelAsync(Guid id, DateOnly date, string reason, CancellationToken cancellationToken = default)
    {
        using var lockHandle = await ledger.AcquireAsync(cancellationToken);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var document = await unit.FindAsync<BillingDocument>(id, cancellationToken) ?? throw new WorkspaceValidationException("That document no longer exists.");
        var session = authorizer.Demand(PartyService.CancelPermission(document.Kind.PartyKind()));
        if (document.Status != BillingStatus.Posted) throw new WorkspaceValidationException("Only posted documents can be cancelled. Delete a draft instead.");
        if (await unit.AnyAsync<BillingCancellation>(c => c.DocumentId == id, cancellationToken))
            throw new WorkspaceValidationException($"{document.Kind.Label()} {document.Number} is already cancelled.");
        if (string.IsNullOrWhiteSpace(reason)) throw new WorkspaceValidationException("Enter the reason for cancelling.");
        if (date < document.Date) throw new WorkspaceValidationException("A cancellation cannot be dated before the document.");

        if (!document.Kind.IsReturn())
        {
            var returns = await unit.ListAsync<BillingDocument>(d => d.OriginalDocumentId == id && d.Status == BillingStatus.Posted, cancellationToken);
            var cancelledIds = (await unit.ListAsync<BillingCancellation>(cancellationToken: cancellationToken)).Select(c => c.DocumentId).ToHashSet();
            if (returns.Any(r => !cancelledIds.Contains(r.Id)))
                throw new WorkspaceValidationException($"Returns have been posted against {document.Number}. Cancel them first, or return the rest instead.");
        }

        if (document.PartyId is { } partyId && document.Mode == SettlementMode.Credit)
        {
            var allocations = await OpenItems.LiveAllocationsAsync(unit, partyId, id, cancellationToken);
            // Allocations between documents (a credit note applied to an invoice) simply stop counting; money that has
            // actually been received, paid or refunded must be reversed first.
            if (allocations.Any(a => a.ChargeSource == OpenItemSource.Settlement || a.CreditSource == OpenItemSource.Settlement))
                throw new WorkspaceValidationException($"Receipts, payments or refunds are allocated to {document.Number}. Reverse them first.");
        }

        StockDocument? stockReversal = null;
        if (document.StockDocumentId is { } stockId)
        {
            var original = await unit.FindAsync<StockDocument>(stockId, cancellationToken) ?? throw new WorkspaceValidationException("The document's stock entry is missing.");
            stockReversal = await stock.ReverseCoreAsync(unit, session, original, date, $"Cancellation of {document.Number}", cancellationToken);
        }

        Guid? reversalVoucher = null;
        if (document.VoucherId is { } voucherId)
            reversalVoucher = await ledger.ReverseAsync(unit, voucherId, date, $"Cancellation of {document.Kind.Label().ToLowerInvariant()} {document.Number}", cancellationToken);

        Guard(() => unit.Add(new BillingCancellation(Guid.NewGuid(), session.CompanyId, id, date, reason, reversalVoucher, stockReversal?.Id, clock.UtcNow, session.UserId)));
        unit.Record(new AuditEvent("Cancelled", nameof(BillingDocument), id.ToString(), $"Cancelled {document.Kind.Label().ToLowerInvariant()} {document.Number}: {reason.Trim()}"));
        await unit.CommitAsync(cancellationToken);
    }

    // ----- Queries -----

    public async Task<IReadOnlyList<BillingDocumentRow>> ListAsync(BillingListFilter filter, CancellationToken cancellationToken = default)
    {
        foreach (var kind in filter.Kinds.Distinct()) authorizer.Demand(PartyService.ViewPermission(kind.PartyKind()));
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var kinds = filter.Kinds.ToList();
        var from = filter.From ?? DateOnly.MinValue;
        var to = filter.To ?? DateOnly.MaxValue;
        var documents = await unit.ListAsync<BillingDocument>(d => kinds.Contains(d.Kind) && d.Date >= from && d.Date <= to
            && (filter.PartyId == null || d.PartyId == filter.PartyId), cancellationToken);
        var cancelled = (await unit.ListAsync<BillingCancellation>(cancellationToken: cancellationToken)).Select(c => c.DocumentId).ToHashSet();
        var open = (await OpenItems.LoadAsync(unit, filter.PartyId, cancellationToken)).ToDictionary(i => i.Id, i => i.Open);
        var numbers = (await unit.ListAsync<BillingDocument>(d => d.Status == BillingStatus.Posted, cancellationToken)).ToDictionary(d => d.Id, d => d.Number);
        var search = (filter.Search ?? string.Empty).Trim();
        return documents
            .Where(d => search.Length == 0 || (d.Number ?? string.Empty).Contains(search, StringComparison.OrdinalIgnoreCase)
                || d.PartyName.Contains(search, StringComparison.OrdinalIgnoreCase) || d.Reference.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => d.Date).ThenByDescending(d => d.Number ?? "~", StringComparer.Ordinal)
            .Select(d =>
            {
                var outstanding = open.GetValueOrDefault(d.Id);
                return new BillingDocumentRow(d.Id, d.Kind, d.Number ?? "(draft)", d.Date, d.PartyName.Length > 0 ? d.PartyName : WalkIn, d.Mode,
                    d.TaxableAmount, d.Tax, d.Total, outstanding, StatusOf(d, cancelled.Contains(d.Id), outstanding),
                    d.OriginalDocumentId is { } o ? numbers.GetValueOrDefault(o) : null, d.Reference);
            })
            .ToList();
    }

    public async Task<BillingDocumentView> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var d = await unit.FindAsync<BillingDocument>(id, cancellationToken) ?? throw new WorkspaceValidationException("That document no longer exists.");
        authorizer.Demand(PartyService.ViewPermission(d.Kind.PartyKind()));
        var showCost = authorizer.Has(Permissions.InventoryViewCost);
        var company = await unit.FindAsync<Company>(d.CompanyId, cancellationToken) ?? throw new WorkspaceValidationException("Sign in again.");
        var lines = (await unit.ListAsync<BillingLine>(l => l.DocumentId == id, cancellationToken)).OrderBy(l => l.LineNumber).ToList();
        var productIds = lines.Where(l => l.ProductId != null).Select(l => l.ProductId!.Value).Distinct().ToList();
        var products = (await unit.ListAsync<Product>(p => productIds.Contains(p.Id), cancellationToken)).ToDictionary(p => p.Id);
        var unitNames = (await unit.ListAsync<Unit>(cancellationToken: cancellationToken)).ToDictionary(u => u.Id, u => u.Code);
        var batchIds = lines.Where(l => l.BatchId != null).Select(l => l.BatchId!.Value).ToList();
        var batches = (await unit.ListAsync<Batch>(b => batchIds.Contains(b.Id), cancellationToken)).ToDictionary(b => b.Id);
        var cancellation = (await unit.ListAsync<BillingCancellation>(c => c.DocumentId == id, cancellationToken)).SingleOrDefault();
        var warehouse = await unit.FindAsync<Warehouse>(d.WarehouseId, cancellationToken);
        var original = d.OriginalDocumentId is { } oid ? await unit.FindAsync<BillingDocument>(oid, cancellationToken) : null;
        var cancelledIds = (await unit.ListAsync<BillingCancellation>(cancellationToken: cancellationToken)).Select(c => c.DocumentId).ToHashSet();
        var returns = (await unit.ListAsync<BillingDocument>(r => r.OriginalDocumentId == id && r.Status == BillingStatus.Posted, cancellationToken))
            .Where(r => !cancelledIds.Contains(r.Id)).Select(r => r.Number!).ToList();

        decimal outstanding = 0;
        var allocations = new List<AllocationView>();
        if (d.PartyId is { } partyId && d.Status == BillingStatus.Posted && d.Mode == SettlementMode.Credit)
        {
            var items = await OpenItems.LoadAsync(unit, partyId, cancellationToken);
            var byId = items.ToDictionary(i => i.Id);
            outstanding = byId.TryGetValue(id, out var self) ? self.Open : 0;
            foreach (var a in await OpenItems.LiveAllocationsAsync(unit, partyId, id, cancellationToken))
            {
                var other = byId[a.ChargeId == id ? a.CreditId : a.ChargeId];
                allocations.Add(new AllocationView(other.Id, other.Type, other.Number, a.Date, a.Amount));
            }
        }

        return new BillingDocumentView(d.Id, d.Kind, d.Status, d.Number, d.Date, d.PartyId, d.PartyName.Length > 0 ? d.PartyName : WalkIn, d.PartyGstin,
            d.PlaceOfSupply, IndianState.FromCode(d.PlaceOfSupply)?.Name ?? string.Empty, d.IsInterState, d.Mode, d.CashAccountId, d.WarehouseId,
            warehouse?.Name ?? "?", d.OriginalDocumentId, original?.Number, d.Reference, d.Narration, d.PricesIncludeTax, d.RoundToRupee,
            d.GrossAmount, d.DiscountAmount, d.TaxableAmount, d.Cgst, d.Sgst, d.Igst, d.RoundOff, d.Total, showCost ? d.CostOfGoods : null,
            outstanding, StatusOf(d, cancellation is not null, outstanding), cancellation?.Reason,
            lines.Select(l => new BillingLineView(l.Id, l.LineNumber, l.ProductId, l.ProductId is { } pid && products.TryGetValue(pid, out var p) ? p.Sku : string.Empty, l.Description,
                l.HsnCode, l.UnitId, l.UnitId is { } uid ? unitNames.GetValueOrDefault(uid, "?") : string.Empty, l.Quantity, l.Rate, l.DiscountPercent, l.GrossAmount, l.DiscountAmount,
                l.TaxableAmount, l.TaxRatePercent, l.Cgst, l.Sgst, l.Igst, l.Total, l.BatchId,
                l.BatchId is { } b && batches.TryGetValue(b, out var batch) ? batch.Number : l.BatchNumber, l.ExpiresOn, l.SerialNumber, l.OriginalLineId,
                showCost && l.IsLocked ? l.CostValue : null, l.LineType, l.TaxCategoryId, l.SourceId)).ToList(),
            allocations, returns, company.LegalName.Length > 0 ? company.LegalName : company.Name, company.Gstin, company.StateCode);
    }

    public async Task<IReadOnlyList<ReturnableLine>> ReturnableLinesAsync(Guid originalId, CancellationToken cancellationToken = default)
    {
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var original = await unit.FindAsync<BillingDocument>(originalId, cancellationToken) ?? throw new WorkspaceValidationException("That document no longer exists.");
        authorizer.Demand(PartyService.ViewPermission(original.Kind.PartyKind()));
        if (original.Kind.IsReturn()) throw new WorkspaceValidationException("A return cannot itself be returned.");
        var lines = (await unit.ListAsync<BillingLine>(l => l.DocumentId == originalId, cancellationToken)).OrderBy(l => l.LineNumber).ToList();
        var returned = await ReturnedAsync(unit, lines.Select(l => l.Id).ToList(), null, cancellationToken);
        var productIds = lines.Where(l => l.ProductId != null).Select(l => l.ProductId!.Value).ToList();
        var products = (await unit.ListAsync<Product>(p => productIds.Contains(p.Id), cancellationToken)).ToDictionary(p => p.Id);
        var unitNames = (await unit.ListAsync<Unit>(cancellationToken: cancellationToken)).ToDictionary(u => u.Id, u => u.Code);
        var batches = (await unit.ListAsync<Batch>(b => productIds.Contains(b.ProductId), cancellationToken)).ToDictionary(b => b.Id, b => b.Number);
        return lines.Select(l => new ReturnableLine(l.Id, l.ProductId, l.ProductId is { } pid ? products[pid].Sku : string.Empty, l.Description, l.UnitId,
                l.UnitId is { } uid ? unitNames.GetValueOrDefault(uid, "?") : string.Empty,
                l.Quantity, returned.GetValueOrDefault(l.Id).Quantity, l.Rate, l.BatchId, l.BatchId is { } b ? batches.GetValueOrDefault(b, l.BatchNumber) : l.BatchNumber,
                l.SerialNumber))
            .ToList();
    }

    public async Task<BillingSettingsView> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        authorizer.RequireSession();
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var settings = await SettingsAsync(unit, cancellationToken);
        return new BillingSettingsView(settings.RoundToRupee, settings.PricesIncludeTax);
    }

    public async Task SaveSettingsAsync(bool roundToRupee, bool pricesIncludeTax, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.CompanyManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        (await SettingsAsync(unit, cancellationToken)).Update(roundToRupee, pricesIncludeTax);
        await unit.CommitAsync(cancellationToken);
    }

    // ----- Composition: the single place where documents are calculated -----

    private async Task<List<BillingLine>> ComposeAsync(IUnitOfWork unit, SessionInfo session, BillingDocument document, BillingDraftInput input,
        CancellationToken cancellationToken)
    {
        var company = await unit.FindAsync<Company>(session.CompanyId, cancellationToken) ?? throw new WorkspaceValidationException("Sign in again.");
        var settings = await SettingsAsync(unit, cancellationToken);
        var warehouse = await unit.FindAsync<Warehouse>(input.WarehouseId, cancellationToken)
            ?? throw new WorkspaceValidationException("Choose a warehouse. Add warehouses under Settings → Branches and warehouses.");
        if (!warehouse.IsActive) throw new WorkspaceValidationException($"Warehouse {warehouse.Name} is inactive.");

        BillingDocument? original = null;
        Dictionary<Guid, BillingLine> originalLines = [];
        var partyId = input.PartyId;
        var mode = input.Mode;
        var cashAccountId = input.CashAccountId;
        bool pricesIncludeTax = input.PricesIncludeTax ?? settings.PricesIncludeTax, roundToRupee = input.RoundToRupee ?? settings.RoundToRupee;
        if (!input.Kind.IsSales()) pricesIncludeTax = input.PricesIncludeTax ?? false;
        if (input.Kind.IsReturn())
        {
            if (input.OriginalDocumentId is not { } originalId) throw new WorkspaceValidationException("Choose the document being returned.");
            original = await unit.FindAsync<BillingDocument>(originalId, cancellationToken) ?? throw new WorkspaceValidationException("The original document no longer exists.");
            if (original.Kind != input.Kind.OriginalKind() || original.Status != BillingStatus.Posted)
                throw new WorkspaceValidationException($"A {input.Kind.Label().ToLowerInvariant()} must be made against a posted {input.Kind.OriginalKind().Label().ToLowerInvariant()}.");
            if (await unit.AnyAsync<BillingCancellation>(c => c.DocumentId == originalId, cancellationToken))
                throw new WorkspaceValidationException($"{original.Number} is cancelled and cannot be returned.");
            if (input.Date < original.Date) throw new WorkspaceValidationException($"A return cannot be dated before {original.Number}.");
            originalLines = (await unit.ListAsync<BillingLine>(l => l.DocumentId == originalId, cancellationToken)).ToDictionary(l => l.Id);
            partyId = original.PartyId;
            mode = original.Mode;
            cashAccountId = mode == SettlementMode.Cash ? input.CashAccountId ?? original.CashAccountId : null;
            pricesIncludeTax = original.PricesIncludeTax;
        }

        Party? party = null;
        if (partyId is { } pid)
        {
            party = await unit.FindAsync<Party>(pid, cancellationToken) ?? throw new WorkspaceValidationException("That party no longer exists.");
            if (party.Kind != input.Kind.PartyKind()) throw new WorkspaceValidationException($"Choose a {PartyService.Noun(input.Kind.PartyKind())}.");
            if (!party.IsActive && !input.Kind.IsReturn()) throw new WorkspaceValidationException($"{party.Name} is inactive.");
        }
        else if (!input.Kind.IsSales()) throw new WorkspaceValidationException("Choose the supplier.");
        else if (mode == SettlementMode.Credit) throw new WorkspaceValidationException("Choose the customer for a credit sale, or make it a cash sale.");

        if (mode == SettlementMode.Cash)
        {
            cashAccountId ??= await ledger.AccountAsync(unit, LedgerAccountRole.Cash, cancellationToken);
            var account = await unit.FindAsync<Account>(cashAccountId.Value, cancellationToken) ?? throw new WorkspaceValidationException("That account no longer exists.");
            if (!account.IsCashOrBank || account.IsGroup || !account.IsActive) throw new WorkspaceValidationException("Choose an active cash or bank account.");
        }

        // Place of supply: the party's state (the company's own state when unknown). Returns keep the original's.
        var placeOfSupply = original?.PlaceOfSupply ?? (party is { StateCode.Length: > 0 } ? party.StateCode : company.StateCode);
        var interState = original?.IsInterState ?? (placeOfSupply != company.StateCode);
        var name = party?.Name ?? (string.IsNullOrWhiteSpace(input.WalkInName) ? string.Empty : input.WalkInName.Trim());
        Guard(() => document.UpdateDraft(input.Date, partyId, name, party?.Gstin ?? string.Empty, placeOfSupply, interState, mode, cashAccountId,
            warehouse.Id, original?.Id, input.Reference, input.Narration, pricesIncludeTax, roundToRupee));

        var productIds = input.Lines.Where(l => l.LineType != BillingLineType.Service).Select(l => l.ProductId)
            .Concat(originalLines.Values.Where(l => l.ProductId != null).Select(l => l.ProductId!.Value)).Distinct().ToList();
        var customerPrices = input.Kind == BillingDocumentKind.SalesInvoice && partyId is { } priceParty
            ? (await unit.ListAsync<CustomerPrice>(c => c.PartyId == priceParty, cancellationToken)).ToDictionary(c => c.ProductId, c => c.Price)
            : [];
        var products = (await unit.ListAsync<Product>(p => productIds.Contains(p.Id), cancellationToken)).ToDictionary(p => p.Id);
        var conversions = await unit.ListAsync<UnitConversion>(c => productIds.Contains(c.ProductId), cancellationToken);
        var unitRows = (await unit.ListAsync<Unit>(cancellationToken: cancellationToken)).ToDictionary(u => u.Id);
        var taxCategories = (await unit.ListAsync<TaxCategory>(cancellationToken: cancellationToken)).ToDictionary(t => t.Id);
        var returned = original is null ? [] : await ReturnedAsync(unit, originalLines.Keys.ToList(), document.Id, cancellationToken);
        var thisReturn = new HashSet<Guid>();

        var lines = new List<BillingLine>();
        for (var i = 0; i < input.Lines.Count; i++)
        {
            var entry = input.Lines[i];
            var lineNumber = i + 1;
            BillingLine line;
            if (original is not null)
            {
                if (entry.OriginalLineId is not { } originalLineId || !originalLines.TryGetValue(originalLineId, out var source))
                    throw new WorkspaceValidationException($"Line {lineNumber}: choose a line of {original.Number} to return.");
                if (!thisReturn.Add(originalLineId))
                    throw new WorkspaceValidationException($"Line {lineNumber}: {source.Description} is already on this return. Change the quantity on that line.");
                var already = returned.TryGetValue(originalLineId, out var r) ? r : (Quantity: 0m, Amounts: LineAmounts.Zero, Cost: 0m);
                var quantity = Quantity(entry.Quantity, lineNumber);
                var remaining = source.Quantity - already.Quantity;
                if (quantity > remaining)
                    throw new WorkspaceValidationException($"Line {lineNumber}: only {remaining:0.###} of {source.Description} can still be returned.");
                var amounts = BillingCalculator.Portion(source.Amounts, source.Quantity, already.Amounts, already.Quantity, quantity);
                var baseQuantity = source.BaseQuantity == source.Quantity ? quantity : BaseOf(source, quantity, unitRows, products, lineNumber);
                line = new BillingLine(Guid.NewGuid(), session.CompanyId, document.Id, lineNumber, source.ProductId, source.Description, source.HsnCode,
                    source.UnitId, quantity, baseQuantity, source.Rate, source.DiscountPercent, source.TaxCategoryId, source.TaxRatePercent, amounts,
                    source.BatchId, source.BatchNumber, source.ExpiresOn, source.SerialNumber, source.Id, source.LineType, source.SourceId);
            }
            else if (entry.LineType == BillingLineType.Service)
            {
                if (input.Kind != BillingDocumentKind.SalesInvoice) throw new WorkspaceValidationException($"Line {lineNumber}: service lines are only for sales invoices.");
                if (string.IsNullOrWhiteSpace(entry.Description)) throw new WorkspaceValidationException($"Line {lineNumber}: describe the service.");
                if (entry.Rate is not { } rate) throw new WorkspaceValidationException($"Line {lineNumber}: enter the rate of the service.");
                var quantity = Quantity(entry.Quantity, lineNumber);
                TaxCategory? tax = null;
                if (entry.TaxCategoryId is { } t && !taxCategories.TryGetValue(t, out tax)) throw new WorkspaceValidationException($"Line {lineNumber}: that tax category no longer exists.");
                var amounts = Guard(() => BillingCalculator.Line(quantity, rate, entry.DiscountPercent, entry.DiscountAmount, tax?.ReferenceRatePercent ?? 0,
                    interState, pricesIncludeTax), lineNumber);
                EnforceSalesLimits(session, entry.Description.Trim(), rate, rate, amounts, lineNumber);
                var sac = (entry.HsnCode ?? string.Empty).Trim();
                if (sac.Length > 8) throw new WorkspaceValidationException($"Line {lineNumber}: the SAC code can have at most 8 characters.");
                line = Guard(() => new BillingLine(Guid.NewGuid(), session.CompanyId, document.Id, lineNumber, null, entry.Description, sac, null, quantity, quantity,
                    rate, entry.DiscountPercent, tax?.Id, tax?.ReferenceRatePercent ?? 0, amounts, null, string.Empty, null, null, null, BillingLineType.Service), lineNumber);
            }
            else
            {
                if (entry.LineType == BillingLineType.IssuedPart && (input.Kind != BillingDocumentKind.SalesInvoice || entry.SourceId is null))
                    throw new WorkspaceValidationException($"Line {lineNumber}: issued parts are billed from their service job.");
                if (!products.TryGetValue(entry.ProductId, out var product)) throw new WorkspaceValidationException($"Line {lineNumber}: choose a product.");
                if (product.IsArchived) throw new WorkspaceValidationException($"Line {lineNumber}: {product.Sku} is archived.");
                var unitId = entry.UnitId ?? product.UnitId;
                var factor = Factor(product, unitId, conversions, lineNumber);
                var quantity = Quantity(entry.Quantity, lineNumber);
                var baseQuantity = quantity * factor;
                var baseUnit = unitRows[product.UnitId];
                if (decimal.Round(baseQuantity, baseUnit.DecimalPlaces) != baseQuantity)
                    throw new WorkspaceValidationException($"Line {lineNumber}: {quantity:0.###} is not a valid quantity of {product.Sku} in {baseUnit.Code}.");
                // A customer-specific price (per base unit) replaces the selling price for that customer.
                var basePrice = input.Kind.IsSales() ? customerPrices.GetValueOrDefault(product.Id, product.SellingPrice) : product.PurchasePrice;
                var listPrice = Money.Round(basePrice * factor);
                var rate = entry.Rate ?? listPrice;
                var tax = product.TaxCategoryId is { } t && taxCategories.TryGetValue(t, out var category) ? category : null;
                var amounts = Guard(() => BillingCalculator.Line(quantity, rate, entry.DiscountPercent, entry.DiscountAmount, tax?.ReferenceRatePercent ?? 0,
                    interState, pricesIncludeTax), lineNumber);
                if (input.Kind.IsSales()) EnforceSalesLimits(session, product.Sku, rate, listPrice, amounts, lineNumber);
                var description = string.IsNullOrWhiteSpace(entry.Description) ? product.DisplayName : entry.Description.Trim();
                line = Guard(() => new BillingLine(Guid.NewGuid(), session.CompanyId, document.Id, lineNumber, product.Id, description, product.HsnCode, unitId,
                    quantity, baseQuantity, rate, entry.DiscountPercent, tax?.Id, tax?.ReferenceRatePercent ?? 0, amounts, entry.BatchId, entry.BatchNumber ?? string.Empty,
                    entry.ExpiresOn, entry.SerialNumber, null, entry.LineType, entry.SourceId), lineNumber);
            }

            unit.Add(line);
            lines.Add(line);
        }

        var totals = BillingCalculator.Totals(lines.Select(l => l.Amounts), roundToRupee);
        Guard(() => document.SetTotals(totals));
        return lines;
    }

    /// <summary>Price override and discount limits, checked on every save and again when posting (as the poster).</summary>
    private static void EnforceSalesLimits(SessionInfo session, string item, decimal rate, decimal listPrice, LineAmounts amounts, int lineNumber)
    {
        if (rate != listPrice && !session.Has(Permissions.SalesPriceOverride))
            throw new WorkspaceValidationException($"Line {lineNumber}: you may not change the price of {item} ({listPrice:N2}).");
        if (amounts.Discount <= 0) return;
        if (!session.Has(Permissions.SalesDiscount)) throw new WorkspaceValidationException($"Line {lineNumber}: you may not give discounts.");
        if (amounts.Gross > 0 && amounts.Discount * 100m > session.MaxDiscountPercent * amounts.Gross)
            throw new WorkspaceValidationException($"Line {lineNumber}: the discount is more than your limit of {session.MaxDiscountPercent:0.##}%.");
    }

    private static StockLineInput StockLine(BillingDocumentKind kind, BillingLine line, Dictionary<Guid, BillingLine> originalLines,
        Dictionary<Guid, (decimal Quantity, decimal Cost)> priorReturns)
    {
        switch (kind)
        {
            case BillingDocumentKind.PurchaseBill:
                return new StockLineInput(line.ProductId!.Value, line.Quantity, line.UnitId, BatchId: line.BatchId, BatchNumber: NullIfEmpty(line.BatchNumber),
                    ExpiresOn: line.ExpiresOn, SerialNumber: line.SerialNumber, LineValue: line.TaxableAmount);
            case BillingDocumentKind.SalesReturn:
            {
                // Goods come back at the cost they left with, in proportion, so returning everything restores the exact cost.
                var source = originalLines[line.OriginalLineId!.Value];
                var prior = priorReturns.GetValueOrDefault(source.Id);
                var cost = BillingCalculator.PortionOf(source.CostValue, source.Quantity, prior.Cost, prior.Quantity, line.Quantity);
                priorReturns[source.Id] = (prior.Quantity + line.Quantity, prior.Cost + cost);
                return new StockLineInput(line.ProductId!.Value, line.Quantity, line.UnitId, BatchId: line.BatchId, SerialNumber: line.SerialNumber, LineValue: cost);
            }

            default:
                return new StockLineInput(line.ProductId!.Value, line.Quantity, line.UnitId, BatchId: line.BatchId, BatchNumber: line.BatchId is null ? NullIfEmpty(line.BatchNumber) : null,
                    SerialNumber: line.SerialNumber);
        }
    }

    /// <summary>
    /// The document's voucher. Debits are positive amounts and credits negative; amounts on the same account are netted.
    /// See docs/ACCOUNTING_MAP.md, "Sales and purchases".
    /// </summary>
    private async Task<List<LedgerPostingLine>> VoucherLinesAsync(IUnitOfWork unit, BillingDocument d, List<BillingLine> lines,
        IReadOnlyList<StockLineValue> stockValues, CancellationToken cancellationToken)
    {
        var amounts = new Dictionary<Guid, decimal>();
        var notes = new Dictionary<Guid, string>();
        async Task Add(LedgerAccountRole role, decimal amount, string note)
        {
            if (amount == 0) return;
            var account = await ledger.AccountAsync(unit, role, cancellationToken);
            AddTo(account, amount, note);
        }

        void AddTo(Guid account, decimal amount, string note)
        {
            if (amount == 0) return;
            amounts[account] = amounts.GetValueOrDefault(account) + amount;
            notes.TryAdd(account, note);
        }

        var partyAccount = d.Mode == SettlementMode.Cash ? d.CashAccountId!.Value
            : (await unit.FindAsync<Party>(d.PartyId!.Value, cancellationToken))!.AccountId;
        var stockValue = stockValues.Sum(v => v.StockValue);        // signed: + goods in, − goods out
        var entered = stockValues.Sum(v => v.EnteredValue);
        var sales = d.Kind.IsSales();
        // Charges (invoice, bill) and returns mirror each other: s = +1 for the party's normal direction.
        var s = d.Kind.IsCharge() ? 1 : -1;
        if (sales)
        {
            AddTo(partyAccount, s * d.Total, d.Mode == SettlementMode.Cash ? "Received" : "Receivable");
            await Add(d.Kind == BillingDocumentKind.SalesInvoice ? LedgerAccountRole.Sales : LedgerAccountRole.SalesReturns, -s * d.TaxableAmount, "Taxable value");
            await Add(LedgerAccountRole.OutputCgst, -s * d.Cgst, "CGST");
            await Add(LedgerAccountRole.OutputSgst, -s * d.Sgst, "SGST");
            await Add(LedgerAccountRole.OutputIgst, -s * d.Igst, "IGST");
            await Add(LedgerAccountRole.RoundOff, -s * d.RoundOff, "Round off");
            // Cost of goods: a sale moves its stock value out of Inventory into COGS; a return brings back the cost it
            // left with (COGS credited); any difference to the stock value a negative-stock settlement gave is an adjustment.
            await Add(LedgerAccountRole.Inventory, stockValue, "Stock");
            await Add(LedgerAccountRole.CostOfGoodsSold, -entered, "Cost of goods sold");
            await Add(LedgerAccountRole.InventoryAdjustment, entered - stockValue, "Cost of units issued while stock was negative");
        }
        else
        {
            AddTo(partyAccount, -s * d.Total, d.Mode == SettlementMode.Cash ? "Paid" : "Payable");
            await Add(LedgerAccountRole.InputCgst, s * d.Cgst, "CGST");
            await Add(LedgerAccountRole.InputSgst, s * d.Sgst, "SGST");
            await Add(LedgerAccountRole.InputIgst, s * d.Igst, "IGST");
            await Add(LedgerAccountRole.RoundOff, s * d.RoundOff, "Round off");
            await Add(LedgerAccountRole.Inventory, stockValue, "Stock");
            // Bill: the taxable value is the cost; a settlement of negative stock moves part of it to adjustments.
            // Debit note: goods leave at their book cost; the difference to the value charged back is an adjustment.
            await Add(LedgerAccountRole.InventoryAdjustment, s * d.TaxableAmount - stockValue,
                d.Kind == BillingDocumentKind.PurchaseBill ? "Cost of units issued while stock was negative" : "Purchase return at a value other than book cost");
        }

        return amounts.Where(a => a.Value != 0)
            .Select(a => a.Value > 0 ? new LedgerPostingLine(a.Key, a.Value, 0, notes[a.Key]) : new LedgerPostingLine(a.Key, 0, -a.Value, notes[a.Key]))
            .ToList();
    }

    /// <summary>A credit-mode return settles its original invoice or bill first; anything left is unapplied credit.</summary>
    private async Task AutoAllocateReturnAsync(IUnitOfWork unit, SessionInfo session, BillingDocument d, CancellationToken cancellationToken)
    {
        if (!d.Kind.IsReturn() || d.Mode != SettlementMode.Credit || d.PartyId is not { } partyId || d.OriginalDocumentId is not { } originalId) return;
        var items = await OpenItems.LoadAsync(unit, partyId, cancellationToken);
        var original = items.SingleOrDefault(i => i.Id == originalId);
        var amount = Math.Min(original?.Open ?? 0, d.Total);
        if (amount > 0)
            unit.Add(new Allocation(Guid.NewGuid(), session.CompanyId, partyId, OpenItemSource.Document, originalId, OpenItemSource.Document, d.Id, amount, d.Date,
                clock.UtcNow, session.UserId));
    }

    /// <summary>
    /// Credit sales may not take the customer's receivable (its ledger balance) above a non-zero credit limit unless the
    /// poster overrides it, which needs sales.credit-override and is recorded in the audit trail.
    /// </summary>
    private async Task EnsureCreditLimitAsync(IUnitOfWork unit, SessionInfo session, BillingDocument d, bool overrideLimit, CancellationToken cancellationToken)
    {
        if (d.Kind != BillingDocumentKind.SalesInvoice || d.Mode != SettlementMode.Credit || d.PartyId is not { } partyId) return;
        var party = await unit.FindAsync<Party>(partyId, cancellationToken);
        if (party is null || party.CreditLimit <= 0) return;
        var balance = (await unit.ListAsync<LedgerEntry>(e => e.AccountId == party.AccountId, cancellationToken)).Sum(e => e.Debit - e.Credit);
        var exposure = balance + d.Total;
        if (exposure <= party.CreditLimit) return;
        if (!overrideLimit)
            throw new CreditLimitExceededException(
                $"{party.Name}'s credit limit is {party.CreditLimit:N2}. With this invoice the receivable would be {exposure:N2}, which is {exposure - party.CreditLimit:N2} over the limit.",
                party.CreditLimit, exposure);
        if (!session.Has(Permissions.SalesCreditOverride))
            throw new AccessDeniedException($"Posting above {party.Name}'s credit limit needs the permission \"Sales: Override credit limit\".");
        unit.Record(new AuditEvent("CreditLimitOverridden", nameof(BillingDocument), d.Id.ToString(),
            $"Credit limit {party.CreditLimit:N2} of {party.Name} overridden: receivable after posting {exposure:N2}"));
    }

    /// <summary>A supplier's bill number may be recorded once per supplier (cancelled bills excepted).</summary>
    private static async Task EnsureNotDuplicateBillAsync(IUnitOfWork unit, BillingDocument d, CancellationToken cancellationToken)
    {
        if (d.Kind != BillingDocumentKind.PurchaseBill || d.Reference.Length == 0) return;
        var reference = d.Reference;
        var partyId = d.PartyId;
        var same = await unit.ListAsync<BillingDocument>(x => x.Kind == BillingDocumentKind.PurchaseBill && x.Status == BillingStatus.Posted
            && x.PartyId == partyId && x.Reference == reference && x.Id != d.Id, cancellationToken);
        if (same.Count == 0) return;
        var ids = same.Select(x => x.Id).ToList();
        var cancelled = (await unit.ListAsync<BillingCancellation>(c => ids.Contains(c.DocumentId), cancellationToken)).Select(c => c.DocumentId).ToHashSet();
        var live = same.FirstOrDefault(x => !cancelled.Contains(x.Id));
        if (live is not null) throw new WorkspaceValidationException($"Supplier bill {reference} is already recorded as {live.Number}.");
    }

    /// <summary>Quantity, amounts and cost already returned per original line by posted, uncancelled returns (other than <paramref name="exclude"/>).</summary>
    private static async Task<Dictionary<Guid, (decimal Quantity, LineAmounts Amounts, decimal Cost)>> ReturnedAsync(IUnitOfWork unit, List<Guid> originalLineIds,
        Guid? exclude, CancellationToken cancellationToken)
    {
        var lines = await unit.ListAsync<BillingLine>(l => l.OriginalLineId != null && originalLineIds.Contains(l.OriginalLineId!.Value) && l.DocumentId != exclude, cancellationToken);
        var documentIds = lines.Select(l => l.DocumentId).Distinct().ToList();
        var posted = (await unit.ListAsync<BillingDocument>(d => documentIds.Contains(d.Id) && d.Status == BillingStatus.Posted, cancellationToken)).Select(d => d.Id).ToHashSet();
        var cancelled = (await unit.ListAsync<BillingCancellation>(c => documentIds.Contains(c.DocumentId), cancellationToken)).Select(c => c.DocumentId).ToHashSet();
        return lines.Where(l => posted.Contains(l.DocumentId) && !cancelled.Contains(l.DocumentId))
            .GroupBy(l => l.OriginalLineId!.Value)
            .ToDictionary(g => g.Key, g => (g.Sum(l => l.Quantity), g.Aggregate(LineAmounts.Zero, (a, l) => a.Plus(l.Amounts)), g.Sum(l => l.CostValue)));
    }

    private static async Task<Dictionary<Guid, (decimal Quantity, decimal Cost)>> PriorReturnsAsync(IUnitOfWork unit, BillingDocument d, CancellationToken cancellationToken)
    {
        if (d.OriginalDocumentId is not { } originalId) return [];
        var ids = (await unit.ListAsync<BillingLine>(l => l.DocumentId == originalId, cancellationToken)).Select(l => l.Id).ToList();
        return (await ReturnedAsync(unit, ids, d.Id, cancellationToken)).ToDictionary(p => p.Key, p => (p.Value.Quantity, p.Value.Cost));
    }

    private static async Task<BillingSettings> SettingsAsync(IUnitOfWork unit, CancellationToken cancellationToken) =>
        (await unit.ListAsync<BillingSettings>(cancellationToken: cancellationToken)).SingleOrDefault()
        ?? throw new WorkspaceValidationException("Billing settings are missing. Sign out and sign in again to finish the upgrade.");

    private static PaymentStatus StatusOf(BillingDocument d, bool cancelled, decimal outstanding) =>
        d.Status == BillingStatus.Draft ? PaymentStatus.Draft
        : cancelled ? PaymentStatus.Cancelled
        : d.Mode == SettlementMode.Cash || outstanding == 0 ? PaymentStatus.Paid
        : outstanding == d.Total ? PaymentStatus.Unpaid
        : PaymentStatus.PartlyPaid;

    private static string CreatePermission(BillingDocumentKind kind) => PartyService.CreatePermission(kind.PartyKind());

    private static decimal Quantity(decimal quantity, int lineNumber)
    {
        if (quantity <= 0 || decimal.Round(quantity, 3) != quantity)
            throw new WorkspaceValidationException($"Line {lineNumber}: quantity must be more than zero, with at most 3 decimal places.");
        return quantity;
    }

    private static decimal Factor(Product product, Guid unitId, List<UnitConversion> conversions, int lineNumber)
    {
        if (unitId == product.UnitId) return 1;
        return conversions.FirstOrDefault(c => c.ProductId == product.Id && c.UnitId == unitId)?.Factor
            ?? throw new WorkspaceValidationException($"Line {lineNumber}: {product.Sku} has no conversion for that unit.");
    }

    private static decimal BaseOf(BillingLine source, decimal quantity, Dictionary<Guid, Unit> unitRows, Dictionary<Guid, Product> products, int lineNumber)
    {
        var baseQuantity = source.BaseQuantity * quantity / source.Quantity;
        var decimals = source.ProductId is { } pid && products.TryGetValue(pid, out var p) && unitRows.TryGetValue(p.UnitId, out var u) ? u.DecimalPlaces : 3;
        if (decimal.Round(baseQuantity, decimals) != baseQuantity)
            throw new WorkspaceValidationException($"Line {lineNumber}: {quantity:0.###} does not convert to a whole base quantity.");
        return baseQuantity;
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private static void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (DomainException ex)
        {
            throw new WorkspaceValidationException(ex.Message);
        }
    }

    private static T Guard<T>(Func<T> func, int lineNumber)
    {
        try
        {
            return func();
        }
        catch (DomainException ex)
        {
            throw new WorkspaceValidationException($"Line {lineNumber}: {ex.Message}");
        }
    }
}
