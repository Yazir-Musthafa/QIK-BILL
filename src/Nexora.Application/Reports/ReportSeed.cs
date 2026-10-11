using Nexora.Domain.Companies;
using Nexora.Domain.Reports;

namespace Nexora.Application.Reports;

/// <summary>Gives each company its report settings (default ageing columns). Also marks the Phase 8 upgrade as done.</summary>
public sealed class ReportSeedContributor : ICompanySeedContributor
{
    public Type MarkerType => typeof(ReportSettings);

    public IEnumerable<object> Seed(Company company, DateTime nowUtc)
    {
        yield return new ReportSettings(Guid.NewGuid(), company.Id);
    }
}
