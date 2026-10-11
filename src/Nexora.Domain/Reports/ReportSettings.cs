using Nexora.Domain.Common;

namespace Nexora.Domain.Reports;

/// <summary>
/// Report settings of a company: the ageing bucket boundaries in days. Also the Phase 8 seed marker, so its absence
/// means the company has not been upgraded to Phase 8 yet.
/// </summary>
public sealed class ReportSettings : ICompanyOwned, IAudited
{
    public static readonly IReadOnlyList<int> DefaultBuckets = [30, 60, 90, 180];

    private ReportSettings()
    {
        AgeingBuckets = string.Empty;
    }

    public ReportSettings(Guid id, Guid companyId) : this()
    {
        Id = id;
        CompanyId = companyId;
        SetAgeingBuckets(DefaultBuckets);
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>Upper bounds of the ageing columns in days, ascending, for example "30,60,90,180".</summary>
    public string AgeingBuckets { get; private set; }

    public IReadOnlyList<int> Buckets => AgeingBuckets.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();

    public void SetAgeingBuckets(IReadOnlyList<int> days)
    {
        if (days.Count is < 1 or > 6) throw new DomainException("Use between one and six ageing columns.");
        for (var i = 0; i < days.Count; i++)
        {
            if (days[i] is < 1 or > 3650) throw new DomainException("Ageing days must be between 1 and 3650.");
            if (i > 0 && days[i] <= days[i - 1]) throw new DomainException("Ageing days must increase from left to right, for example 30, 60, 90.");
        }

        AgeingBuckets = string.Join(',', days);
    }
}
