using Nexora.Application.Abstractions;
using Nexora.Application.Security;
using Nexora.Domain.Accounting;
using Nexora.Domain.Audit;
using Nexora.Domain.Common;
using Nexora.Domain.Companies;
using Nexora.Domain.Inventory;
using Nexora.Domain.Security;

namespace Nexora.Application.Inventory;

/// <summary>
/// One line of a stock document. Quantity is in <see cref="UnitId"/> (the product's base unit when null).
/// For a physical count, Quantity is the counted quantity and may be zero. Batch-tracked products need a batch
/// (an existing <see cref="BatchId"/>, or a <see cref="BatchNumber"/> that is created on receipt).
/// Serial-tracked products need one line per serial number with quantity 1.
/// </summary>
/// <para><see cref="LineValue"/> is the exact value of the whole line, used by purchase bills and sales returns instead of a unit cost.</para>
public sealed record StockLineInput(Guid ProductId, decimal Quantity, Guid? UnitId = null, decimal? UnitCost = null,
    Guid? BatchId = null, string? BatchNumber = null, DateOnly? ManufacturedOn = null, DateOnly? ExpiresOn = null, string? SerialNumber = null,
    decimal? LineValue = null);

/// <summary>
/// Value of one posted line, signed like the movement: <see cref="StockValue"/> is what the stock ledger records;
/// <see cref="EnteredValue"/> is what incoming stock cost. They differ only when a receipt settles negative stock.
/// </summary>
internal sealed record StockLineValue(int LineNumber, decimal EnteredValue, decimal StockValue);

internal sealed record StockCoreResult(StockDocument Document, IReadOnlyList<StockLineValue> Lines, decimal Increases, decimal Decreases, decimal Entered);

public sealed record StockDocumentInput(StockDocumentType Type, DateOnly Date, Guid WarehouseId, string Narration, IReadOnlyList<StockLineInput> Lines,
    Guid? ToWarehouseId = null, Guid? CounterAccountId = null, StockState FromState = StockState.Good, StockState ToState = StockState.Good);

public sealed record StockPostingResult(Guid DocumentId, string Number, Guid? VoucherId, decimal ValueChange);

public sealed record StockBalanceRow(Guid ProductId, string Sku, string ProductName, Guid WarehouseId, string Warehouse, StockState State,
    Guid? BatchId, string Batch, DateOnly? ExpiresOn, string? SerialNumber, decimal Quantity, string Unit);

public sealed record ValuationRow(Guid ProductId, string Sku, string ProductName, decimal Quantity, decimal Value, decimal UnitCost);

public sealed record StockReconciliation(DateOnly AsOf, decimal StockValue, decimal LedgerValue)
{
    public decimal Difference => StockValue - LedgerValue;

    public bool IsReconciled => Difference == 0;
}

public sealed record MovementRow(long Sequence, DateOnly Date, string DocumentNumber, StockDocumentType DocumentType, string Sku, string ProductName,
    string Warehouse, StockState State, string Batch, string? SerialNumber, decimal Quantity, decimal? Value);

public sealed record StockDocumentRow(Guid Id, string Number, StockDocumentType Type, DateOnly Date, string Narration, string Warehouse,
    string? ToWarehouse, decimal? Value, bool IsReversed, Guid? ReversesDocumentId);

public sealed record LowStockRow(Guid ProductId, string Sku, string ProductName, decimal Available, decimal ReorderLevel, string Unit);

public sealed record ExpiringBatchRow(string Sku, string ProductName, string Batch, DateOnly ExpiresOn, decimal Quantity, bool IsExpired);

/// <summary>
/// Stock documents and the stock ledger. Each document is posted in one transaction: its movements, its number, and
/// (when value changes) its accounting voucher through <see cref="ILedgerPoster"/>. See docs/ACCOUNTING_MAP.md.
/// </summary>
public sealed class StockService(IUnitOfWorkFactory units, IAuthorizer authorizer, ILedgerPoster ledger, IClock clock)
{
    private const string SourceType = "stock";

    public async Task<StockPostingResult> PostAsync(StockDocumentInput input, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.InventoryAdjust);
        if (input.Type is StockDocumentType.Opening or StockDocumentType.Receipt) authorizer.Demand(Permissions.InventoryViewCost);
        if (input.Type == StockDocumentType.Reversal) throw new WorkspaceValidationException("Reverse a document from the documents list.");
        if (input.Type.IsBilling()) throw new WorkspaceValidationException("Sales and purchase stock is posted by sales and purchase documents.");

        using var lockHandle = await ledger.AcquireAsync(cancellationToken);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var year = FinancialYear.Containing(input.Date);
        await EnsureOpenYearAsync(unit, session, input.Date, cancellationToken);
        var number = await NumberAllocator.NextAsync(unit, session.CompanyId, input.Type.NumberingKey(), year,
            candidate => unit.AnyAsync<StockDocument>(d => d.Number == candidate, cancellationToken), cancellationToken);
        var posted = await PostCoreAsync(unit, session, input, number, cancellationToken);
        var document = posted.Document;
        var (increases, decreases) = (posted.Increases, posted.Decreases);

        Guid? voucherId = null;
        var lines = await AccountingLinesAsync(unit, input, increases, decreases, posted.Entered, cancellationToken);
        if (lines.Count > 0)
        {
            voucherId = await ledger.PostAsync(unit,
                new LedgerPosting(input.Date, $"{input.Type.Label()} {number}{(document.Narration.Length > 0 ? " · " + document.Narration : string.Empty)}",
                    SourceType, document.Id, lines), cancellationToken);
            document.AttachVoucher(voucherId.Value);
        }

        unit.Record(new AuditEvent("Posted", nameof(StockDocument), document.Id.ToString(),
            $"Posted {input.Type.Label().ToLowerInvariant()} {number}" + (increases - decreases != 0 ? $" changing stock value by {increases - decreases:N2}" : string.Empty)));
        await unit.CommitAsync(cancellationToken);
        return new StockPostingResult(document.Id, number, voucherId, increases - decreases);
    }

    /// <summary>
    /// Adds a stock document and its movements to the caller's unit of work, without an accounting voucher, and returns
    /// the value of each line. Used by <see cref="PostAsync"/> and by sales and purchase documents, which post one
    /// voucher for the whole document. The caller holds the posting lock, checks permissions and allocates the number.
    /// </summary>
    internal async Task<StockCoreResult> PostCoreAsync(IUnitOfWork unit, SessionInfo session, StockDocumentInput input, string number,
        CancellationToken cancellationToken)
    {
        if (input.Lines.Count == 0) throw new WorkspaceValidationException("Add at least one line.");
        if (input.Lines.Count > 500) throw new WorkspaceValidationException("A stock document can have at most 500 lines.");
        var context = await PostingContext.LoadAsync(unit, input.Lines.Select(l => l.ProductId), cancellationToken);
        await EnsureOpenYearAsync(unit, session, input.Date, cancellationToken);
        var warehouse = await ActiveWarehouseAsync(unit, input.WarehouseId, cancellationToken);
        Domain.Companies.Warehouse? destination = null;
        if (input.Type == StockDocumentType.Transfer)
        {
            if (input.ToWarehouseId is not { } to) throw new WorkspaceValidationException("Choose the destination warehouse.");
            destination = await ActiveWarehouseAsync(unit, to, cancellationToken);
        }

        if (input.Type == StockDocumentType.StateChange && input.FromState == input.ToState)
            throw new WorkspaceValidationException("Choose a different new state.");

        var documentId = Guid.NewGuid();
        var document = new StockDocument(documentId, session.CompanyId, input.Type, number, input.Date, input.Narration,
            warehouse.Id, destination?.Id, input.CounterAccountId, null, clock.UtcNow, session.UserId);
        unit.Add(document);

        // increases and decreases are stock values; entered is what incoming stock cost, which differs from its stock
        // value only when a receipt settles negative stock (CostPool.Receive). The difference is a cost adjustment.
        decimal increases = 0, decreases = 0, entered = 0;
        var values = new List<StockLineValue>(input.Lines.Count);
        var serials = new HashSet<(Guid, string)>();
        for (var i = 0; i < input.Lines.Count; i++)
        {
            var line = input.Lines[i];
            var lineNumber = i + 1;
            var product = context.Product(line.ProductId, lineNumber);
            context.EnsureDate(product, input.Date);
            var quantity = input.Type == StockDocumentType.PhysicalCount
                ? context.CountedBaseQuantity(product, line, lineNumber)
                : context.BaseQuantity(product, line, lineNumber);
            var batch = await context.ResolveBatchAsync(unit, session.CompanyId, product, line, input.Type, lineNumber, cancellationToken);
            var serial = context.Serial(product, line, quantity, lineNumber);
            if (serial is not null && !serials.Add((product.Id, serial)))
                throw new WorkspaceValidationException($"Line {lineNumber}: serial {serial} of {product.Sku} is listed twice in this document.");
            var pool = context.Pool(product);
            decimal lineEntered = 0, lineStock = 0;

            switch (input.Type)
            {
                case StockDocumentType.Opening:
                case StockDocumentType.Receipt:
                case StockDocumentType.Purchase:
                case StockDocumentType.SalesReturn:
                {
                    var value = line.LineValue is { } exact ? ExactValue(exact, lineNumber)
                        : EnteredCost(line, quantity, line.UnitCost ?? throw new WorkspaceValidationException($"Line {lineNumber}: enter the unit cost."), lineNumber);
                    context.EnsureSerialNotInStock(product, serial, lineNumber);
                    var stockValue = pool.Receive(quantity, value);
                    context.Add(document, lineNumber, product, warehouse.Id, StockState.Good, batch, serial, input.Date, quantity, stockValue, false, line);
                    (lineEntered, lineStock) = (value, stockValue);
                    break;
                }

                case StockDocumentType.AdjustmentIn:
                {
                    var value = line.UnitCost is { } typed ? EnteredCost(line, quantity, typed, lineNumber) : CostOf(quantity, decimal.Round(pool.CurrentUnitCost, 4), lineNumber);
                    context.EnsureSerialNotInStock(product, serial, lineNumber);
                    var stockValue = pool.Receive(quantity, value);
                    context.Add(document, lineNumber, product, warehouse.Id, input.FromState, batch, serial, input.Date, quantity, stockValue, false, line);
                    (lineEntered, lineStock) = (value, stockValue);
                    break;
                }

                case StockDocumentType.Issue:
                case StockDocumentType.AdjustmentOut:
                case StockDocumentType.Sale:
                case StockDocumentType.PurchaseReturn:
                {
                    context.EnsureAvailable(product, warehouse.Id, input.FromState, batch, serial, quantity, lineNumber);
                    var value = pool.Issue(quantity, context.Settings.AllowNegativeStock);
                    context.Add(document, lineNumber, product, warehouse.Id, input.FromState, batch, serial, input.Date, -quantity, -value, false, line);
                    (lineEntered, lineStock) = (-value, -value);
                    break;
                }

                case StockDocumentType.Transfer:
                    context.EnsureAvailable(product, warehouse.Id, input.FromState, batch, serial, quantity, lineNumber);
                    context.Add(document, lineNumber, product, warehouse.Id, input.FromState, batch, serial, input.Date, -quantity, 0, true, line);
                    context.Add(document, lineNumber, product, destination!.Id, input.FromState, batch, serial, input.Date, quantity, 0, true, line);
                    break;

                case StockDocumentType.StateChange:
                    context.EnsureAvailable(product, warehouse.Id, input.FromState, batch, serial, quantity, lineNumber);
                    context.Add(document, lineNumber, product, warehouse.Id, input.FromState, batch, serial, input.Date, -quantity, 0, true, line);
                    context.Add(document, lineNumber, product, warehouse.Id, input.ToState, batch, serial, input.Date, quantity, 0, true, line);
                    break;

                case StockDocumentType.PhysicalCount:
                {
                    var book = context.Balance(product.Id, warehouse.Id, StockState.Good, batch?.Id, serial);
                    var difference = quantity - book;
                    if (difference > 0)
                    {
                        context.EnsureSerialNotInStock(product, serial, lineNumber);
                        var value = CostOf(difference, decimal.Round(pool.CurrentUnitCost, 4), lineNumber);
                        var stockValue = pool.Receive(difference, value);
                        context.Add(document, lineNumber, product, warehouse.Id, StockState.Good, batch, serial, input.Date, difference, stockValue, false, line);
                        (lineEntered, lineStock) = (value, stockValue);
                    }
                    else if (difference < 0)
                    {
                        var value = pool.Issue(-difference, allowNegative: true);
                        context.Add(document, lineNumber, product, warehouse.Id, StockState.Good, batch, serial, input.Date, difference, -value, false, line);
                        (lineEntered, lineStock) = (-value, -value);
                    }

                    break;
                }

                default:
                    throw new WorkspaceValidationException("This document type cannot be posted here.");
            }

            if (lineStock > 0) increases += lineStock;
            else decreases -= lineStock;
            if (lineEntered > 0) entered += lineEntered;
            values.Add(new StockLineValue(lineNumber, lineEntered, lineStock));
        }

        return new StockCoreResult(document, values, increases, decreases, entered);
    }

    /// <summary>
    /// Reverses a stock document with opposite movements at the original values, and reverses its voucher.
    /// Allowed only while the document is the latest stock activity for each of its products, which keeps costing exact.
    /// </summary>
    public async Task<string> ReverseAsync(Guid documentId, DateOnly date, string narration, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.InventoryAdjust);
        using var lockHandle = await ledger.AcquireAsync(cancellationToken);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var original = await unit.FindAsync<StockDocument>(documentId, cancellationToken)
            ?? throw new WorkspaceValidationException("That stock document no longer exists.");
        if (original.Type.IsBilling())
            throw new WorkspaceValidationException($"Stock document {original.Number} belongs to a sales or purchase document. Cancel or return it there.");
        var reversal = await ReverseCoreAsync(unit, session, original, date, narration, cancellationToken);
        if (original.VoucherId is { } voucherId)
        {
            var reversalVoucher = await ledger.ReverseAsync(unit, voucherId, date, $"Reversal of stock document {original.Number}", cancellationToken);
            reversal.AttachVoucher(reversalVoucher);
        }

        unit.Record(new AuditEvent("Reversed", nameof(StockDocument), original.Id.ToString(), $"Reversed stock document {original.Number} with {reversal.Number}"));
        await unit.CommitAsync(cancellationToken);
        return reversal.Number;
    }

    /// <summary>
    /// Adds the reversal of <paramref name="original"/> (opposite movements at the original values) to the caller's unit of
    /// work. Does not reverse any voucher: the caller does, because a sales or purchase voucher covers more than stock.
    /// </summary>
    internal async Task<StockDocument> ReverseCoreAsync(IUnitOfWork unit, SessionInfo session, StockDocument original, DateOnly date, string narration,
        CancellationToken cancellationToken)
    {
        var documentId = original.Id;
        if (original.Type == StockDocumentType.Reversal) throw new WorkspaceValidationException("A reversal cannot itself be reversed.");
        if (await unit.AnyAsync<StockDocument>(d => d.ReversesDocumentId == documentId, cancellationToken))
            throw new WorkspaceValidationException($"Stock document {original.Number} has already been reversed.");
        if (date < original.Date) throw new WorkspaceValidationException("A reversal cannot be dated before the document it reverses.");
        await EnsureOpenYearAsync(unit, session, date, cancellationToken);

        var movements = await unit.ListAsync<StockMovement>(m => m.DocumentId == documentId, cancellationToken);
        var productIds = movements.Select(m => m.ProductId).Distinct().ToList();
        var context = await PostingContext.LoadAsync(unit, productIds, cancellationToken);
        if (movements.Count > 0)
        {
            // Later documents that have themselves been reversed (with their reversals) no longer count, so a chain can
            // be reversed newest first. Costing skips every reversed pair, which restores the pools exactly.
            var last = movements.Max(m => m.Sequence);
            if (context.HasLiveMovementsAfter(productIds, last))
                throw new WorkspaceValidationException("Later stock movements exist for these products. Reverse the later documents first, or post an adjustment instead.");
        }

        var year = FinancialYear.Containing(date);
        var number = await NumberAllocator.NextAsync(unit, session.CompanyId, StockDocumentType.Reversal.NumberingKey(), year,
            candidate => unit.AnyAsync<StockDocument>(d => d.Number == candidate, cancellationToken), cancellationToken);
        var reversal = new StockDocument(Guid.NewGuid(), session.CompanyId, StockDocumentType.Reversal, number, date,
            string.IsNullOrWhiteSpace(narration) ? $"Reversal of {original.Number}" : narration,
            original.WarehouseId, original.ToWarehouseId, original.CounterAccountId, original.Id, clock.UtcNow, session.UserId);
        unit.Add(reversal);

        foreach (var movement in movements.OrderBy(m => m.Sequence))
        {
            var product = context.Product(movement.ProductId, movement.LineNumber);
            context.EnsureDate(product, date);
            if (movement.Quantity > 0)
            {
                var batch = movement.BatchId is { } b ? context.Batch(b) : null;
                context.EnsureAvailable(product, movement.WarehouseId, movement.State, batch, movement.SerialNumber, movement.Quantity, movement.LineNumber);
            }

            context.AddReversal(reversal, movement);
        }

        return reversal;
    }

    public async Task<IReadOnlyList<StockBalanceRow>> BalancesAsync(DateOnly? asOf = null, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.InventoryView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var date = asOf ?? DateOnly.MaxValue;
        var movements = await unit.ListAsync<StockMovement>(m => m.Date <= date, cancellationToken);
        var lookup = await Lookup.LoadAsync(unit, cancellationToken);
        return movements
            .GroupBy(m => (m.ProductId, m.WarehouseId, m.State, m.BatchId, m.SerialNumber))
            .Select(g => (g.Key, Quantity: g.Sum(m => m.Quantity)))
            .Where(x => x.Quantity != 0)
            .Select(x =>
            {
                var product = lookup.Products[x.Key.ProductId];
                var batch = x.Key.BatchId is { } b ? lookup.Batches[b] : null;
                return new StockBalanceRow(product.Id, product.Sku, product.DisplayName, x.Key.WarehouseId, lookup.Warehouses.GetValueOrDefault(x.Key.WarehouseId, "?"),
                    x.Key.State, x.Key.BatchId, batch?.Number ?? string.Empty, batch?.ExpiresOn, x.Key.SerialNumber, x.Quantity, lookup.UnitCode(product));
            })
            .OrderBy(r => r.Sku, StringComparer.Ordinal).ThenBy(r => r.Warehouse).ThenBy(r => r.State).ThenBy(r => r.Batch)
            .ToList();
    }

    /// <summary>Quantity and value per product at cost. Requires inventory.view-cost.</summary>
    public async Task<IReadOnlyList<ValuationRow>> ValuationAsync(DateOnly? asOf = null, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.InventoryViewCost);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var date = asOf ?? DateOnly.MaxValue;
        var movements = await unit.ListAsync<StockMovement>(m => m.Date <= date, cancellationToken);
        var lookup = await Lookup.LoadAsync(unit, cancellationToken);
        return movements.GroupBy(m => m.ProductId)
            .Select(g => (Product: lookup.Products[g.Key], Quantity: g.Sum(m => m.Quantity), Value: g.Sum(m => m.Value)))
            .Where(x => x.Quantity != 0 || x.Value != 0)
            .Select(x => new ValuationRow(x.Product.Id, x.Product.Sku, x.Product.DisplayName, x.Quantity, x.Value,
                x.Quantity > 0 ? decimal.Round(x.Value / x.Quantity, 4) : 0))
            .OrderBy(r => r.Sku, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Compares stock value (from the stock ledger) with the balance of the Inventory accounts in the general ledger.
    /// They must be equal; manual vouchers cannot post to Inventory, so only stock documents move it.
    /// </summary>
    public async Task<StockReconciliation> ReconcileAsync(DateOnly asOf, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.InventoryViewCost);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var stock = (await unit.ListAsync<StockMovement>(m => m.Date <= asOf, cancellationToken)).Sum(m => m.Value);
        var inventoryAccounts = (await unit.ListAsync<Account>(a => a.Kind == AccountKind.Inventory, cancellationToken)).Select(a => a.Id).ToList();
        var ledgerValue = (await unit.ListAsync<LedgerEntry>(e => inventoryAccounts.Contains(e.AccountId) && e.Date <= asOf, cancellationToken))
            .Sum(e => e.Debit - e.Credit);
        return new StockReconciliation(asOf, stock, ledgerValue);
    }

    public async Task<IReadOnlyList<MovementRow>> MovementsAsync(Guid? productId = null, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.InventoryView);
        var showCost = authorizer.Has(Permissions.InventoryViewCost);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var movements = await unit.ListAsync<StockMovement>(m => productId == null || m.ProductId == productId, cancellationToken);
        var documents = (await unit.ListAsync<StockDocument>(cancellationToken: cancellationToken)).ToDictionary(d => d.Id);
        var lookup = await Lookup.LoadAsync(unit, cancellationToken);
        return movements.OrderByDescending(m => m.Sequence)
            .Select(m =>
            {
                var product = lookup.Products[m.ProductId];
                return new MovementRow(m.Sequence, m.Date, documents[m.DocumentId].Number, documents[m.DocumentId].Type, product.Sku, product.DisplayName,
                    lookup.Warehouses.GetValueOrDefault(m.WarehouseId, "?"), m.State, m.BatchId is { } b ? lookup.Batches[b].Number : string.Empty,
                    m.SerialNumber, m.Quantity, showCost ? m.Value : null);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<StockDocumentRow>> DocumentsAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.InventoryView);
        var showCost = authorizer.Has(Permissions.InventoryViewCost);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var documents = await unit.ListAsync<StockDocument>(cancellationToken: cancellationToken);
        var values = (await unit.ListAsync<StockMovement>(cancellationToken: cancellationToken)).GroupBy(m => m.DocumentId).ToDictionary(g => g.Key, g => g.Sum(m => m.Value));
        var reversed = documents.Where(d => d.ReversesDocumentId != null).Select(d => d.ReversesDocumentId!.Value).ToHashSet();
        var warehouses = (await unit.ListAsync<Domain.Companies.Warehouse>(cancellationToken: cancellationToken)).ToDictionary(w => w.Id, w => w.Name);
        return documents.OrderByDescending(d => d.PostedUtc)
            .Select(d => new StockDocumentRow(d.Id, d.Number, d.Type, d.Date, d.Narration, warehouses.GetValueOrDefault(d.WarehouseId, "?"),
                d.ToWarehouseId is { } to ? warehouses.GetValueOrDefault(to, "?") : null, showCost ? values.GetValueOrDefault(d.Id) : null,
                reversed.Contains(d.Id), d.ReversesDocumentId))
            .ToList();
    }

    /// <summary>Active products whose available (good) stock is at or below their reorder level.</summary>
    public async Task<IReadOnlyList<LowStockRow>> LowStockAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.InventoryView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var lookup = await Lookup.LoadAsync(unit, cancellationToken);
        var available = (await unit.ListAsync<StockMovement>(m => m.State == StockState.Good, cancellationToken))
            .GroupBy(m => m.ProductId).ToDictionary(g => g.Key, g => g.Sum(m => m.Quantity));
        return lookup.Products.Values
            .Where(p => !p.IsArchived && p.ReorderLevel > 0 && available.GetValueOrDefault(p.Id) <= p.ReorderLevel)
            .OrderBy(p => p.Sku, StringComparer.Ordinal)
            .Select(p => new LowStockRow(p.Id, p.Sku, p.DisplayName, available.GetValueOrDefault(p.Id), p.ReorderLevel, lookup.UnitCode(p)))
            .ToList();
    }

    /// <summary>Batches in stock that have expired or will expire within <paramref name="withinDays"/> days.</summary>
    public async Task<IReadOnlyList<ExpiringBatchRow>> ExpiringBatchesAsync(DateOnly today, int withinDays = 30, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.InventoryView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var lookup = await Lookup.LoadAsync(unit, cancellationToken);
        var limit = today.AddDays(withinDays);
        var quantities = (await unit.ListAsync<StockMovement>(m => m.BatchId != null, cancellationToken))
            .GroupBy(m => m.BatchId!.Value).ToDictionary(g => g.Key, g => g.Sum(m => m.Quantity));
        return lookup.Batches.Values
            .Where(b => b.ExpiresOn is { } e && e <= limit && quantities.GetValueOrDefault(b.Id) > 0)
            .OrderBy(b => b.ExpiresOn)
            .Select(b => new ExpiringBatchRow(lookup.Products[b.ProductId].Sku, lookup.Products[b.ProductId].DisplayName, b.Number, b.ExpiresOn!.Value,
                quantities[b.Id], b.IsExpiredOn(today)))
            .ToList();
    }

    private async Task<List<LedgerPostingLine>> AccountingLinesAsync(IUnitOfWork unit, StockDocumentInput input, decimal increases, decimal decreases,
        decimal entered, CancellationToken cancellationToken)
    {
        var lines = new List<LedgerPostingLine>();
        if (increases == 0 && decreases == 0 && entered == 0) return lines;
        var inventory = await ledger.AccountAsync(unit, LedgerAccountRole.Inventory, cancellationToken);

        switch (input.Type)
        {
            case StockDocumentType.Opening:
            case StockDocumentType.Receipt:
                var credit = input.Type == StockDocumentType.Opening
                    ? await ledger.AccountAsync(unit, LedgerAccountRole.OpeningBalanceDifference, cancellationToken)
                    : await CounterAccountAsync(unit, input.CounterAccountId, "credit", cancellationToken);
                if (increases != 0) lines.Add(new(inventory, increases, 0));
                var settlement = entered - increases;
                if (settlement != 0)
                {
                    // Units issued earlier while stock was negative cost more (or less) than estimated.
                    var settlementAccount = await ledger.AccountAsync(unit, LedgerAccountRole.InventoryAdjustment, cancellationToken);
                    const string note = "Cost of units issued while stock was negative";
                    lines.Add(settlement > 0 ? new(settlementAccount, settlement, 0, note) : new(settlementAccount, 0, -settlement, note));
                }

                if (entered != 0) lines.Add(new(credit, 0, entered));
                break;
            case StockDocumentType.Issue:
                var debit = input.CounterAccountId is null
                    ? await ledger.AccountAsync(unit, LedgerAccountRole.InventoryAdjustment, cancellationToken)
                    : await CounterAccountAsync(unit, input.CounterAccountId, "debit", cancellationToken);
                lines.Add(new(debit, decreases, 0));
                lines.Add(new(inventory, 0, decreases));
                break;
            default:
                var adjustment = await ledger.AccountAsync(unit, LedgerAccountRole.InventoryAdjustment, cancellationToken);
                if (increases > 0)
                {
                    lines.Add(new(inventory, increases, 0, "Stock gained"));
                    lines.Add(new(adjustment, 0, increases, "Stock gained"));
                }

                if (decreases > 0)
                {
                    lines.Add(new(adjustment, decreases, 0, "Stock lost or written off"));
                    lines.Add(new(inventory, 0, decreases, "Stock lost or written off"));
                }

                break;
        }

        return lines;
    }

    private static async Task<Guid> CounterAccountAsync(IUnitOfWork unit, Guid? accountId, string side, CancellationToken cancellationToken)
    {
        if (accountId is not { } id) throw new WorkspaceValidationException($"Choose the account to {side} (for example the supplier).");
        var account = await unit.FindAsync<Account>(id, cancellationToken) ?? throw new WorkspaceValidationException("That account no longer exists.");
        if (account.IsGroup || !account.IsActive) throw new WorkspaceValidationException($"{account.Name} cannot be used: choose an active ledger account, not a group.");
        if (account.Kind == AccountKind.Inventory) throw new WorkspaceValidationException("Choose an account other than Inventory.");
        return id;
    }

    /// <summary>A typed unit cost is per the unit the line was entered in (for example per BOX), not per base unit.</summary>
    private static decimal EnteredCost(StockLineInput line, decimal baseQuantity, decimal unitCost, int lineNumber) =>
        CostOf(line.UnitId is null ? baseQuantity : line.Quantity, unitCost, lineNumber);

    private static decimal ExactValue(decimal value, int lineNumber)
    {
        if (value < 0 || !Money.HasValidScale(value)) throw new WorkspaceValidationException($"Line {lineNumber}: the value must be zero or more, in rupees and paise.");
        return value;
    }

    private static decimal CostOf(decimal quantity, decimal unitCost, int lineNumber)
    {
        if (unitCost < 0) throw new WorkspaceValidationException($"Line {lineNumber}: the unit cost cannot be negative.");
        if (decimal.Round(unitCost, 4) != unitCost) throw new WorkspaceValidationException($"Line {lineNumber}: the unit cost can have at most 4 decimal places.");
        return Money.Round(quantity * unitCost);
    }

    private static async Task<Domain.Companies.Warehouse> ActiveWarehouseAsync(IUnitOfWork unit, Guid id, CancellationToken cancellationToken)
    {
        var warehouse = await unit.FindAsync<Domain.Companies.Warehouse>(id, cancellationToken)
            ?? throw new WorkspaceValidationException("Choose a warehouse. Add warehouses under Settings → Branches and warehouses.");
        if (!warehouse.IsActive) throw new WorkspaceValidationException($"Warehouse {warehouse.Name} is inactive.");
        return warehouse;
    }

    private static async Task EnsureOpenYearAsync(IUnitOfWork unit, SessionInfo session, DateOnly date, CancellationToken cancellationToken)
    {
        var company = await unit.FindAsync<Company>(session.CompanyId, cancellationToken) ?? throw new WorkspaceValidationException("Sign in again.");
        if (date < company.FirstFinancialYear.StartDate)
            throw new WorkspaceValidationException($"The company's books start on {company.FirstFinancialYear.StartDate:dd MMM yyyy}. Choose a later date.");
        var year = FinancialYear.Containing(date);
        var period = (await unit.ListAsync<FinancialYearPeriod>(p => p.StartYear == year.StartYear, cancellationToken)).SingleOrDefault()
            ?? throw new WorkspaceValidationException($"FY {year.Label} has not been opened. Open it under Settings → Financial years first.");
        if (period.IsClosed) throw new WorkspaceValidationException($"FY {year.Label} is closed. Post in an open year or ask an administrator to reopen it.");
    }

    /// <summary>Everything one posting needs: products, units, conversions, batches, existing movements and cost pools.</summary>
    private sealed class PostingContext
    {
        private readonly Dictionary<Guid, Product> _products;
        private readonly Dictionary<Guid, Unit> _units;
        private readonly List<UnitConversion> _conversions;
        private readonly Dictionary<Guid, Batch> _batches;
        private readonly List<StockMovement> _movements;
        private readonly HashSet<Guid> _parents;
        private readonly HashSet<Guid> _skipped;
        private readonly Dictionary<Guid, CostPool> _pools = [];
        private readonly IUnitOfWork _unit;
        private long _nextSequence;

        private PostingContext(IUnitOfWork unit, InventorySettings settings, Dictionary<Guid, Product> products, Dictionary<Guid, Unit> units,
            List<UnitConversion> conversions, Dictionary<Guid, Batch> batches, List<StockMovement> movements, HashSet<Guid> parents,
            HashSet<Guid> skipped, long nextSequence)
        {
            _unit = unit;
            Settings = settings;
            _products = products;
            _units = units;
            _conversions = conversions;
            _batches = batches;
            _movements = movements;
            _parents = parents;
            _skipped = skipped;
            _nextSequence = nextSequence;
        }

        public InventorySettings Settings { get; }

        public static async Task<PostingContext> LoadAsync(IUnitOfWork unit, IEnumerable<Guid> productIds, CancellationToken cancellationToken)
        {
            var ids = productIds.Distinct().ToList();
            var settings = await InventoryCatalogueService.SettingsAsync(unit, cancellationToken);
            var products = (await unit.ListAsync<Product>(p => ids.Contains(p.Id), cancellationToken)).ToDictionary(p => p.Id);
            var unitRows = (await unit.ListAsync<Unit>(cancellationToken: cancellationToken)).ToDictionary(u => u.Id);
            var conversions = await unit.ListAsync<UnitConversion>(c => ids.Contains(c.ProductId), cancellationToken);
            var batches = (await unit.ListAsync<Batch>(b => ids.Contains(b.ProductId), cancellationToken)).ToDictionary(b => b.Id);
            var movements = await unit.ListAsync<StockMovement>(m => ids.Contains(m.ProductId), cancellationToken);
            var parents = (await unit.ListAsync<Product>(p => p.ParentProductId != null && ids.Contains(p.ParentProductId!.Value), cancellationToken))
                .Select(p => p.ParentProductId!.Value).ToHashSet();
            var reversals = await unit.ListAsync<StockDocument>(d => d.ReversesDocumentId != null, cancellationToken);
            var skipped = reversals.Select(d => d.Id).Concat(reversals.Select(d => d.ReversesDocumentId!.Value)).ToHashSet();
            var last = (await unit.ListAsync<StockMovement, long>(null, m => m.Sequence, descending: true, 0, 1, cancellationToken)).FirstOrDefault();
            return new PostingContext(unit, settings, products, unitRows, conversions, batches, movements, parents, skipped, (last?.Sequence ?? 0) + 1);
        }

        public Product Product(Guid id, int lineNumber)
        {
            if (!_products.TryGetValue(id, out var product)) throw new WorkspaceValidationException($"Line {lineNumber}: choose a product.");
            if (product.IsArchived) throw new WorkspaceValidationException($"Line {lineNumber}: {product.Sku} is archived.");
            if (_parents.Contains(product.Id)) throw new WorkspaceValidationException($"Line {lineNumber}: {product.Sku} has variants. Choose a variant.");
            return product;
        }

        public Batch Batch(Guid id) => _batches[id];

        public bool HasLiveMovementsAfter(IReadOnlyCollection<Guid> productIds, long sequence) =>
            _movements.Any(m => productIds.Contains(m.ProductId) && m.Sequence > sequence && !_skipped.Contains(m.DocumentId));

        /// <summary>Stock dates follow posting order per product, so costing order and date order always agree.</summary>
        public void EnsureDate(Product product, DateOnly date)
        {
            var latest = _movements.Where(m => m.ProductId == product.Id).Select(m => (DateOnly?)m.Date).Max();
            if (latest is { } last && date < last)
                throw new WorkspaceValidationException($"{product.Sku} has stock movements up to {last:dd-MM-yyyy}. Post on or after that date.");
        }

        public decimal BaseQuantity(Product product, StockLineInput line, int lineNumber)
        {
            var baseUnit = _units[product.UnitId];
            if (line.UnitId is null || line.UnitId == product.UnitId) return Quantity.EnsureValid(line.Quantity, baseUnit.DecimalPlaces, $"Line {lineNumber} quantity");
            var conversion = _conversions.FirstOrDefault(c => c.ProductId == product.Id && c.UnitId == line.UnitId)
                ?? throw new WorkspaceValidationException($"Line {lineNumber}: {product.Sku} has no conversion for that unit.");
            Quantity.EnsureValid(line.Quantity, 3, $"Line {lineNumber} quantity");
            var converted = line.Quantity * conversion.Factor;
            if (decimal.Round(converted, baseUnit.DecimalPlaces) != converted)
                throw new WorkspaceValidationException($"Line {lineNumber}: {line.Quantity} converts to {converted:0.######} {baseUnit.Code}, which is not a valid {baseUnit.Code} quantity.");
            return Quantity.EnsureValid(converted, baseUnit.DecimalPlaces, $"Line {lineNumber} quantity");
        }

        public decimal CountedBaseQuantity(Product product, StockLineInput line, int lineNumber) =>
            line.Quantity == 0 ? 0 : BaseQuantity(product, line, lineNumber);

        public async Task<Batch?> ResolveBatchAsync(IUnitOfWork unit, Guid companyId, Product product, StockLineInput line, StockDocumentType type,
            int lineNumber, CancellationToken cancellationToken)
        {
            if (product.Tracking != TrackingMode.Batch)
            {
                if (line.BatchId is not null || !string.IsNullOrWhiteSpace(line.BatchNumber))
                    throw new WorkspaceValidationException($"Line {lineNumber}: {product.Sku} is not batch-tracked.");
                return null;
            }

            if (line.BatchId is { } id)
            {
                if (!_batches.TryGetValue(id, out var existing) || existing.ProductId != product.Id)
                    throw new WorkspaceValidationException($"Line {lineNumber}: that batch does not belong to {product.Sku}.");
                return existing;
            }

            var number = (line.BatchNumber ?? string.Empty).Trim().ToUpperInvariant();
            if (number.Length == 0) throw new WorkspaceValidationException($"Line {lineNumber}: {product.Sku} is batch-tracked; enter the batch number.");
            var found = _batches.Values.FirstOrDefault(b => b.ProductId == product.Id && b.Number == number);
            if (found is not null) return found;
            if (type is not (StockDocumentType.Opening or StockDocumentType.Receipt or StockDocumentType.AdjustmentIn or StockDocumentType.PhysicalCount or StockDocumentType.Purchase))
                throw new WorkspaceValidationException($"Line {lineNumber}: batch {number} of {product.Sku} does not exist.");
            if (product.TracksExpiry && line.ExpiresOn is null) throw new WorkspaceValidationException($"Line {lineNumber}: enter the expiry date of batch {number}.");
            var batch = new Batch(Guid.NewGuid(), companyId, product.Id, number, line.ManufacturedOn, line.ExpiresOn);
            unit.Add(batch);
            _batches[batch.Id] = batch;
            await Task.CompletedTask;
            return batch;
        }

        public string? Serial(Product product, StockLineInput line, decimal quantity, int lineNumber)
        {
            var serial = string.IsNullOrWhiteSpace(line.SerialNumber) ? null : line.SerialNumber.Trim().ToUpperInvariant();
            if (product.Tracking != TrackingMode.Serial)
            {
                if (serial is not null) throw new WorkspaceValidationException($"Line {lineNumber}: {product.Sku} is not serial-tracked.");
                return null;
            }

            if (serial is null || serial.Length > 60) throw new WorkspaceValidationException($"Line {lineNumber}: enter the serial number (up to 60 characters).");
            if (quantity is not (0 or 1)) throw new WorkspaceValidationException($"Line {lineNumber}: serial-tracked items are entered one per line, quantity 1.");
            return serial;
        }

        public void EnsureSerialNotInStock(Product product, string? serial, int lineNumber)
        {
            if (serial is null) return;
            var inStock = _movements.Where(m => m.ProductId == product.Id && m.SerialNumber == serial).Sum(m => m.Quantity);
            if (inStock > 0) throw new WorkspaceValidationException($"Line {lineNumber}: serial {serial} of {product.Sku} is already in stock.");
        }

        public decimal Balance(Guid productId, Guid warehouseId, StockState state, Guid? batchId, string? serial) =>
            _movements.Where(m => m.ProductId == productId && m.WarehouseId == warehouseId && m.State == state && m.BatchId == batchId
                && (serial == null || m.SerialNumber == serial)).Sum(m => m.Quantity);

        /// <summary>Blocks negative stock unless the company allows it. Serial and batch stock can never go negative.</summary>
        public void EnsureAvailable(Product product, Guid warehouseId, StockState state, Batch? batch, string? serial, decimal quantity, int lineNumber)
        {
            var balance = Balance(product.Id, warehouseId, state, batch?.Id, serial);
            if (balance >= quantity) return;
            if (Settings.AllowNegativeStock && serial is null && batch is null) return;
            var where = state == StockState.Good ? string.Empty : $" {state.ToString().ToLowerInvariant()}";
            var what = serial is not null ? $"serial {serial}" : batch is not null ? $"batch {batch.Number}" : product.Sku;
            throw new WorkspaceValidationException($"Line {lineNumber}: only {balance:0.###}{where} of {what} is in this warehouse; {quantity:0.###} is needed.");
        }

        public CostPool Pool(Product product)
        {
            if (!_pools.TryGetValue(product.Id, out var pool))
            {
                pool = CostPool.Replay(Settings.CostingMethod, _movements.Where(m => m.ProductId == product.Id), _skipped, product.PurchasePrice);
                _pools[product.Id] = pool;
            }

            return pool;
        }

        public void Add(StockDocument document, int lineNumber, Product product, Guid warehouseId, StockState state, Batch? batch, string? serial,
            DateOnly date, decimal quantity, decimal value, bool isInternal, StockLineInput line)
        {
            var movement = new StockMovement(Guid.NewGuid(), document.CompanyId, document.Id, lineNumber, _nextSequence++, product.Id, warehouseId, state,
                batch?.Id, serial, date, quantity, value, isInternal, line.UnitId, line.UnitId is null ? null : line.Quantity);
            _movements.Add(movement);
            _unit.Add(movement);
        }

        public void AddReversal(StockDocument reversal, StockMovement original)
        {
            var movement = new StockMovement(Guid.NewGuid(), reversal.CompanyId, reversal.Id, original.LineNumber, _nextSequence++, original.ProductId,
                original.WarehouseId, original.State, original.BatchId, original.SerialNumber, reversal.Date, -original.Quantity, -original.Value,
                original.IsInternal, null, null);
            _movements.Add(movement);
            _unit.Add(movement);
        }
    }

    /// <summary>Names for read-only lists.</summary>
    private sealed class Lookup
    {
        public required Dictionary<Guid, Product> Products { get; init; }

        public required Dictionary<Guid, Batch> Batches { get; init; }

        public required Dictionary<Guid, string> Warehouses { get; init; }

        public required Dictionary<Guid, string> Units { get; init; }

        public string UnitCode(Product product) => Units.GetValueOrDefault(product.UnitId, "?");

        public static async Task<Lookup> LoadAsync(IUnitOfWork unit, CancellationToken cancellationToken) => new()
        {
            Products = (await unit.ListAsync<Product>(cancellationToken: cancellationToken)).ToDictionary(p => p.Id),
            Batches = (await unit.ListAsync<Batch>(cancellationToken: cancellationToken)).ToDictionary(b => b.Id),
            Warehouses = (await unit.ListAsync<Domain.Companies.Warehouse>(cancellationToken: cancellationToken)).ToDictionary(w => w.Id, w => w.Name),
            Units = (await unit.ListAsync<Unit>(cancellationToken: cancellationToken)).ToDictionary(u => u.Id, u => u.Code),
        };
    }
}
