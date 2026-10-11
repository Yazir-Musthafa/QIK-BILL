namespace Nexora.Domain.Numbering;

public sealed record DocumentTypeInfo(string Key, string Name, string DefaultPrefix, int DefaultPadding);

/// <summary>Document types that receive company-specific numbers. Seeded into every new company.</summary>
public static class DocumentTypes
{
    public const string JournalVoucher = "voucher.journal";
    public const string PaymentVoucher = "voucher.payment";
    public const string ReceiptVoucher = "voucher.receipt";
    public const string ContraVoucher = "voucher.contra";
    public const string SalesVoucher = "voucher.sales";
    public const string PurchaseVoucher = "voucher.purchase";
    public const string ExpenseVoucher = "voucher.expense";
    public const string IncomeVoucher = "voucher.income";
    public const string CreditNoteVoucher = "voucher.credit-note";
    public const string DebitNoteVoucher = "voucher.debit-note";
    public const string OpeningVoucher = "voucher.opening";
    public const string ReversalVoucher = "voucher.reversal";
    public const string TaxInvoice = "invoice.tax";
    public const string CashBill = "invoice.cash";
    public const string Quotation = "sales.quotation";
    public const string SalesOrder = "sales.order";
    public const string PurchaseOrder = "purchase.order";

    public static IReadOnlyList<DocumentTypeInfo> All { get; } =
    [
        new(JournalVoucher, "Journal voucher", "JV/{FY}/", 4),
        new(PaymentVoucher, "Payment voucher", "PV/{FY}/", 4),
        new(ReceiptVoucher, "Receipt voucher", "RV/{FY}/", 4),
        new(ContraVoucher, "Contra voucher", "CV/{FY}/", 4),
        new(SalesVoucher, "Sales voucher", "SV/{FY}/", 4),
        new(PurchaseVoucher, "Purchase voucher", "PU/{FY}/", 4),
        new(ExpenseVoucher, "Expense voucher", "EV/{FY}/", 4),
        new(IncomeVoucher, "Income voucher", "IV/{FY}/", 4),
        new(CreditNoteVoucher, "Credit note", "CN/{FY}/", 4),
        new(DebitNoteVoucher, "Debit note", "DN/{FY}/", 4),
        new(OpeningVoucher, "Opening balance voucher", "OB/{FY}/", 3),
        new(ReversalVoucher, "Reversal voucher", "RJ/{FY}/", 4),
        new(TaxInvoice, "Tax invoice", "INV/{FY}/", 4),
        new(CashBill, "Cash bill", "CB/{FY}/", 4),
        new(Quotation, "Quotation", "QT/{FY}/", 4),
        new(SalesOrder, "Sales order", "SO/{FY}/", 4),
        new(PurchaseOrder, "Purchase order", "PO/{FY}/", 4),
    ];

    public static DocumentTypeInfo? Find(string key) =>
        All.FirstOrDefault(d => d.Key == key) ?? Inventory.InventoryDocumentTypes.All.FirstOrDefault(d => d.Key == key)
        ?? Billing.BillingDocumentTypes.All.FirstOrDefault(d => d.Key == key)
        ?? Expenses.ExpenseDocumentTypes.All.FirstOrDefault(d => d.Key == key)
        ?? Service.ServiceDocumentTypes.All.FirstOrDefault(d => d.Key == key);
}
