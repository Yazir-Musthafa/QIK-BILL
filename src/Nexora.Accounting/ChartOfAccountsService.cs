using Nexora.Application;
using Nexora.Application.Abstractions;
using Nexora.Application.Security;
using Nexora.Domain.Accounting;
using Nexora.Domain.Security;

namespace Nexora.Accounting;

public sealed record AccountRow(Guid Id, string Code, string Name, AccountNature Nature, AccountKind Kind, Guid? ParentId,
    string? SystemKey, bool IsActive, int Depth)
{
    public bool IsGroup => Kind == AccountKind.Group;
}

public sealed record AccountInput(Guid? Id, string Code, string Name, Guid ParentId, AccountKind Kind);

public sealed class ChartOfAccountsService(IUnitOfWorkFactory units, IAuthorizer authorizer)
{
    /// <summary>All accounts in tree order (each group followed by its children), with their depth.</summary>
    public async Task<IReadOnlyList<AccountRow>> ListAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.AccountsView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var accounts = await unit.ListAsync<Account>(cancellationToken: cancellationToken);
        var children = accounts.ToLookup(a => a.ParentId);
        var rows = new List<AccountRow>();
        void Walk(Guid? parent, int depth)
        {
            foreach (var account in children[parent].OrderBy(a => a.Code, StringComparer.Ordinal))
            {
                rows.Add(new AccountRow(account.Id, account.Code, account.Name, account.Nature, account.Kind, account.ParentId,
                    account.SystemKey, account.IsActive, depth));
                Walk(account.Id, depth + 1);
            }
        }

        Walk(null, 0);
        return rows;
    }

    /// <summary>Creates or updates a ledger or a group. Nature is inherited from the parent group.</summary>
    public async Task<Guid> SaveAsync(AccountInput input, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.AccountsManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var parent = await unit.FindAsync<Account>(input.ParentId, cancellationToken)
            ?? throw new WorkspaceValidationException("Choose the group this account belongs to.");
        var code = (input.Code ?? string.Empty).Trim().ToUpperInvariant();
        var name = (input.Name ?? string.Empty).Trim();
        var selfId = input.Id ?? Guid.Empty;
        if (await unit.AnyAsync<Account>(a => a.Code == code && a.Id != selfId, cancellationToken))
            throw new WorkspaceValidationException($"Account code {code} is already used.");
        if (await unit.AnyAsync<Account>(a => a.Name == name && a.Id != selfId, cancellationToken))
            throw new WorkspaceValidationException($"An account named \"{name}\" already exists.");

        Account account;
        if (input.Id is { } id)
        {
            account = await unit.FindAsync<Account>(id, cancellationToken)
                ?? throw new WorkspaceValidationException("That account no longer exists. Refresh and try again.");
            if (parent.Nature != account.Nature)
                throw new WorkspaceValidationException("An account cannot move to a group of a different nature.");
            if (await IsDescendantAsync(unit, parent.Id, account.Id, cancellationToken))
                throw new WorkspaceValidationException("A group cannot be moved inside itself.");
            account.Rename(code, name);
            account.MoveTo(parent);
            if (!account.IsGroup && account.Kind != input.Kind)
            {
                if (await unit.AnyAsync<LedgerEntry>(e => e.AccountId == account.Id, cancellationToken))
                    throw new WorkspaceValidationException("The type of an account with postings cannot be changed.");
                account.ChangeKind(input.Kind);
            }
        }
        else
        {
            if (!parent.IsGroup) throw new WorkspaceValidationException("Accounts can only be created under a group.");
            account = new Account(Guid.NewGuid(), session.CompanyId, code, name, parent.Nature, input.Kind, parent.Id, null);
            unit.Add(account);
        }

        await unit.CommitAsync(cancellationToken);
        return account.Id;
    }

    public async Task SetActiveAsync(Guid accountId, bool active, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.AccountsManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var account = await unit.FindAsync<Account>(accountId, cancellationToken)
            ?? throw new WorkspaceValidationException("That account no longer exists.");
        if (!active && account.IsGroup && await unit.AnyAsync<Account>(a => a.ParentId == accountId && a.IsActive, cancellationToken))
            throw new WorkspaceValidationException("Deactivate the accounts inside this group first.");
        account.SetActive(active);
        await unit.CommitAsync(cancellationToken);
    }

    private static async Task<bool> IsDescendantAsync(IUnitOfWork unit, Guid candidateId, Guid ancestorId, CancellationToken cancellationToken)
    {
        var all = (await unit.ListAsync<Account>(cancellationToken: cancellationToken)).ToDictionary(a => a.Id);
        for (Guid? current = candidateId; current is { } id && all.TryGetValue(id, out var node); current = node.ParentId)
        {
            if (id == ancestorId) return true;
        }

        return false;
    }
}
