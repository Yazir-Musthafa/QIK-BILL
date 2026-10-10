using Nexora.Domain.Common;
using Nexora.Domain.Numbering;

namespace Nexora.Domain.Accounting;

public enum VoucherType
{
    Journal,
    Payment,
    Receipt,
    Contra,
    Sales,
    Purchase,
    Expense,
    Income,
    CreditNote,
    DebitNote,
    Opening,
    Reversal,
}

public enum VoucherStatus
{
    Draft,
    Posted,
}

public static class VoucherTypeExtensions
{
    public static string DocumentType(this VoucherType type) => type switch
    {
        VoucherType.Journal => DocumentTypes.JournalVoucher,
        VoucherType.Payment => DocumentTypes.PaymentVoucher,
        VoucherType.Receipt => DocumentTypes.ReceiptVoucher,
        VoucherType.Contra => DocumentTypes.ContraVoucher,
        VoucherType.Sales => DocumentTypes.SalesVoucher,
        VoucherType.Purchase => DocumentTypes.PurchaseVoucher,
        VoucherType.Expense => DocumentTypes.ExpenseVoucher,
        VoucherType.Income => DocumentTypes.IncomeVoucher,
        VoucherType.CreditNote => DocumentTypes.CreditNoteVoucher,
        VoucherType.DebitNote => DocumentTypes.DebitNoteVoucher,
        VoucherType.Opening => DocumentTypes.OpeningVoucher,
        VoucherType.Reversal => DocumentTypes.ReversalVoucher,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static string Label(this VoucherType type) => type switch
    {
        VoucherType.CreditNote => "Credit note",
        VoucherType.DebitNote => "Debit note",
        VoucherType.Opening => "Opening balance",
        _ => type.ToString(),
    };
}

/// <summary>Input for one voucher line. Exactly one of debit or credit is positive.</summary>
public sealed record VoucherLineInput(Guid AccountId, decimal Debit, decimal Credit, string? Narration = null);

/// <summary>
/// A double-entry voucher. Drafts can be edited freely; posting checks that the voucher balances and freezes it.
/// After posting, the only way to change its effect is a reversal voucher (see <see cref="CreateReversal"/>).
/// </summary>
public sealed class Voucher : ICompanyOwned, IAudited
{
    public const int MaxLines = 500;

    private readonly List<VoucherLine> _lines = [];

    private Voucher()
    {
        Narration = string.Empty;
    }

    public Voucher(Guid id, Guid companyId, VoucherType type, DateOnly date, string narration, DateTime createdUtc, Guid createdBy)
    {
        if (id == Guid.Empty || companyId == Guid.Empty) throw new ArgumentException("Ids are required.");
        if (type == VoucherType.Reversal) throw new DomainException("Reversal vouchers are created by reversing a posted voucher.");
        Id = id;
        CompanyId = companyId;
        Type = type;
        Date = date;
        Narration = CleanNarration(narration);
        Status = VoucherStatus.Draft;
        CreatedUtc = DateTime.SpecifyKind(createdUtc, DateTimeKind.Utc);
        CreatedBy = createdBy;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public VoucherType Type { get; private set; }

    /// <summary>Assigned from the company's numbering rule when posted; null while a draft.</summary>
    public string? Number { get; private set; }

    public DateOnly Date { get; private set; }

    /// <summary>Start year of the financial year the voucher was posted in; 0 while a draft.</summary>
    public int FinancialYearStart { get; private set; }

    public string Narration { get; private set; }

    public VoucherStatus Status { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTime? PostedUtc { get; private set; }

    public Guid? PostedBy { get; private set; }

    /// <summary>For a reversal voucher: the posted voucher whose effect it cancels.</summary>
    public Guid? ReversesVoucherId { get; private set; }

    /// <summary>Set when another module created the voucher, for example "stock". Such vouchers are reversed from their source.</summary>
    public string? SourceType { get; private set; }

    public Guid? SourceId { get; private set; }

    public IReadOnlyList<VoucherLine> Lines => _lines;

    public bool IsPosted => Status == VoucherStatus.Posted;

    public decimal TotalDebit => _lines.Sum(l => l.Debit);

    public decimal TotalCredit => _lines.Sum(l => l.Credit);

    public void UpdateDraft(DateOnly date, string narration, IReadOnlyList<VoucherLineInput> lines)
    {
        EnsureDraft();
        if (lines.Count > MaxLines) throw new DomainException($"A voucher can have at most {MaxLines} lines.");
        Date = date;
        Narration = CleanNarration(narration);
        _lines.Clear();
        for (var i = 0; i < lines.Count; i++)
        {
            var input = lines[i];
            _lines.Add(new VoucherLine(Guid.NewGuid(), CompanyId, Id, i + 1, input.AccountId, input.Debit, input.Credit, input.Narration));
        }
    }

    /// <summary>Records which business document created this voucher. Only for drafts, before posting.</summary>
    public void MarkSource(string sourceType, Guid sourceId)
    {
        EnsureDraft();
        if (string.IsNullOrWhiteSpace(sourceType) || sourceType.Length > 20) throw new ArgumentException("Source type is required.", nameof(sourceType));
        SourceType = sourceType;
        SourceId = sourceId;
    }

    /// <summary>
    /// Checks the double-entry rules: at least two lines, every line on one side only, positive totals,
    /// and total debits equal to total credits. Enforced here so no layer can post an unbalanced journal.
    /// </summary>
    public void EnsureBalanced()
    {
        if (_lines.Count < 2) throw new DomainException("A voucher needs at least two lines.");
        var debit = TotalDebit;
        var credit = TotalCredit;
        if (debit == 0) throw new DomainException("The voucher total cannot be zero.");
        if (debit != credit)
            throw new DomainException($"Debits ({debit:N2}) and credits ({credit:N2}) differ by {Math.Abs(debit - credit):N2}. A voucher must balance.");
        if (_lines.Select(l => l.AccountId).Distinct().Count() < 2)
            throw new DomainException("A voucher must involve at least two different accounts.");
    }

    /// <summary>Freezes the voucher with its number. Called only by the posting engine inside its transaction.</summary>
    public IReadOnlyList<LedgerEntry> Post(string number, int financialYearStart, DateTime nowUtc, Guid postedBy)
    {
        EnsureDraft();
        EnsureBalanced();
        if (string.IsNullOrWhiteSpace(number)) throw new ArgumentException("Number is required.", nameof(number));
        Number = number;
        FinancialYearStart = financialYearStart;
        Status = VoucherStatus.Posted;
        PostedUtc = nowUtc;
        PostedBy = postedBy;
        foreach (var line in _lines) line.Lock();
        return _lines.Select(l => new LedgerEntry(Guid.NewGuid(), CompanyId, Id, l.LineNumber, l.AccountId, Date, financialYearStart, l.Debit, l.Credit))
            .ToList();
    }

    /// <summary>
    /// Creates a draft reversal: same accounts, debits and credits swapped, dated <paramref name="date"/>.
    /// The original stays posted and unchanged.
    /// </summary>
    public Voucher CreateReversal(Guid id, DateOnly date, string narration, DateTime nowUtc, Guid createdBy)
    {
        if (!IsPosted) throw new DomainException("Only posted vouchers can be reversed. Edit or delete the draft instead.");
        if (Type == VoucherType.Reversal) throw new DomainException("A reversal cannot itself be reversed. Post a new voucher instead.");
        if (date < Date) throw new DomainException("A reversal cannot be dated before the voucher it reverses.");

        var reversal = new Voucher
        {
            Id = id,
            CompanyId = CompanyId,
            Type = VoucherType.Reversal,
            Date = date,
            Narration = CleanNarration(string.IsNullOrWhiteSpace(narration) ? $"Reversal of {Number}" : narration),
            Status = VoucherStatus.Draft,
            CreatedUtc = nowUtc,
            CreatedBy = createdBy,
            ReversesVoucherId = Id,
            SourceType = SourceType,
            SourceId = SourceId,
        };
        reversal.UpdateDraft(date, reversal.Narration,
            _lines.Select(l => new VoucherLineInput(l.AccountId, l.Credit, l.Debit, l.Narration)).ToList());
        return reversal;
    }

    private void EnsureDraft()
    {
        if (IsPosted) throw new DomainException($"Voucher {Number} is posted and cannot be changed. Reverse it instead.");
    }

    private static string CleanNarration(string? narration)
    {
        var text = (narration ?? string.Empty).Trim();
        if (text.Length > 500) throw new DomainException("Narration must be 500 characters or fewer.");
        return text;
    }
}

public sealed class VoucherLine : ICompanyOwned, IAudited
{
    private VoucherLine()
    {
    }

    internal VoucherLine(Guid id, Guid companyId, Guid voucherId, int lineNumber, Guid accountId, decimal debit, decimal credit, string? narration)
    {
        if (accountId == Guid.Empty) throw new DomainException($"Line {lineNumber}: choose an account.");
        Money.EnsureValid(debit, $"Line {lineNumber} debit");
        Money.EnsureValid(credit, $"Line {lineNumber} credit");
        if ((debit > 0) == (credit > 0)) throw new DomainException($"Line {lineNumber}: enter either a debit or a credit amount, not both or neither.");
        var text = (narration ?? string.Empty).Trim();
        if (text.Length > 200) throw new DomainException($"Line {lineNumber}: narration must be 200 characters or fewer.");
        Id = id;
        CompanyId = companyId;
        VoucherId = voucherId;
        LineNumber = lineNumber;
        AccountId = accountId;
        Debit = debit;
        Credit = credit;
        Narration = text;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid VoucherId { get; private set; }

    public int LineNumber { get; private set; }

    public Guid AccountId { get; private set; }

    public decimal Debit { get; private set; }

    public decimal Credit { get; private set; }

    public string? Narration { get; private set; }

    /// <summary>Set when the voucher is posted; locked lines can never change again.</summary>
    public bool IsLocked { get; private set; }

    internal void Lock() => IsLocked = true;
}

/// <summary>
/// One posting in the general ledger, written when a voucher is posted. Append-only: never updated or
/// deleted (enforced in the application and by database triggers). Ledgers and reports read only these rows.
/// </summary>
public sealed class LedgerEntry : ICompanyOwned
{
    private LedgerEntry()
    {
    }

    public LedgerEntry(Guid id, Guid companyId, Guid voucherId, int lineNumber, Guid accountId, DateOnly date, int financialYearStart,
        decimal debit, decimal credit)
    {
        Id = id;
        CompanyId = companyId;
        VoucherId = voucherId;
        LineNumber = lineNumber;
        AccountId = accountId;
        Date = date;
        FinancialYearStart = financialYearStart;
        Debit = Money.EnsureValid(debit);
        Credit = Money.EnsureValid(credit);
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid VoucherId { get; private set; }

    public int LineNumber { get; private set; }

    public Guid AccountId { get; private set; }

    public DateOnly Date { get; private set; }

    public int FinancialYearStart { get; private set; }

    public decimal Debit { get; private set; }

    public decimal Credit { get; private set; }
}
