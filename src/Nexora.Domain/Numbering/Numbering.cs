using Nexora.Domain.Common;
using Nexora.Domain.Companies;

namespace Nexora.Domain.Numbering;

/// <summary>
/// How a company numbers one document type, for example journal vouchers or tax invoices.
/// The prefix may contain {FY}, replaced by the financial-year label such as 2026-27.
/// </summary>
public sealed class NumberingRule : ICompanyOwned, IAudited
{
    public const string FinancialYearToken = "{FY}";

    private NumberingRule()
    {
        DocumentType = Prefix = string.Empty;
    }

    public NumberingRule(Guid id, Guid companyId, string documentType, string prefix, int padding, bool resetEachFinancialYear)
    {
        if (id == Guid.Empty || companyId == Guid.Empty) throw new ArgumentException("Ids are required.");
        if (string.IsNullOrWhiteSpace(documentType)) throw new ArgumentException("Document type is required.", nameof(documentType));
        Id = id;
        CompanyId = companyId;
        DocumentType = documentType;
        Prefix = string.Empty;
        Update(prefix, padding, resetEachFinancialYear);
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string DocumentType { get; private set; }

    public string Prefix { get; private set; }

    public int Padding { get; private set; }

    public bool ResetEachFinancialYear { get; private set; }

    public void Update(string prefix, int padding, bool resetEachFinancialYear)
    {
        var value = (prefix ?? string.Empty).Trim();
        if (value.Length > 20) throw new DomainException("Prefix must be 20 characters or fewer.");
        if (value.Any(char.IsWhiteSpace)) throw new DomainException("Prefix cannot contain spaces.");
        if (padding is < 1 or > 8) throw new DomainException("Number width must be between 1 and 8 digits.");
        Prefix = value;
        Padding = padding;
        ResetEachFinancialYear = resetEachFinancialYear;
    }

    /// <summary>Counter period: the financial-year start when numbering resets yearly, otherwise 0.</summary>
    public int PeriodFor(FinancialYear year) => ResetEachFinancialYear ? year.StartYear : 0;

    public string Format(long number, FinancialYear year) =>
        Prefix.Replace(FinancialYearToken, year.Label, StringComparison.Ordinal) + number.ToString().PadLeft(Padding, '0');
}

/// <summary>Next number for a document type and period. Incremented only inside the posting transaction.</summary>
public sealed class NumberCounter : ICompanyOwned
{
    private NumberCounter()
    {
        DocumentType = string.Empty;
    }

    public NumberCounter(Guid id, Guid companyId, string documentType, int period)
    {
        Id = id;
        CompanyId = companyId;
        DocumentType = documentType;
        Period = period;
        Next = 1;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string DocumentType { get; private set; }

    public int Period { get; private set; }

    public long Next { get; private set; }

    public long Take() => Next++;
}
