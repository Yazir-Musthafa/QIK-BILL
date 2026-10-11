using Nexora.Domain.Companies;
using Nexora.Domain.Expenses;
using Nexora.Domain.Numbering;
using Nexora.Domain.Service;

namespace Nexora.Application.Expenses;

/// <summary>
/// Gives each new company, and each existing company on upgrade, its expense and service settings and the numbering
/// for expense vouchers (EXP/) and service job cards (JOB/). CRM needs no seed.
/// </summary>
public sealed class ExpenseSeedContributor : ICompanySeedContributor
{
    public Type MarkerType => typeof(ExpenseSettings);

    public IEnumerable<object> Seed(Company company, DateTime nowUtc)
    {
        yield return new ExpenseSettings(Guid.NewGuid(), company.Id);
        foreach (var type in ExpenseDocumentTypes.All)
            yield return new NumberingRule(Guid.NewGuid(), company.Id, type.Key, type.DefaultPrefix, type.DefaultPadding, resetEachFinancialYear: true);
    }
}

public sealed class ServiceSeedContributor : ICompanySeedContributor
{
    public Type MarkerType => typeof(ServiceSettings);

    public IEnumerable<object> Seed(Company company, DateTime nowUtc)
    {
        yield return new ServiceSettings(Guid.NewGuid(), company.Id);
        foreach (var type in ServiceDocumentTypes.All)
            yield return new NumberingRule(Guid.NewGuid(), company.Id, type.Key, type.DefaultPrefix, type.DefaultPadding, resetEachFinancialYear: true);
    }
}
