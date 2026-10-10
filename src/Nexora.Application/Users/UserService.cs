using Microsoft.Extensions.Logging;
using Nexora.Application.Abstractions;
using Nexora.Application.Security;
using Nexora.Domain.Audit;
using Nexora.Domain.Security;
using Nexora.Domain.Users;

namespace Nexora.Application.Users;

public sealed record UserRow(Guid UserId, Guid MembershipId, string Username, string DisplayName, Guid RoleId, string RoleName,
    bool IsActive, bool MustChangePassword, DateTime? LastSignInUtc);

public sealed record NewUserRequest(string Username, string DisplayName, string TemporaryPassword, Guid RoleId);

/// <summary>Users of the signed-in company. Accounts are installation-wide; access is per company.</summary>
public sealed class UserService(
    IUnitOfWorkFactory units,
    IWorkspaceStore store,
    IWorkspaceService workspace,
    IAuthorizer authorizer,
    IPasswordHasher passwordHasher,
    IClock clock,
    ILogger<UserService> logger)
{
    public async Task<IReadOnlyList<UserRow>> ListAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.UsersView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var memberships = await unit.ListAsync<CompanyMembership>(cancellationToken: cancellationToken);
        var roles = (await unit.ListAsync<Role>(cancellationToken: cancellationToken)).ToDictionary(r => r.Id);
        var userIds = memberships.Select(m => m.UserId).ToList();
        var users = (await unit.ListAsync<User>(u => userIds.Contains(u.Id), cancellationToken)).ToDictionary(u => u.Id);
        return memberships
            .Where(m => users.ContainsKey(m.UserId))
            .Select(m =>
            {
                var user = users[m.UserId];
                return new UserRow(user.Id, m.Id, user.Username, user.DisplayName, m.RoleId, roles[m.RoleId].Name,
                    m.IsActive, user.MustChangePassword, user.LastSignInUtc);
            })
            .OrderBy(r => r.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Creates a new account with a temporary password that must be changed at first sign-in.</summary>
    public async Task<Guid> CreateAsync(NewUserRequest request, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.UsersManage);
        var username = Username.Validate(request.Username);
        if (PasswordPolicy.Check(request.TemporaryPassword, username) is { } problem) throw new WorkspaceValidationException(problem);
        if (await store.FindUserByNormalizedUsernameAsync(Username.Normalize(username), cancellationToken) is not null)
            throw new WorkspaceValidationException("That username is already taken on this computer. Add the existing user instead, or choose another username.");

        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var role = await unit.FindAsync<Role>(request.RoleId, cancellationToken) ?? throw RoleNotFound();
        EnsureCanAssign(session, role);
        var now = clock.UtcNow;
        var user = new User(Guid.NewGuid(), username, request.DisplayName, passwordHasher.Hash(request.TemporaryPassword), now);
        user.ChangePassword(user.PasswordHash, now, mustChangeOnNextSignIn: true);
        unit.Add(user);
        unit.Add(new CompanyMembership(Guid.NewGuid(), session.CompanyId, user.Id, role.Id, now));
        await unit.CommitAsync(cancellationToken);
        logger.LogInformation("User created; by {ActorId}; user {UserId}", session.UserId, user.Id);
        return user.Id;
    }

    /// <summary>Gives an account that already exists (for example from another company) access to this company.</summary>
    public async Task AddExistingAsync(string username, Guid roleId, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.UsersManage);
        var user = await store.FindUserByNormalizedUsernameAsync(Username.Normalize(username), cancellationToken)
            ?? throw new WorkspaceValidationException("No account with that username exists. Create a new user instead.");

        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        if (await unit.AnyAsync<CompanyMembership>(m => m.UserId == user.Id, cancellationToken))
            throw new WorkspaceValidationException("That user already has access to this company.");
        var role = await unit.FindAsync<Role>(roleId, cancellationToken) ?? throw RoleNotFound();
        EnsureCanAssign(session, role);
        unit.Add(new CompanyMembership(Guid.NewGuid(), session.CompanyId, user.Id, role.Id, clock.UtcNow));
        await unit.CommitAsync(cancellationToken);
    }

    public async Task ChangeRoleAsync(Guid membershipId, Guid roleId, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.UsersManage);
        await using (var unit = await units.BeginAsync(cancellationToken: cancellationToken))
        {
            var membership = await unit.FindAsync<CompanyMembership>(membershipId, cancellationToken) ?? throw UserNotFound();
            var role = await unit.FindAsync<Role>(roleId, cancellationToken) ?? throw RoleNotFound();
            EnsureCanAssign(session, role);
            if (!role.IsAdministrator) await EnsureAnotherAdministratorAsync(unit, membership, cancellationToken);
            membership.ChangeRole(role.Id);
            await unit.CommitAsync(cancellationToken);
            if (membership.UserId != session.UserId) return;
        }

        await workspace.RefreshSessionAsync(cancellationToken);
    }

    public async Task SetActiveAsync(Guid membershipId, bool active, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.UsersManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var membership = await unit.FindAsync<CompanyMembership>(membershipId, cancellationToken) ?? throw UserNotFound();
        if (!active && membership.UserId == session.UserId) throw new WorkspaceValidationException("You cannot remove your own access.");
        if (!active) await EnsureAnotherAdministratorAsync(unit, membership, cancellationToken);
        membership.SetActive(active);
        await unit.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Sets a temporary password. Because accounts are shared across companies, this is allowed only when
    /// every company the user can open is one where the caller can also manage users.
    /// </summary>
    public async Task ResetPasswordAsync(Guid userId, string temporaryPassword, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.UsersManage);
        if (userId == session.UserId) throw new WorkspaceValidationException("Use Change password to change your own password.");
        var user = await store.FindUserAsync(userId, cancellationToken) ?? throw UserNotFound();
        if (PasswordPolicy.Check(temporaryPassword, user.Username) is { } problem) throw new WorkspaceValidationException(problem);

        var callerManages = (await store.GetAccessAsync(session.UserId, cancellationToken))
            .Where(a => a.Role.Has(Permissions.UsersManage)).Select(a => a.Company.Id).ToHashSet();
        var targetCompanies = (await store.GetAccessAsync(userId, cancellationToken)).Select(a => a.Company.Id).ToList();
        if (!targetCompanies.Contains(session.CompanyId)) throw UserNotFound();
        if (targetCompanies.Any(c => !callerManages.Contains(c)))
            throw new AccessDeniedException("This user also works in a company you do not manage. Ask an administrator of that company to reset the password.");

        user.ChangePassword(passwordHasher.Hash(temporaryPassword), clock.UtcNow, mustChangeOnNextSignIn: true);
        await store.UpdateUserAsync(user, cancellationToken);

        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        unit.Record(new AuditEvent("PasswordReset", nameof(User), user.Id.ToString(), $"Temporary password set for {user.Username}"));
        await unit.CommitAsync(cancellationToken);
        logger.LogInformation("Password reset; by {ActorId}; user {UserId}", session.UserId, user.Id);
    }

    /// <summary>Prevents privilege escalation: only holders of every permission in a role may hand it out.</summary>
    private static void EnsureCanAssign(SessionInfo session, Role role)
    {
        if (role.Permissions.Any(p => !session.Has(p)))
            throw new AccessDeniedException("You cannot assign a role that has permissions you do not have yourself.");
    }

    private static async Task EnsureAnotherAdministratorAsync(IUnitOfWork unit, CompanyMembership membership, CancellationToken cancellationToken)
    {
        var adminRoleIds = (await unit.ListAsync<Role>(r => r.SystemKey == Role.AdministratorKey, cancellationToken)).Select(r => r.Id).ToList();
        if (!adminRoleIds.Contains(membership.RoleId) || !membership.IsActive) return;
        var others = await unit.AnyAsync<CompanyMembership>(
            m => m.Id != membership.Id && m.IsActive && adminRoleIds.Contains(m.RoleId), cancellationToken);
        if (!others) throw new WorkspaceValidationException("Every company needs at least one active administrator.");
    }

    private static WorkspaceValidationException UserNotFound() => new("That user no longer has access to this company.");

    private static WorkspaceValidationException RoleNotFound() => new("That role no longer exists. Refresh and try again.");
}
