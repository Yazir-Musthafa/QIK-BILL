using Nexora.Domain.Accounting;
using Nexora.Domain.Common;
using Nexora.Domain.Inventory;

namespace Nexora.Domain.Billing;

public enum BillingDocumentKind
{
    /// <summary>Tax invoice to a customer, on credit or paid in cash.</summary>
    SalesInvoice,

    /// <summary>Credit note for goods a customer returned against an invoice.</summary>
    SalesReturn,

    /// <summary>Supplier's invoice for goods bought, recorded with our own number.</summary>
    PurchaseBill,

    /// <summary>Debit note for goods returned to a supplier against a bill.</summary>
    PurchaseReturn,
}

public enum BillingStatus
{
    Draft,
    Posted,
}

/// <summary>Credit: the party ledger is debited or credited. Cash: a cash or bank account is, and nothing stays outstanding.</summary>
public enum SettlementMode
{
    Credit,
    Cash,
}

/// <summary>Numbering keys of sales and purchase documents, seeded into every company by the billing seed.</summary>
public static class BillingDocumentTypes
{
    public const string CreditNote = "sales.credit-note";
    public const string PurchaseBill = "purchase.bill";
    public const string DebitNote = "purchase.debit-note";
    public const string CustomerReceipt = "sales.receipt";
    public const string CustomerRefund = "sales.refund";
    public const string SupplierPayment = "purchase.payment";
    public const string SupplierRefund = "purchase.refund";

    /// <summary>Sales invoices use the existing tax invoice numbering (invoice.tax). Prefixes differ from stock documents.</summary>
    public static IReadOnlyList<Numbering.DocumentTypeInfo> All { get; } =
    [
        new(CreditNote, "Credit note (sales return)", "CRN/{FY}/", 4),
        new(PurchaseBill, "Purchase bill", "PB/{FY}/", 4),
        new(DebitNote, "Debit note (purchase return)", "DBN/{FY}/", 4),
        new(CustomerReceipt, "Customer receipt", "REC/{FY}/", 4),
        new(CustomerRefund, "Customer refund", "RFD/{FY}/", 4),
        new(SupplierPayment, "Supplier payment", "PAY/{FY}/", 4),
        new(SupplierRefund, "Supplier refund", "SRF/{FY}/", 4),
    ];
}

public static class BillingDocumentKindExtensions
{
    public static bool IsSales(this BillingDocumentKind kind) => kind is BillingDocumentKind.SalesInvoice or BillingDocumentKind.SalesReturn;

    public static bool IsReturn(this BillingDocumentKind kind) => kind is BillingDocumentKind.SalesReturn or BillingDocumentKind.PurchaseReturn;

    /// <summary>Invoices and bills increase what the party owes us or what we owe them; returns reduce it.</summary>
    public static bool IsCharge(this BillingDocumentKind kind) => kind is BillingDocumentKind.SalesInvoice or BillingDocumentKind.PurchaseBill;

    public static PartyKind PartyKind(this BillingDocumentKind kind) => kind.IsSales() ? Billing.PartyKind.Customer : Billing.PartyKind.Supplier;

    /// <summary>The document a return is made against.</summary>
    public static BillingDocumentKind OriginalKind(this BillingDocumentKind kind) => kind switch
    {
        BillingDocumentKind.SalesReturn => BillingDocumentKind.SalesInvoice,
        BillingDocumentKind.PurchaseReturn => BillingDocumentKind.PurchaseBill,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static string Label(this BillingDocumentKind kind) => kind switch
    {
        BillingDocumentKind.SalesInvoice => "Sales invoice",
        BillingDocumentKind.SalesReturn => "Credit note",
        BillingDocumentKind.PurchaseBill => "Purchase bill",
        BillingDocumentKind.PurchaseReturn => "Debit note",
        _ => kind.ToString(),
    };

    public static string NumberingKey(this BillingDocumentKind kind) => kind switch
    {
        BillingDocumentKind.SalesInvoice => Numbering.DocumentTypes.TaxInvoice,
        BillingDocumentKind.SalesReturn => BillingDocumentTypes.CreditNote,
        BillingDocumentKind.PurchaseBill => BillingDocumentTypes.PurchaseBill,
        BillingDocumentKind.PurchaseReturn => BillingDocumentTypes.DebitNote,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static StockDocumentType StockType(this BillingDocumentKind kind) => kind switch
    {
        BillingDocumentKind.SalesInvoice => StockDocumentType.Sale,
        BillingDocumentKind.SalesReturn => StockDocumentType.SalesReturn,
        BillingDocumentKind.PurchaseBill => StockDocumentType.Purchase,
        BillingDocumentKind.PurchaseReturn => StockDocumentType.PurchaseReturn,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static VoucherType VoucherType(this BillingDocumentKind kind) => kind switch
    {
        BillingDocumentKind.SalesInvoice => Accounting.VoucherType.Sales,
        BillingDocumentKind.SalesReturn => Accounting.VoucherType.CreditNote,
        BillingDocumentKind.PurchaseBill => Accounting.VoucherType.Purchase,
        BillingDocumentKind.PurchaseReturn => Accounting.VoucherType.DebitNote,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

/// <summary>
/// A sales invoice, credit note, purchase bill or debit note. Drafts can be edited and deleted; posting recalculates
/// everything, takes the number, moves stock and posts one accounting voucher in a single transaction. A posted
/// document never changes (the data context and database triggers refuse it): it is cancelled with a
/// <see cref="BillingCancellation"/>, or corrected with a return.
/// </summary>
public sealed class BillingDocument : ICompanyOwned, IAudited
{
    private BillingDocument()
    {
        PartyName = PartyGstin = PlaceOfSupply = Reference = Narration = string.Empty;
    }

    public BillingDocument(Guid id, Guid companyId, BillingDocumentKind kind, DateOnly date, DateTime createdUtc, Guid createdBy) : this()
    {
        Id = id;
        CompanyId = companyId;
        Kind = kind;
        Status = BillingStatus.Draft;
        Date = date;
        RoundToRupee = true;
        CreatedUtc = DateTime.SpecifyKind(createdUtc, DateTimeKind.Utc);
        CreatedBy = createdBy;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public BillingDocumentKind Kind { get; private set; }

    public BillingStatus Status { get; private set; }

    /// <summary>Null while a draft.</summary>
    public string? Number { get; private set; }

    public DateOnly Date { get; private set; }

    /// <summary>Null only for a cash sale to a walk-in customer.</summary>
    public Guid? PartyId { get; private set; }

    /// <summary>Name and GSTIN as they were when posted, so the document does not change if the party is edited.</summary>
    public string PartyName { get; private set; }

    public string PartyGstin { get; private set; }

    /// <summary>State code of the place of supply.</summary>
    public string PlaceOfSupply { get; private set; }

    public bool IsInterState { get; private set; }

    public SettlementMode Mode { get; private set; }

    /// <summary>Cash or bank account for <see cref="SettlementMode.Cash"/>.</summary>
    public Guid? CashAccountId { get; private set; }

    public Guid WarehouseId { get; private set; }

    /// <summary>For a return, the invoice or bill it is made against.</summary>
    public Guid? OriginalDocumentId { get; private set; }

    /// <summary>The supplier's bill number for purchases; the customer's order reference for sales.</summary>
    public string Reference { get; private set; }

    public string Narration { get; private set; }

    public bool PricesIncludeTax { get; private set; }

    public bool RoundToRupee { get; private set; }

    public decimal GrossAmount { get; private set; }

    public decimal DiscountAmount { get; private set; }

    public decimal TaxableAmount { get; private set; }

    public decimal Cgst { get; private set; }

    public decimal Sgst { get; private set; }

    public decimal Igst { get; private set; }

    public decimal RoundOff { get; private set; }

    public decimal Total { get; private set; }

    /// <summary>Stock value of the goods that left or came back (sales and sales returns), or left (purchase returns).</summary>
    public decimal CostOfGoods { get; private set; }

    /// <summary>Null only when every amount was zero, so there was nothing to post.</summary>
    public Guid? VoucherId { get; private set; }

    public Guid? StockDocumentId { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTime? PostedUtc { get; private set; }

    public Guid? PostedBy { get; private set; }

    public decimal Tax => Cgst + Sgst + Igst;

    public void UpdateDraft(DateOnly date, Guid? partyId, string partyName, string partyGstin, string placeOfSupply, bool interState,
        SettlementMode mode, Guid? cashAccountId, Guid warehouseId, Guid? originalDocumentId, string reference, string narration,
        bool pricesIncludeTax, bool roundToRupee)
    {
        EnsureDraft();
        var name = (partyName ?? string.Empty).Trim();
        if (name.Length > 120) throw new DomainException("The party name can have up to 120 characters.");
        var cleanReference = (reference ?? string.Empty).Trim();
        if (cleanReference.Length > 40) throw new DomainException("The reference can have up to 40 characters.");
        var cleanNarration = (narration ?? string.Empty).Trim();
        if (cleanNarration.Length > 300) throw new DomainException("Narration must be 300 characters or fewer.");
        if (Kind.IsReturn() && originalDocumentId is null) throw new DomainException($"A {Kind.Label().ToLowerInvariant()} must be made against a posted document.");
        if (!Kind.IsReturn() && originalDocumentId is not null) throw new DomainException("Only returns refer to an original document.");
        if (mode == SettlementMode.Cash && cashAccountId is null) throw new DomainException("Choose the cash or bank account.");
        if (mode == SettlementMode.Credit && partyId is null) throw new DomainException("Choose the party for a credit document.");
        Date = date;
        PartyId = partyId;
        PartyName = name;
        PartyGstin = (partyGstin ?? string.Empty).Trim().ToUpperInvariant();
        PlaceOfSupply = (placeOfSupply ?? string.Empty).Trim();
        IsInterState = interState;
        Mode = mode;
        CashAccountId = mode == SettlementMode.Cash ? cashAccountId : null;
        WarehouseId = warehouseId;
        OriginalDocumentId = originalDocumentId;
        Reference = cleanReference;
        Narration = cleanNarration;
        PricesIncludeTax = pricesIncludeTax;
        RoundToRupee = roundToRupee;
    }

    public void SetTotals(DocumentTotals totals)
    {
        EnsureDraft();
        if (totals.Total < 0) throw new DomainException("The document total cannot be negative.");
        if (totals.Total > Money.MaxAmount) throw new DomainException("The document total is too large.");
        GrossAmount = totals.Gross;
        DiscountAmount = totals.Discount;
        TaxableAmount = totals.Taxable;
        Cgst = totals.Cgst;
        Sgst = totals.Sgst;
        Igst = totals.Igst;
        RoundOff = totals.RoundOff;
        Total = totals.Total;
    }

    /// <summary>Called once, inside the posting transaction.</summary>
    public void MarkPosted(string number, Guid? voucherId, Guid stockDocumentId, decimal costOfGoods, DateTime postedUtc, Guid postedBy)
    {
        EnsureDraft();
        if (string.IsNullOrWhiteSpace(number)) throw new DomainException("A posted document needs a number.");
        Money.EnsureValid(costOfGoods, "Cost of goods");
        Status = BillingStatus.Posted;
        Number = number;
        VoucherId = voucherId;
        StockDocumentId = stockDocumentId;
        CostOfGoods = costOfGoods;
        PostedUtc = DateTime.SpecifyKind(postedUtc, DateTimeKind.Utc);
        PostedBy = postedBy;
    }

    public void EnsureDraft()
    {
        if (Status != BillingStatus.Draft) throw new DomainException($"{Kind.Label()} {Number} is posted and cannot be changed. Cancel it or make a return instead.");
    }
}

/// <summary>
/// One line of a billing document. Amounts are calculated by <see cref="BillingCalculator"/>; the tax rate used is
/// stored on the line, so a posted document keeps the rate that applied when it was posted. Locked when posted.
/// </summary>
public sealed class BillingLine : ICompanyOwned
{
    private BillingLine()
    {
        Description = HsnCode = BatchNumber = string.Empty;
    }

    public BillingLine(Guid id, Guid companyId, Guid documentId, int lineNumber, Guid productId, string description, string hsnCode,
        Guid unitId, decimal quantity, decimal baseQuantity, decimal rate, decimal discountPercent, Guid? taxCategoryId, decimal taxRatePercent,
        LineAmounts amounts, Guid? batchId, string batchNumber, DateOnly? expiresOn, string? serialNumber, Guid? originalLineId) : this()
    {
        if (lineNumber < 1) throw new DomainException("Line numbers start at 1.");
        if (quantity <= 0 || baseQuantity <= 0) throw new DomainException("Quantity must be more than zero.");
        var text = (description ?? string.Empty).Trim();
        if (text.Length is 0 or > 200) throw new DomainException("Each line needs a description of up to 200 characters.");
        Id = id;
        CompanyId = companyId;
        DocumentId = documentId;
        LineNumber = lineNumber;
        ProductId = productId;
        Description = text;
        HsnCode = (hsnCode ?? string.Empty).Trim();
        UnitId = unitId;
        Quantity = quantity;
        BaseQuantity = baseQuantity;
        Rate = rate;
        DiscountPercent = discountPercent;
        TaxCategoryId = taxCategoryId;
        TaxRatePercent = taxRatePercent;
        GrossAmount = amounts.Gross;
        DiscountAmount = amounts.Discount;
        TaxableAmount = amounts.Taxable;
        Cgst = amounts.Cgst;
        Sgst = amounts.Sgst;
        Igst = amounts.Igst;
        BatchId = batchId;
        BatchNumber = (batchNumber ?? string.Empty).Trim().ToUpperInvariant();
        ExpiresOn = expiresOn;
        SerialNumber = string.IsNullOrWhiteSpace(serialNumber) ? null : serialNumber.Trim().ToUpperInvariant();
        OriginalLineId = originalLineId;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid DocumentId { get; private set; }

    public int LineNumber { get; private set; }

    public Guid ProductId { get; private set; }

    public string Description { get; private set; }

    public string HsnCode { get; private set; }

    /// <summary>The unit the quantity and rate are entered in.</summary>
    public Guid UnitId { get; private set; }

    public decimal Quantity { get; private set; }

    /// <summary>Quantity in the product's base unit, as it moves in stock.</summary>
    public decimal BaseQuantity { get; private set; }

    /// <summary>Price per <see cref="UnitId"/>, inclusive or exclusive of tax as the document says.</summary>
    public decimal Rate { get; private set; }

    public decimal DiscountPercent { get; private set; }

    public decimal GrossAmount { get; private set; }

    public decimal DiscountAmount { get; private set; }

    public decimal TaxableAmount { get; private set; }

    public Guid? TaxCategoryId { get; private set; }

    public decimal TaxRatePercent { get; private set; }

    public decimal Cgst { get; private set; }

    public decimal Sgst { get; private set; }

    public decimal Igst { get; private set; }

    public Guid? BatchId { get; private set; }

    /// <summary>A new batch to create on a purchase bill (when <see cref="BatchId"/> is null).</summary>
    public string BatchNumber { get; private set; }

    public DateOnly? ExpiresOn { get; private set; }

    public string? SerialNumber { get; private set; }

    /// <summary>For a return line, the invoice or bill line being returned.</summary>
    public Guid? OriginalLineId { get; private set; }

    /// <summary>Stock value of the line (cost of goods sold or returned), set when posted.</summary>
    public decimal CostValue { get; private set; }

    public bool IsLocked { get; private set; }

    public LineAmounts Amounts => new(GrossAmount, DiscountAmount, TaxableAmount, Cgst, Sgst, Igst);

    public decimal Total => TaxableAmount + Cgst + Sgst + Igst;

    public void Lock(decimal costValue, Guid? batchId)
    {
        if (IsLocked) throw new InvalidOperationException("The line is already locked.");
        Money.EnsureValid(costValue, "Cost");
        CostValue = costValue;
        BatchId ??= batchId;
        IsLocked = true;
    }
}

/// <summary>Cancels a posted document: reverses its voucher and stock. Append-only; at most one per document.</summary>
public sealed class BillingCancellation : ICompanyOwned
{
    private BillingCancellation()
    {
        Reason = string.Empty;
    }

    public BillingCancellation(Guid id, Guid companyId, Guid documentId, DateOnly date, string reason, Guid? reversalVoucherId,
        Guid? stockReversalId, DateTime cancelledUtc, Guid cancelledBy) : this()
    {
        var text = (reason ?? string.Empty).Trim();
        if (text.Length is 0 or > 300) throw new DomainException("Enter the reason for cancelling (up to 300 characters).");
        Id = id;
        CompanyId = companyId;
        DocumentId = documentId;
        Date = date;
        Reason = text;
        ReversalVoucherId = reversalVoucherId;
        StockReversalId = stockReversalId;
        CancelledUtc = DateTime.SpecifyKind(cancelledUtc, DateTimeKind.Utc);
        CancelledBy = cancelledBy;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid DocumentId { get; private set; }

    public DateOnly Date { get; private set; }

    public string Reason { get; private set; }

    /// <summary>Null when the document had no accounting effect (every amount zero).</summary>
    public Guid? ReversalVoucherId { get; private set; }

    public Guid? StockReversalId { get; private set; }

    public DateTime CancelledUtc { get; private set; }

    public Guid CancelledBy { get; private set; }
}
