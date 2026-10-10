using Nexora.Domain.Accounting;
using Nexora.Domain.Common;

namespace Nexora.Domain.Billing;

public enum SettlementKind
{
    /// <summary>Money received from a customer (against invoices, or as an advance).</summary>
    CustomerReceipt,

    /// <summary>Money paid back to a customer (against credit notes or advances).</summary>
    CustomerRefund,

    /// <summary>Money paid to a supplier (against bills, or as an advance).</summary>
    SupplierPayment,

    /// <summary>Money a supplier paid back (against debit notes or advances).</summary>
    SupplierRefund,
}

public enum PaymentMethod
{
    Cash,
    Upi,
    Card,
    Cheque,
    BankTransfer,
    Other,
}

public static class SettlementKindExtensions
{
    public static PartyKind PartyKind(this SettlementKind kind) =>
        kind is SettlementKind.CustomerReceipt or SettlementKind.CustomerRefund ? Billing.PartyKind.Customer : Billing.PartyKind.Supplier;

    /// <summary>Refunds increase the party's normal balance again (they are charges); receipts and payments reduce it.</summary>
    public static bool IsCharge(this SettlementKind kind) => kind is SettlementKind.CustomerRefund or SettlementKind.SupplierRefund;

    /// <summary>True when money comes in (the cash or bank account is debited).</summary>
    public static bool IsMoneyIn(this SettlementKind kind) => kind is SettlementKind.CustomerReceipt or SettlementKind.SupplierRefund;

    public static string Label(this SettlementKind kind) => kind switch
    {
        SettlementKind.CustomerReceipt => "Customer receipt",
        SettlementKind.CustomerRefund => "Customer refund",
        SettlementKind.SupplierPayment => "Supplier payment",
        SettlementKind.SupplierRefund => "Supplier refund",
        _ => kind.ToString(),
    };

    public static string NumberingKey(this SettlementKind kind) => kind switch
    {
        SettlementKind.CustomerReceipt => BillingDocumentTypes.CustomerReceipt,
        SettlementKind.CustomerRefund => BillingDocumentTypes.CustomerRefund,
        SettlementKind.SupplierPayment => BillingDocumentTypes.SupplierPayment,
        SettlementKind.SupplierRefund => BillingDocumentTypes.SupplierRefund,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

/// <summary>
/// A receipt from or payment to a party through a cash or bank account. Posted when saved, with its voucher; never
/// changed afterwards. Amounts not allocated to documents remain as an advance (unapplied) on the party.
/// </summary>
public sealed class Settlement : ICompanyOwned, IAudited
{
    private Settlement()
    {
        Number = Reference = Narration = string.Empty;
    }

    public Settlement(Guid id, Guid companyId, SettlementKind kind, string number, DateOnly date, Guid partyId, Guid accountId, decimal amount,
        PaymentMethod method, string reference, string narration, Guid voucherId, DateTime postedUtc, Guid postedBy) : this()
    {
        if (amount <= 0) throw new DomainException("Enter an amount more than zero.");
        Money.EnsureValid(amount);
        var cleanReference = (reference ?? string.Empty).Trim();
        if (cleanReference.Length > 60) throw new DomainException("The reference can have up to 60 characters.");
        var cleanNarration = (narration ?? string.Empty).Trim();
        if (cleanNarration.Length > 300) throw new DomainException("Narration must be 300 characters or fewer.");
        Id = id;
        CompanyId = companyId;
        Kind = kind;
        Number = number;
        Date = date;
        PartyId = partyId;
        AccountId = accountId;
        Amount = amount;
        Method = method;
        Reference = cleanReference;
        Narration = cleanNarration;
        VoucherId = voucherId;
        PostedUtc = DateTime.SpecifyKind(postedUtc, DateTimeKind.Utc);
        PostedBy = postedBy;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public SettlementKind Kind { get; private set; }

    public string Number { get; private set; }

    public DateOnly Date { get; private set; }

    public Guid PartyId { get; private set; }

    /// <summary>The cash or bank account.</summary>
    public Guid AccountId { get; private set; }

    public decimal Amount { get; private set; }

    public PaymentMethod Method { get; private set; }

    /// <summary>Cheque number, UPI or bank transaction reference.</summary>
    public string Reference { get; private set; }

    public string Narration { get; private set; }

    public Guid VoucherId { get; private set; }

    public DateTime PostedUtc { get; private set; }

    public Guid PostedBy { get; private set; }
}

/// <summary>Reverses a settlement and its voucher. Its allocations stop counting. Append-only; at most one per settlement.</summary>
public sealed class SettlementReversal : ICompanyOwned
{
    private SettlementReversal()
    {
        Reason = string.Empty;
    }

    public SettlementReversal(Guid id, Guid companyId, Guid settlementId, DateOnly date, string reason, Guid reversalVoucherId,
        DateTime reversedUtc, Guid reversedBy) : this()
    {
        var text = (reason ?? string.Empty).Trim();
        if (text.Length is 0 or > 300) throw new DomainException("Enter the reason for reversing (up to 300 characters).");
        Id = id;
        CompanyId = companyId;
        SettlementId = settlementId;
        Date = date;
        Reason = text;
        ReversalVoucherId = reversalVoucherId;
        ReversedUtc = DateTime.SpecifyKind(reversedUtc, DateTimeKind.Utc);
        ReversedBy = reversedBy;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid SettlementId { get; private set; }

    public DateOnly Date { get; private set; }

    public string Reason { get; private set; }

    public Guid ReversalVoucherId { get; private set; }

    public DateTime ReversedUtc { get; private set; }

    public Guid ReversedBy { get; private set; }
}

/// <summary>Which table an open item comes from.</summary>
public enum OpenItemSource
{
    Document,
    Settlement,
}

/// <summary>
/// Settles part or all of a charge (invoice, bill or refund) with a credit (receipt, payment, return) of the same party.
/// Append-only. An allocation stops counting when either side is cancelled or reversed, so balances are always derived.
/// </summary>
public sealed class Allocation : ICompanyOwned
{
    private Allocation()
    {
    }

    public Allocation(Guid id, Guid companyId, Guid partyId, OpenItemSource chargeSource, Guid chargeId, OpenItemSource creditSource, Guid creditId,
        decimal amount, DateOnly date, DateTime createdUtc, Guid createdBy)
    {
        if (amount <= 0) throw new DomainException("An allocation must be more than zero.");
        Money.EnsureValid(amount);
        if (chargeId == creditId) throw new DomainException("A document cannot settle itself.");
        Id = id;
        CompanyId = companyId;
        PartyId = partyId;
        ChargeSource = chargeSource;
        ChargeId = chargeId;
        CreditSource = creditSource;
        CreditId = creditId;
        Amount = amount;
        Date = date;
        CreatedUtc = DateTime.SpecifyKind(createdUtc, DateTimeKind.Utc);
        CreatedBy = createdBy;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid PartyId { get; private set; }

    public OpenItemSource ChargeSource { get; private set; }

    public Guid ChargeId { get; private set; }

    public OpenItemSource CreditSource { get; private set; }

    public Guid CreditId { get; private set; }

    public decimal Amount { get; private set; }

    public DateOnly Date { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    public Guid CreatedBy { get; private set; }
}

/// <summary>Billing preferences of a company. Also the seed marker for billing numbering on upgrade.</summary>
public sealed class BillingSettings : ICompanyOwned, IAudited
{
    private BillingSettings()
    {
    }

    public BillingSettings(Guid id, Guid companyId)
    {
        Id = id;
        CompanyId = companyId;
        RoundToRupee = true;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>New documents round their total to whole rupees (the difference posts to Round Off).</summary>
    public bool RoundToRupee { get; private set; }

    /// <summary>New sales documents use tax-inclusive rates.</summary>
    public bool PricesIncludeTax { get; private set; }

    public void Update(bool roundToRupee, bool pricesIncludeTax)
    {
        RoundToRupee = roundToRupee;
        PricesIncludeTax = pricesIncludeTax;
    }
}
