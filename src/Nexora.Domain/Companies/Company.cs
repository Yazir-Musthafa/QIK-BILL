using Nexora.Domain.Common;

namespace Nexora.Domain.Companies;

public sealed class Company
{
    public const int NameMaxLength = 120;

    private Company()
    {
        Name = string.Empty;
        StateCode = string.Empty;
    }

    public Company(Guid id, string name, string stateCode, FinancialYear firstFinancialYear, bool isDemo, DateTime createdUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Company id is required.", nameof(id));
        Id = id;
        Name = NormalizeName(name);
        StateCode = ValidateStateCode(stateCode);
        FinancialYearStartYear = firstFinancialYear.StartYear;
        IsDemo = isDemo;
        CreatedUtc = DateTime.SpecifyKind(createdUtc, DateTimeKind.Utc);
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    /// <summary>Two-digit GST state code of the principal place of business.</summary>
    public string StateCode { get; private set; }

    /// <summary>Start year of the first financial year the company keeps books for.</summary>
    public int FinancialYearStartYear { get; private set; }

    /// <summary>Demonstration companies hold sample data only and may be deleted.</summary>
    public bool IsDemo { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    public FinancialYear FirstFinancialYear => new(FinancialYearStartYear);

    public void Rename(string name) => Name = NormalizeName(name);

    private static string NormalizeName(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0) throw new DomainException("Enter the company name.");
        if (trimmed.Length > NameMaxLength)
            throw new DomainException($"Company name must be {NameMaxLength} characters or fewer.");
        return trimmed;
    }

    private static string ValidateStateCode(string? stateCode)
    {
        if (!IndianState.IsValidCode(stateCode)) throw new DomainException("Choose the state of the principal place of business.");
        return stateCode!;
    }
}
