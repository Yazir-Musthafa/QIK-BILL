using Nexora.Domain.Companies;
using Nexora.Domain.Users;

namespace Nexora.Application.Abstractions;

/// <summary>
/// Persistence boundary for Phase 1 workspace data. Each method is a self-contained unit of work,
/// so implementations can use a short-lived context per call. Provider details stay in Infrastructure.
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
    Task CreateInitialWorkspaceAsync(IReadOnlyCollection<Company> companies, User administrator, CancellationToken cancellationToken);

    Task<User?> FindUserByNormalizedUsernameAsync(string normalizedUsername, CancellationToken cancellationToken);

    Task UpdateUserAsync(User user, CancellationToken cancellationToken);

    Task<IReadOnlyList<Company>> GetCompaniesAsync(CancellationToken cancellationToken);

    Task<Company?> FindCompanyAsync(Guid companyId, CancellationToken cancellationToken);

    Task DeleteCompanyAsync(Guid companyId, CancellationToken cancellationToken);

    Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken);

    Task SetSettingsAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken);
}

public sealed class WorkspaceAlreadyInitializedException()
    : Exception("Setup has already been completed on this computer.");
