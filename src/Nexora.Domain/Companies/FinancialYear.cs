using Nexora.Domain.Common;

namespace Nexora.Domain.Companies;

/// <summary>
/// An Indian financial year, running from 1 April to 31 March of the following year.
/// </summary>
public readonly record struct FinancialYear
{
    public const int MinimumStartYear = 2000;
    public const int MaximumStartYear = 2100;

    public FinancialYear(int startYear)
    {
        if (startYear is < MinimumStartYear or > MaximumStartYear)
        {
            throw new DomainException(
                $"Financial year must start between {MinimumStartYear} and {MaximumStartYear}.");
        }

        StartYear = startYear;
    }

    public int StartYear { get; }

    public DateOnly StartDate => new(StartYear, 4, 1);

    public DateOnly EndDate => new(StartYear + 1, 3, 31);

    /// <summary>Label in the conventional "2026-27" form.</summary>
    public string Label => $"{StartYear}-{(StartYear + 1) % 100:00}";

    public bool Contains(DateOnly date) => date >= StartDate && date <= EndDate;

    public static FinancialYear Containing(DateOnly date) =>
        new(date.Month >= 4 ? date.Year : date.Year - 1);

    public override string ToString() => Label;
}
