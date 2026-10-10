using Nexora.Application.Abstractions;
using Nexora.Domain.Companies;
using Nexora.Domain.Numbering;
using Nexora.Domain.Security;

namespace Nexora.Application;

/// <summary>Adds module-specific starting data (for example a chart of accounts) to a new company.</summary>
public interface ICompanySeedContributor
{
    /// <summary>A company-owned entity type whose absence means the seed has not been applied yet.</summary>
    Type MarkerType { get; }

    IEnumerable<object> Seed(Company company, DateTime nowUtc);
}

/// <summary>
/// Builds everything a new company starts with: the built-in roles, an administrator membership for
/// its creator, numbering rules for every document type, and whatever registered modules contribute.
/// </summary>
public sealed class CompanyProvisioner(IEnumerable<ICompanySeedContributor> contributors)
{
    public CompanyProvisioning Create(Company company, Guid administratorUserId, DateTime nowUtc) =>
        new(company, CoreSeed(company, [administratorUserId], nowUtc).Concat(contributors.SelectMany(c => c.Seed(company, nowUtc))).ToList());

    /// <summary>
    /// Upgrades a company created by an older version. Phase 1 had no roles and every user was an
    /// administrator, so that access is preserved: each existing user becomes an administrator of it.
    /// </summary>
    public async Task UpgradeAsync(IWorkspaceStore store, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var companies = await store.ListAllCompaniesAsync(cancellationToken);
        if (companies.Count == 0) return;
        var userIds = await store.ListAllUserIdsAsync(cancellationToken);
        foreach (var company in companies)
        {
            var entities = new List<object>();
            if (!await store.CompanyHasAsync(company.Id, typeof(Role), cancellationToken)) entities.AddRange(CoreSeed(company, userIds, nowUtc));
            foreach (var contributor in contributors)
            {
                if (!await store.CompanyHasAsync(company.Id, contributor.MarkerType, cancellationToken))
                    entities.AddRange(contributor.Seed(company, nowUtc));
            }

            if (entities.Count > 0) await store.AddToCompanyAsync(company.Id, entities, cancellationToken);
        }
    }

    private static IEnumerable<object> CoreSeed(Company company, IReadOnlyList<Guid> administratorUserIds, DateTime nowUtc)
    {
        var roles = RoleTemplates.All.Select(t => RoleTemplates.Create(t, company.Id)).ToList();
        var administrator = roles.Single(r => r.IsAdministrator);
        foreach (var role in roles) yield return role;
        foreach (var userId in administratorUserIds)
            yield return new CompanyMembership(Guid.NewGuid(), company.Id, userId, administrator.Id, nowUtc);
        foreach (var type in DocumentTypes.All)
            yield return new NumberingRule(Guid.NewGuid(), company.Id, type.Key, type.DefaultPrefix, type.DefaultPadding, resetEachFinancialYear: true);
    }
}
