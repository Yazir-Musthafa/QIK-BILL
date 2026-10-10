using Nexora.Domain.Companies;
using Nexora.Domain.Security;
using Nexora.Domain.Users;

namespace Nexora.Application.Abstractions;

/// <summary>A new company and every row it starts with (roles, numbering, chart of accounts, ...).</summary>
public sealed record CompanyProvisioning(Company Company, IReadOnlyList<object> Entities);

/// <summary>A company a user may open, with the role they have there.</summary>
public sealed record CompanyAccess(Company Company, Role Role, CompanyMembership Membership);

/// <summary>
/// Installation-level persistence: setup, sign-in and company provisioning. These operations happen
/// before or across company sessions, so they are the only place allowed to bypass the company filter.
/// Each method is a self-contained unit of work.
/// </summary>
public interface IWorkspaceStore
{
    /// <summary>Creates or upgrades the database schema.</summary>
    Task MigrateAsync(CancellationToken cancellationToken);

    Task<bool> HasAnyUserAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Atomically stores the first companies and administrator. Throws
    /// <see cref="WorkspaceAlreadyInitializedException"/> if any user already exists.
    /// </summary>
    Task CreateInitialWorkspaceAsync(IReadOnlyCollection<CompanyProvisioning> companies, User administrator, CancellationToken cancellationToken);

    /// <summary>Stores a new company created by a signed-in user.</summary>
    Task ProvisionCompanyAsync(CompanyProvisioning provisioning, CancellationToken cancellationToken);

    Task<User?> FindUserByNormalizedUsernameAsync(string normalizedUsername, CancellationToken cancellationToken);

    Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken);

    Task UpdateUserAsync(User user, CancellationToken cancellationToken);

    /// <summary>Companies where the user has an active membership, across all companies.</summary>
    Task<IReadOnlyList<CompanyAccess>> GetAccessAsync(Guid userId, CancellationToken cancellationToken);

    Task DeleteCompanyAsync(Guid companyId, CancellationToken cancellationToken);

    // ----- Upgrade support: bring companies created by older versions up to date -----

    Task<IReadOnlyList<Company>> ListAllCompaniesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> ListAllUserIdsAsync(CancellationToken cancellationToken);

    /// <summary>True if the company has at least one row of the given company-owned entity type.</summary>
    Task<bool> CompanyHasAsync(Guid companyId, Type entityType, CancellationToken cancellationToken);

    /// <summary>Adds rows to an existing company outside a session, recorded in its audit trail as a system upgrade.</summary>
    Task AddToCompanyAsync(Guid companyId, IReadOnlyList<object> entities, CancellationToken cancellationToken);

    Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken);

    Task SetSettingsAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken);
}

public sealed class WorkspaceAlreadyInitializedException()
    : Exception("Setup has already been completed on this computer.");
