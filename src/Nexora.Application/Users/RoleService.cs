using Nexora.Application.Abstractions;
using Nexora.Application.Security;
using Nexora.Domain.Security;

namespace Nexora.Application.Users;

public sealed record RoleRow(Guid Id, string Name, string Description, bool IsSystem, bool IsAdministrator,
    IReadOnlySet<string> Permissions, decimal MaxDiscountPercent, int MemberCount);

public sealed record RoleInput(Guid? Id, string Name, string Description, IReadOnlyCollection<string> Permissions, decimal MaxDiscountPercent);

public sealed class RoleService(IUnitOfWorkFactory units, IWorkspaceService workspace, IAuthorizer authorizer)
{
    public IReadOnlyList<PermissionInfo> Catalogue => Permissions.All;

    public async Task<IReadOnlyList<RoleRow>> ListAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.UsersView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var roles = await unit.ListAsync<Role>(cancellationToken: cancellationToken);
        var members = (await unit.ListAsync<CompanyMembership>(m => m.IsActive, cancellationToken))
            .GroupBy(m => m.RoleId).ToDictionary(g => g.Key, g => g.Count());
        return roles
            .OrderBy(r => !r.IsAdministrator).ThenBy(r => !r.IsSystem).ThenBy(r => r.Name)
            .Select(r => new RoleRow(r.Id, r.Name, r.Description, r.IsSystem, r.IsAdministrator, r.Permissions,
                r.MaxDiscountPercent, members.GetValueOrDefault(r.Id)))
            .ToList();
    }

    public async Task<Guid> SaveAsync(RoleInput input, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.RolesManage);
        // No escalation: a role can only receive permissions the editor already has.
        var missing = input.Permissions.Where(p => !session.Has(p)).ToList();
        if (missing.Count > 0) throw new AccessDeniedException("You cannot grant permissions you do not have yourself.");

        Guid roleId;
        await using (var unit = await units.BeginAsync(cancellationToken: cancellationToken))
        {
            var name = (input.Name ?? string.Empty).Trim();
            if (await unit.AnyAsync<Role>(r => r.Name == name && r.Id != (input.Id ?? Guid.Empty), cancellationToken))
                throw new WorkspaceValidationException($"A role named \"{name}\" already exists.");

            Role role;
            if (input.Id is { } id)
            {
                role = await unit.FindAsync<Role>(id, cancellationToken)
                    ?? throw new WorkspaceValidationException("That role no longer exists. Refresh and try again.");
                role.Rename(input.Name!, input.Description);
                if (!role.IsAdministrator)
                {
                    role.SetPermissions(input.Permissions);
                    role.SetMaxDiscountPercent(input.MaxDiscountPercent);
                }
            }
            else
            {
                role = new Role(Guid.NewGuid(), session.CompanyId, input.Name!, input.Description, null, input.Permissions, input.MaxDiscountPercent);
                unit.Add(role);
            }

            await unit.CommitAsync(cancellationToken);
            roleId = role.Id;
        }

        if (roleId == session.RoleId) await workspace.RefreshSessionAsync(cancellationToken);
        return roleId;
    }

    public async Task DeleteAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.RolesManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var role = await unit.FindAsync<Role>(roleId, cancellationToken)
            ?? throw new WorkspaceValidationException("That role no longer exists.");
        if (role.IsSystem) throw new WorkspaceValidationException("Built-in roles cannot be deleted.");
        if (await unit.AnyAsync<CompanyMembership>(m => m.RoleId == roleId, cancellationToken))
            throw new WorkspaceValidationException("Move the users of this role to another role first.");
        unit.Remove(role);
        await unit.CommitAsync(cancellationToken);
    }
}
