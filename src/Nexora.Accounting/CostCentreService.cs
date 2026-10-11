using Nexora.Application;
using Nexora.Application.Abstractions;
using Nexora.Application.Security;
using Nexora.Domain.Accounting;
using Nexora.Domain.Audit;
using Nexora.Domain.Common;
using Nexora.Domain.Security;

namespace Nexora.Accounting;

public sealed record CostCentreRow(Guid Id, string Code, string Name, CostCentreKind Kind, bool IsActive)
{
    public string Display => $"{Code} · {Name}";
}

/// <summary>Cost and profit centres that income and expense voucher lines can be tagged with. accounts.view to read, accounts.manage to change.</summary>
public sealed class CostCentreService(IUnitOfWorkFactory units, IAuthorizer authorizer)
{
    public async Task<IReadOnlyList<CostCentreRow>> ListAsync(bool includeInactive = true, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.AccountsView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        return (await unit.ListAsync<CostCentre>(c => includeInactive || c.IsActive, cancellationToken)).OrderBy(c => c.Code, StringComparer.Ordinal)
            .Select(c => new CostCentreRow(c.Id, c.Code, c.Name, c.Kind, c.IsActive)).ToList();
    }

    public async Task<Guid> SaveAsync(Guid? id, string code, string name, CostCentreKind kind, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.AccountsManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var cleanCode = (code ?? string.Empty).Trim().ToUpperInvariant();
        var cleanName = (name ?? string.Empty).Trim();
        if (await unit.AnyAsync<CostCentre>(c => c.Id != id && c.Code == cleanCode, cancellationToken)) throw new WorkspaceValidationException($"Code {cleanCode} is already used.");
        if (await unit.AnyAsync<CostCentre>(c => c.Id != id && c.Name == cleanName, cancellationToken)) throw new WorkspaceValidationException($"A centre named \"{cleanName}\" already exists.");
        CostCentre centre;
        if (id is { } existing)
        {
            centre = await unit.FindAsync<CostCentre>(existing, cancellationToken) ?? throw new WorkspaceValidationException("That cost centre no longer exists.");
            Guard(() => centre.Update(cleanCode, cleanName, kind));
        }
        else
        {
            centre = Guard(() => new CostCentre(Guid.NewGuid(), session.CompanyId, cleanCode, cleanName, kind));
            unit.Add(centre);
        }

        await unit.CommitAsync(cancellationToken);
        return centre.Id;
    }

    public async Task SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.AccountsManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var centre = await unit.FindAsync<CostCentre>(id, cancellationToken) ?? throw new WorkspaceValidationException("That cost centre no longer exists.");
        centre.SetActive(active);
        unit.Record(new AuditEvent(active ? "Activated" : "Deactivated", nameof(CostCentre), id.ToString(), $"{centre.Code} {(active ? "activated" : "deactivated")}"));
        await unit.CommitAsync(cancellationToken);
    }

    private static T Guard<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (DomainException ex)
        {
            throw new WorkspaceValidationException(ex.Message);
        }
    }

    private static void Guard(Action action) => Guard(() =>
    {
        action();
        return 0;
    });
}
