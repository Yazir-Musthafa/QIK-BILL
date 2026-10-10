using Nexora.Application.Abstractions;
using Nexora.Application.Security;
using Nexora.Domain.Accounting;
using Nexora.Domain.Billing;
using Nexora.Domain.Common;
using Nexora.Domain.Companies;
using Nexora.Domain.Security;

namespace Nexora.Application.Billing;

public sealed record PartyInput(Guid? Id, PartyKind Kind, PartyDetails Details);

/// <summary>
/// A customer or supplier with its balances. <see cref="LedgerBalance"/> is read from the party's ledger account
/// (receivable for customers, payable for suppliers, so positive means the usual direction).
/// <see cref="Outstanding"/> is the open amount of invoices or bills; <see cref="Unapplied"/> the open amount of
/// receipts, payments and returns not yet allocated. Without manual journals on the party ledger,
/// LedgerBalance = Outstanding − Unapplied.
/// </summary>
public sealed record PartyRow(Guid Id, PartyKind Kind, string Name, string Phone, string Email, string Gstin, string StateCode, string State,
    string Address, string City, string Pincode, decimal CreditLimit, bool IsActive, Guid AccountId, decimal LedgerBalance, decimal Outstanding, decimal Unapplied);

/// <summary>An invoice, bill, return, receipt, payment or refund of a party with what is still open on it.</summary>
public sealed record OpenItemRow(OpenItemSource Source, Guid Id, string Type, string Number, DateOnly Date, bool IsCharge, decimal Amount,
    decimal Allocated, string Reference)
{
    public decimal Open => Amount - Allocated;
}

/// <summary>Customers and suppliers. Creating a party creates its ledger under Sundry Debtors or Sundry Creditors.</summary>
public sealed class PartyService(IUnitOfWorkFactory units, IAuthorizer authorizer, ILedgerPoster ledger)
{
    public async Task<IReadOnlyList<PartyRow>> ListAsync(PartyKind kind, bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(ViewPermission(kind));
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var parties = await unit.ListAsync<Party>(p => p.Kind == kind && (includeInactive || p.IsActive), cancellationToken);
        var accountIds = parties.Select(p => p.AccountId).ToList();
        var entries = (await unit.ListAsync<LedgerEntry>(e => accountIds.Contains(e.AccountId), cancellationToken))
            .GroupBy(e => e.AccountId).ToDictionary(g => g.Key, g => g.Sum(e => e.Debit - e.Credit));
        var items = await OpenItems.LoadAsync(unit, null, cancellationToken);
        return parties.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => Row(p, entries.GetValueOrDefault(p.AccountId), items.Where(i => i.PartyId == p.Id)))
            .ToList();
    }

    public async Task<PartyRow> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var party = await unit.FindAsync<Party>(id, cancellationToken) ?? throw new WorkspaceValidationException("That party no longer exists.");
        authorizer.Demand(ViewPermission(party.Kind));
        var balance = (await unit.ListAsync<LedgerEntry>(e => e.AccountId == party.AccountId, cancellationToken)).Sum(e => e.Debit - e.Credit);
        return Row(party, balance, await OpenItems.LoadAsync(unit, id, cancellationToken));
    }

    /// <summary>Every live item of the party, oldest first, with its open amount.</summary>
    public async Task<IReadOnlyList<OpenItemRow>> OpenItemsAsync(Guid partyId, bool openOnly = false, CancellationToken cancellationToken = default)
    {
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var party = await unit.FindAsync<Party>(partyId, cancellationToken) ?? throw new WorkspaceValidationException("That party no longer exists.");
        authorizer.Demand(ViewPermission(party.Kind));
        return (await OpenItems.LoadAsync(unit, partyId, cancellationToken))
            .Where(i => !openOnly || i.Open != 0)
            .OrderBy(i => i.Date).ThenBy(i => i.Number, StringComparer.Ordinal)
            .Select(i => new OpenItemRow(i.Source, i.Id, i.Type, i.Number, i.Date, i.IsCharge, i.Amount, i.Allocated, i.Reference))
            .ToList();
    }

    public async Task<Guid> SaveAsync(PartyInput input, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(CreatePermission(input.Kind));
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var name = (input.Details.Name ?? string.Empty).Trim();
        var selfId = input.Id ?? Guid.Empty;
        if (await unit.AnyAsync<Party>(p => p.Kind == input.Kind && p.Name == name && p.Id != selfId, cancellationToken))
            throw new WorkspaceValidationException($"A {Noun(input.Kind)} named \"{name}\" already exists.");

        Party party;
        try
        {
            if (input.Id is { } id)
            {
                party = await unit.FindAsync<Party>(id, cancellationToken) ?? throw new WorkspaceValidationException("That party no longer exists. Refresh and try again.");
                if (party.Kind != input.Kind) throw new WorkspaceValidationException("A customer cannot become a supplier. Add the supplier separately.");
                party.Update(input.Details);
                var account = await unit.FindAsync<Account>(party.AccountId, cancellationToken)
                    ?? throw new WorkspaceValidationException("The party's ledger account is missing.");
                var ledgerName = await LedgerNameAsync(unit, party.Name, party.Kind, account.Id, cancellationToken);
                if (account.Name != ledgerName) account.Rename(account.Code, ledgerName);
            }
            else
            {
                var details = input.Details;
                // Validate before creating the ledger, so a bad GSTIN does not leave a stray account behind.
                _ = new Party(Guid.NewGuid(), session.CompanyId, input.Kind, Guid.NewGuid(), details);
                var group = await GroupAsync(unit, input.Kind, cancellationToken);
                var code = await NextCodeAsync(unit, group, cancellationToken);
                var ledgerName = await LedgerNameAsync(unit, name, input.Kind, Guid.Empty, cancellationToken);
                var account = new Account(Guid.NewGuid(), session.CompanyId, code, ledgerName, group.Nature,
                    input.Kind == PartyKind.Customer ? AccountKind.Receivable : AccountKind.Payable, group.Id, null);
                unit.Add(account);
                party = new Party(Guid.NewGuid(), session.CompanyId, input.Kind, account.Id, details);
                unit.Add(party);
            }
        }
        catch (DomainException ex)
        {
            throw new WorkspaceValidationException(ex.Message);
        }

        await unit.CommitAsync(cancellationToken);
        return party.Id;
    }

    public async Task SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var party = await unit.FindAsync<Party>(id, cancellationToken) ?? throw new WorkspaceValidationException("That party no longer exists.");
        authorizer.Demand(CreatePermission(party.Kind));
        party.SetActive(active);
        await unit.CommitAsync(cancellationToken);
    }

    internal static string ViewPermission(PartyKind kind) => kind == PartyKind.Customer ? Permissions.SalesView : Permissions.PurchasesView;

    internal static string CreatePermission(PartyKind kind) => kind == PartyKind.Customer ? Permissions.SalesCreate : Permissions.PurchasesCreate;

    internal static string CancelPermission(PartyKind kind) => kind == PartyKind.Customer ? Permissions.SalesCancel : Permissions.PurchasesCancel;

    internal static string Noun(PartyKind kind) => kind == PartyKind.Customer ? "customer" : "supplier";

    private static PartyRow Row(Party p, decimal debitBalance, IEnumerable<OpenItems.Item> items)
    {
        var list = items.ToList();
        var balance = p.Kind == PartyKind.Customer ? debitBalance : -debitBalance;
        return new PartyRow(p.Id, p.Kind, p.Name, p.Phone, p.Email, p.Gstin, p.StateCode, IndianState.FromCode(p.StateCode)?.Name ?? string.Empty,
            p.Address, p.City, p.Pincode, p.CreditLimit, p.IsActive, p.AccountId, balance,
            list.Where(i => i.IsCharge).Sum(i => i.Open), list.Where(i => !i.IsCharge).Sum(i => i.Open));
    }

    private async Task<Account> GroupAsync(IUnitOfWork unit, PartyKind kind, CancellationToken cancellationToken)
    {
        var id = await ledger.AccountAsync(unit, kind == PartyKind.Customer ? LedgerAccountRole.SundryDebtors : LedgerAccountRole.SundryCreditors, cancellationToken);
        return await unit.FindAsync<Account>(id, cancellationToken) ?? throw new WorkspaceValidationException("The party group is missing from the chart of accounts.");
    }

    /// <summary>Party ledgers are numbered under their group: 1130-0001, 1130-0002, and so on.</summary>
    private static async Task<string> NextCodeAsync(IUnitOfWork unit, Account group, CancellationToken cancellationToken)
    {
        var prefix = group.Code + "-";
        var used = (await unit.ListAsync<Account>(a => a.Code.StartsWith(prefix), cancellationToken)).Select(a => a.Code).ToHashSet(StringComparer.Ordinal);
        for (var n = 1; n < 100_000; n++)
        {
            var code = prefix + n.ToString(n < 10_000 ? "0000" : "00000", System.Globalization.CultureInfo.InvariantCulture);
            if (!used.Contains(code) && !await unit.AnyAsync<Account>(a => a.Code == code, cancellationToken)) return code;
        }

        throw new WorkspaceValidationException("No free ledger code was found for the party.");
    }

    /// <summary>Account names are unique in a company, so a customer and a supplier with the same name get a suffix.</summary>
    private static async Task<string> LedgerNameAsync(IUnitOfWork unit, string name, PartyKind kind, Guid selfAccountId, CancellationToken cancellationToken)
    {
        if (!await unit.AnyAsync<Account>(a => a.Name == name && a.Id != selfAccountId, cancellationToken)) return name;
        var suffixed = $"{name} ({(kind == PartyKind.Customer ? "Customer" : "Supplier")})";
        if (suffixed.Length <= 120 && !await unit.AnyAsync<Account>(a => a.Name == suffixed && a.Id != selfAccountId, cancellationToken)) return suffixed;
        throw new WorkspaceValidationException($"An account named \"{name}\" already exists. Choose a different name.");
    }
}

/// <summary>
/// The open-item view of parties: posted credit-mode documents and settlements that are not cancelled or reversed,
/// and the allocations between them where both sides are still live. Everything is derived from append-only rows.
/// </summary>
internal static class OpenItems
{
    internal sealed record Item(OpenItemSource Source, Guid Id, Guid PartyId, string Type, string Number, DateOnly Date, bool IsCharge,
        decimal Amount, string Reference)
    {
        public decimal Allocated { get; set; }

        public decimal Open => Amount - Allocated;
    }

    public static async Task<List<Item>> LoadAsync(IUnitOfWork unit, Guid? partyId, CancellationToken cancellationToken)
    {
        var documents = await unit.ListAsync<BillingDocument>(d => d.Status == BillingStatus.Posted && d.Mode == SettlementMode.Credit
            && (partyId == null || d.PartyId == partyId), cancellationToken);
        var cancelled = (await unit.ListAsync<BillingCancellation>(cancellationToken: cancellationToken)).Select(c => c.DocumentId).ToHashSet();
        var settlements = await unit.ListAsync<Settlement>(s => partyId == null || s.PartyId == partyId, cancellationToken);
        var reversed = (await unit.ListAsync<SettlementReversal>(cancellationToken: cancellationToken)).Select(r => r.SettlementId).ToHashSet();

        var items = documents.Where(d => !cancelled.Contains(d.Id) && d.PartyId is not null)
            .Select(d => new Item(OpenItemSource.Document, d.Id, d.PartyId!.Value, d.Kind.Label(), d.Number!, d.Date, d.Kind.IsCharge(), d.Total, d.Reference))
            .Concat(settlements.Where(s => !reversed.Contains(s.Id))
                .Select(s => new Item(OpenItemSource.Settlement, s.Id, s.PartyId, s.Kind.Label(), s.Number, s.Date, s.Kind.IsCharge(), s.Amount, s.Reference)))
            .ToList();
        var byId = items.ToDictionary(i => i.Id);
        foreach (var allocation in await unit.ListAsync<Allocation>(a => partyId == null || a.PartyId == partyId, cancellationToken))
        {
            if (!byId.TryGetValue(allocation.ChargeId, out var charge) || !byId.TryGetValue(allocation.CreditId, out var credit)) continue;
            charge.Allocated += allocation.Amount;
            credit.Allocated += allocation.Amount;
        }

        return items;
    }

    /// <summary>Allocations that still count (both sides live) touching <paramref name="itemId"/>.</summary>
    public static async Task<List<Allocation>> LiveAllocationsAsync(IUnitOfWork unit, Guid partyId, Guid itemId, CancellationToken cancellationToken)
    {
        var live = (await LoadAsync(unit, partyId, cancellationToken)).Select(i => i.Id).ToHashSet();
        return (await unit.ListAsync<Allocation>(a => a.PartyId == partyId && (a.ChargeId == itemId || a.CreditId == itemId), cancellationToken))
            .Where(a => live.Contains(a.ChargeId) && live.Contains(a.CreditId))
            .ToList();
    }
}
