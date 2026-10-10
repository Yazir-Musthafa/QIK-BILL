using Nexora.Domain.Accounting;
using Nexora.Domain.Common;

namespace Nexora.Domain.Inventory;

public enum StockDocumentType
{
    Opening,
    Receipt,
    Issue,
    AdjustmentIn,
    AdjustmentOut,
    Transfer,
    PhysicalCount,
    StateChange,
    Reversal,
}

/// <summary>Condition of stock. Only Good stock is available to issue unless another state is chosen explicitly.</summary>
public enum StockState
{
    Good,
    Damaged,
    Expired,
    Returned,
    Obsolete,
}

public static class StockDocumentTypeExtensions
{
    public static string Label(this StockDocumentType type) => type switch
    {
        StockDocumentType.Opening => "Opening stock",
        StockDocumentType.AdjustmentIn => "Adjustment in",
        StockDocumentType.AdjustmentOut => "Adjustment out",
        StockDocumentType.PhysicalCount => "Physical count",
        StockDocumentType.StateChange => "State change",
        _ => type.ToString(),
    };

    public static string NumberingKey(this StockDocumentType type) => type switch
    {
        StockDocumentType.Opening => InventoryDocumentTypes.Opening,
        StockDocumentType.Receipt => InventoryDocumentTypes.Receipt,
        StockDocumentType.Issue => InventoryDocumentTypes.Issue,
        StockDocumentType.AdjustmentIn or StockDocumentType.AdjustmentOut => InventoryDocumentTypes.Adjustment,
        StockDocumentType.Transfer => InventoryDocumentTypes.Transfer,
        StockDocumentType.PhysicalCount => InventoryDocumentTypes.Count,
        StockDocumentType.StateChange => InventoryDocumentTypes.StateChange,
        StockDocumentType.Reversal => InventoryDocumentTypes.Reversal,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}

/// <summary>Numbering keys for stock documents, seeded into every company by the inventory seed.</summary>
public static class InventoryDocumentTypes
{
    public const string Opening = "stock.opening";
    public const string Receipt = "stock.receipt";
    public const string Issue = "stock.issue";
    public const string Adjustment = "stock.adjustment";
    public const string Transfer = "stock.transfer";
    public const string Count = "stock.count";
    public const string StateChange = "stock.state";
    public const string Reversal = "stock.reversal";

    public static IReadOnlyList<Numbering.DocumentTypeInfo> All { get; } =
    [
        new(Opening, "Opening stock", "OS/{FY}/", 4),
        new(Receipt, "Stock receipt", "SR/{FY}/", 4),
        new(Issue, "Stock issue", "SI/{FY}/", 4),
        new(Adjustment, "Stock adjustment", "SA/{FY}/", 4),
        new(Transfer, "Stock transfer", "ST/{FY}/", 4),
        new(Count, "Physical count", "PC/{FY}/", 4),
        new(StateChange, "Stock state change", "SS/{FY}/", 4),
        new(Reversal, "Stock reversal", "SX/{FY}/", 4),
    ];
}

/// <summary>
/// A posted stock document: the header of one or more movements. Stock documents are posted when saved and are then
/// immutable; corrections are made with a reversal document.
/// </summary>
public sealed class StockDocument : ICompanyOwned, IAudited
{
    private StockDocument()
    {
        Number = Narration = string.Empty;
    }

    public StockDocument(Guid id, Guid companyId, StockDocumentType type, string number, DateOnly date, string narration,
        Guid warehouseId, Guid? toWarehouseId, Guid? counterAccountId, Guid? reversesDocumentId, DateTime postedUtc, Guid postedBy)
    {
        var text = (narration ?? string.Empty).Trim();
        if (text.Length > 300) throw new DomainException("Narration must be 300 characters or fewer.");
        if (type == StockDocumentType.Transfer && (toWarehouseId is null || toWarehouseId == warehouseId))
            throw new DomainException("Choose a different destination warehouse for a transfer.");
        Id = id;
        CompanyId = companyId;
        Type = type;
        Number = number;
        Date = date;
        Narration = text;
        WarehouseId = warehouseId;
        ToWarehouseId = toWarehouseId;
        CounterAccountId = counterAccountId;
        ReversesDocumentId = reversesDocumentId;
        PostedUtc = DateTime.SpecifyKind(postedUtc, DateTimeKind.Utc);
        PostedBy = postedBy;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public StockDocumentType Type { get; private set; }

    public string Number { get; private set; }

    public DateOnly Date { get; private set; }

    public string Narration { get; private set; }

    public Guid WarehouseId { get; private set; }

    public Guid? ToWarehouseId { get; private set; }

    /// <summary>The other side of the accounting entry, for receipts (credited) and issues (debited).</summary>
    public Guid? CounterAccountId { get; private set; }

    /// <summary>The accounting voucher posted with this document, if it changed stock value.</summary>
    public Guid? VoucherId { get; private set; }

    public Guid? ReversesDocumentId { get; private set; }

    public DateTime PostedUtc { get; private set; }

    public Guid PostedBy { get; private set; }

    /// <summary>Links the accounting voucher. Called once, inside the posting transaction.</summary>
    public void AttachVoucher(Guid voucherId)
    {
        if (VoucherId is not null) throw new InvalidOperationException("A voucher is already attached.");
        VoucherId = voucherId;
    }
}

/// <summary>
/// One line of the stock ledger. Append-only: never updated or deleted (enforced here, in the data context and by
/// database triggers). Every stock quantity and value in the application is a sum of these rows.
/// Quantity is in the product's base unit and signed (+ in, − out); Value is the signed money value at cost.
/// Transfers and state changes are internal: they move quantity between warehouses or states with zero value.
/// </summary>
public sealed class StockMovement : ICompanyOwned
{
    private StockMovement()
    {
    }

    public StockMovement(Guid id, Guid companyId, Guid documentId, int lineNumber, long sequence, Guid productId, Guid warehouseId,
        StockState state, Guid? batchId, string? serialNumber, DateOnly date, decimal quantity, decimal value, bool isInternal,
        Guid? enteredUnitId, decimal? enteredQuantity)
    {
        if (quantity == 0) throw new DomainException("A stock movement must change the quantity.");
        if (decimal.Round(quantity, 3) != quantity) throw new DomainException("Quantities can have at most 3 decimal places.");
        if (!Money.HasValidScale(value)) throw new DomainException("Values can have at most 2 decimal places.");
        if (isInternal && value != 0) throw new DomainException("Transfers and state changes do not change stock value.");
        if (!isInternal && value != 0 && Math.Sign(value) != Math.Sign(quantity)) throw new DomainException("Value and quantity must move in the same direction.");
        Id = id;
        CompanyId = companyId;
        DocumentId = documentId;
        LineNumber = lineNumber;
        Sequence = sequence;
        ProductId = productId;
        WarehouseId = warehouseId;
        State = state;
        BatchId = batchId;
        SerialNumber = string.IsNullOrWhiteSpace(serialNumber) ? null : serialNumber.Trim().ToUpperInvariant();
        Date = date;
        Quantity = quantity;
        Value = value;
        IsInternal = isInternal;
        EnteredUnitId = enteredUnitId;
        EnteredQuantity = enteredQuantity;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid DocumentId { get; private set; }

    public int LineNumber { get; private set; }

    /// <summary>Posting order within the company. Costing replays movements in this order.</summary>
    public long Sequence { get; private set; }

    public Guid ProductId { get; private set; }

    public Guid WarehouseId { get; private set; }

    public StockState State { get; private set; }

    public Guid? BatchId { get; private set; }

    public string? SerialNumber { get; private set; }

    public DateOnly Date { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal Value { get; private set; }

    public bool IsInternal { get; private set; }

    public Guid? EnteredUnitId { get; private set; }

    public decimal? EnteredQuantity { get; private set; }
}

/// <summary>
/// Cost of one product, pooled across all warehouses and states, built by replaying its movements in posting order.
///
/// Weighted average (perpetual): a receipt adds its quantity and value; an issue takes the average value per unit,
/// rounded to the paisa, and the last unit takes whatever value remains so no rounding residue is left behind.
/// When there is no stock (negative stock allowed), issues use the last known unit cost. A later receipt first settles
/// the missing units: their estimated value leaves the pool and the difference to their real cost is returned as a
/// cost adjustment, so stock never keeps value without quantity (see <see cref="Receive"/>).
///
/// FIFO: each receipt becomes a layer; issues consume the oldest layers first, a whole layer taking its exact
/// remaining value. FIFO never goes negative.
///
/// Internal movements (transfers, state changes) do not touch the pool. Movements of a reversed document and its
/// reversal are skipped together, which restores the pool exactly (a reversal must be the product's latest activity).
/// </summary>
public sealed class CostPool
{
    private readonly List<Layer> _layers = [];

    public CostPool(CostingMethod method, decimal fallbackUnitCost = 0)
    {
        Method = method;
        LastUnitCost = fallbackUnitCost;
    }

    public CostingMethod Method { get; }

    public decimal Quantity { get; private set; }

    public decimal Value { get; private set; }

    public decimal LastUnitCost { get; private set; }

    /// <summary>Unit cost used to value incoming stock that has no cost of its own (adjustments in, count gains).</summary>
    public decimal CurrentUnitCost => Quantity > 0 ? Value / Quantity : LastUnitCost;

    public static CostPool Replay(CostingMethod method, IEnumerable<StockMovement> movements, ISet<Guid> skippedDocuments, decimal fallbackUnitCost = 0)
    {
        var pool = new CostPool(method, fallbackUnitCost);
        decimal quantity = 0, value = 0;
        foreach (var movement in movements.OrderBy(m => m.Sequence))
        {
            if (movement.IsInternal || skippedDocuments.Contains(movement.DocumentId)) continue;
            if (movement.Quantity > 0) pool.Apply(movement.Quantity, movement.Value);
            else pool.Issue(-movement.Quantity, allowNegative: true);
            quantity += movement.Quantity;
            value += movement.Value;
        }

        // Totals come from the recorded movements, so the pool always equals the stock ledger exactly.
        pool.Quantity = quantity;
        pool.Value = value;
        return pool;
    }

    /// <summary>
    /// Adds stock that cost <paramref name="value"/> and returns the value that enters the pool (the stock value).
    /// Normally both are equal. When weighted-average stock is negative, the received units first settle the missing
    /// units: those were issued at an estimated cost, so the pool takes back that estimate and values any remaining
    /// units at the receipt's own cost. The difference (<paramref name="value"/> minus the returned value) is a cost
    /// adjustment the caller posts to Inventory Adjustments.
    /// </summary>
    public decimal Receive(decimal quantity, decimal value)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        Money.EnsureValid(value, "Stock value");
        var stockValue = value;
        if (Method == CostingMethod.WeightedAverage && Quantity < 0)
        {
            var settled = Math.Min(quantity, -Quantity);
            var remainder = quantity - settled;
            var remainderValue = remainder == 0 ? 0 : value - Money.Round(value * settled / quantity);
            stockValue = SettledEstimate(settled) + remainderValue;
        }

        Apply(quantity, stockValue);
        return stockValue;
    }

    /// <summary>Adds stock at its recorded stock value (used when replaying the stock ledger).</summary>
    private void Apply(decimal quantity, decimal stockValue)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (Method == CostingMethod.WeightedAverage && Quantity < 0)
        {
            // Only units beyond the shortfall carry a cost of their own; settling alone leaves the last cost unchanged.
            var settled = Math.Min(quantity, -Quantity);
            var remainder = quantity - settled;
            var remainderValue = stockValue - SettledEstimate(settled);
            if (remainder > 0 && remainderValue > 0) LastUnitCost = remainderValue / remainder;
        }
        else if (stockValue > 0) LastUnitCost = stockValue / quantity;

        if (Method == CostingMethod.Fifo) _layers.Add(new Layer(quantity, stockValue));
        Quantity += quantity;
        Value += stockValue;
    }

    /// <summary>The (positive) value at which <paramref name="settled"/> missing units were issued while stock was negative.</summary>
    private decimal SettledEstimate(decimal settled) => settled == -Quantity ? -Value : Money.Round(Value * settled / Quantity);

    /// <summary>Removes <paramref name="quantity"/> and returns its value at cost (positive).</summary>
    public decimal Issue(decimal quantity, bool allowNegative)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (Method == CostingMethod.Fifo) return IssueFifo(quantity);

        decimal value;
        if (quantity == Quantity) value = Value;
        else if (Quantity > 0 && quantity < Quantity) value = Money.Round(quantity * Value / Quantity);
        else if (!allowNegative) throw new DomainException("There is not enough stock to issue this quantity.");
        else if (Quantity > 0) value = Value + Money.Round((quantity - Quantity) * LastUnitCost);
        else value = Money.Round(quantity * LastUnitCost);

        Quantity -= quantity;
        Value -= value;
        return value;
    }

    private decimal IssueFifo(decimal quantity)
    {
        if (quantity > Quantity) throw new DomainException("There is not enough stock to issue this quantity.");
        decimal total = 0;
        var remaining = quantity;
        while (remaining > 0)
        {
            var layer = _layers[0];
            var take = Math.Min(remaining, layer.Quantity);
            var value = take == layer.Quantity ? layer.Value : Money.Round(take * layer.Value / layer.Quantity);
            layer.Quantity -= take;
            layer.Value -= value;
            if (layer.Quantity == 0) _layers.RemoveAt(0);
            total += value;
            remaining -= take;
        }

        Quantity -= quantity;
        Value -= total;
        return total;
    }

    private sealed class Layer(decimal quantity, decimal value)
    {
        public decimal Quantity { get; set; } = quantity;

        public decimal Value { get; set; } = value;
    }
}
