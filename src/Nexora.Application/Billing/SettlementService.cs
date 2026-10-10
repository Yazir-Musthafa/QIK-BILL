using Nexora.Application.Abstractions;
using Nexora.Application.Security;
using Nexora.Domain.Accounting;
using Nexora.Domain.Audit;
using Nexora.Domain.Billing;
using Nexora.Domain.Common;
using Nexora.Domain.Companies;

namespace Nexora.Application.Billing;

/// <summary>Part of a settlement applied to one open item (an invoice, bill, return or earlier settlement).</summary>
public sealed record AllocationInput(Guid ItemId, decimal Amount);

public sealed record SettlementInput(SettlementKind Kind, DateOnly Date, Guid PartyId, Guid AccountId, decimal Amount, PaymentMethod Method,
    IReadOnlyList<AllocationInput> Allocations, string Reference = "", string Narration = "");

public sealed record SettlementRow(Guid Id, SettlementKind Kind, string Number, DateOnly Date, Guid PartyId, string PartyName, string Account, decimal Amount,
    decimal Allocated, PaymentMethod Method, string Reference, bool IsReversed)
{
    public string KindLabel => Kind.Label();

    public decimal Unapplied => IsReversed ? 0 : Amount - Allocated;
}

/// <summary>A settlement with what it was applied to, for viewing and printing.</summary>
public sealed record SettlementView(Guid Id, SettlementKind Kind, string Number, DateOnly Date, Guid PartyId, string PartyName, string PartyGstin,
    string Account, decimal Amount, PaymentMethod Method, string Reference, string Narration, bool IsReversed, string? ReversalReason,
    IReadOnlyList<AllocationView> Allocations, decimal Unapplied)
{
    public string KindLabel => Kind.Label();
}

/// <summary>
/// Customer receipts and refunds, supplier payments and refunds, and allocations between a party's open items.
/// A settlement posts its voucher (cash or bank against the party ledger) and its allocations in one transaction.
/// Unallocated amounts stay as advances and can be applied later. A settlement is undone only by reversing it.
/// </summary>
public sealed class SettlementService(IUnitOfWorkFactory units, IAuthorizer authorizer, ILedgerPoster ledger, IClock clock)
{
    internal const string SourceType = "settlement";

    public async Task<(Guid Id, string Number)> PostAsync(SettlementInput input, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(PartyService.CreatePermission(input.Kind.PartyKind()));
        if (input.Amount <= 0 || !Money.HasValidScale(input.Amount) || input.Amount > Money.MaxAmount)
            throw new WorkspaceValidationException("Enter an amount more than zero, in rupees and paise.");
        if (input.Allocations.Count > 500) throw new WorkspaceValidationException("A settlement can be allocated to at most 500 documents.");
        if (input.Allocations.Sum(a => a.Amount) > input.Amount)
            throw new WorkspaceValidationException($"The allocations ({input.Allocations.Sum(a => a.Amount):N2}) are more than the amount ({input.Amount:N2}).");

        using var lockHandle = await ledger.AcquireAsync(cancellationToken);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var party = await PartyAsync(unit, input.PartyId, input.Kind.PartyKind(), cancellationToken);
        if (!party.IsActive) throw new WorkspaceValidationException($"{party.Name} is inactive.");
        var account = await unit.FindAsync<Account>(input.AccountId, cancellationToken) ?? throw new WorkspaceValidationException("Choose the cash or bank account.");
        if (!account.IsCashOrBank || account.IsGroup || !account.IsActive) throw new WorkspaceValidationException("Choose an active cash or bank account.");

        var id = Guid.NewGuid();
        var year = FinancialYear.Containing(input.Date);
        var kind = input.Kind;
        var number = await NumberAllocator.NextAsync(unit, session.CompanyId, kind.NumberingKey(), year,
            candidate => unit.AnyAsync<Settlement>(s => s.Kind == kind && s.Number == candidate, cancellationToken), cancellationToken);

        var amount = input.Amount;
        var lines = kind.IsMoneyIn()
            ? new List<LedgerPostingLine> { new(account.Id, amount, 0), new(party.AccountId, 0, amount) }
            : [new(party.AccountId, amount, 0), new(account.Id, 0, amount)];
        var narration = $"{kind.Label()} {number} · {party.Name}" + (input.Reference.Trim().Length > 0 ? $" · {input.Reference.Trim()}" : string.Empty);
        var voucherId = await ledger.PostAsync(unit, new LedgerPosting(input.Date, narration, SourceType, id, lines), cancellationToken);

        Settlement settlement;
        try
        {
            settlement = new Settlement(id, session.CompanyId, kind, number, input.Date, party.Id, account.Id, amount, input.Method, input.Reference,
                input.Narration, voucherId, clock.UtcNow, session.UserId);
        }
        catch (DomainException ex)
        {
            throw new WorkspaceValidationException(ex.Message);
        }

        unit.Add(settlement);
        var items = await OpenItems.LoadAsync(unit, party.Id, cancellationToken);
        var self = new OpenItems.Item(OpenItemSource.Settlement, id, party.Id, kind.Label(), number, input.Date, kind.IsCharge(), amount, settlement.Reference);
        items.Add(self);
        foreach (var allocation in input.Allocations.Where(a => a.Amount != 0))
            Allocate(unit, session, party, items, self, allocation.ItemId, allocation.Amount, input.Date);

        unit.Record(new AuditEvent("Posted", nameof(Settlement), id.ToString(),
            $"Posted {kind.Label().ToLowerInvariant()} {number} of {amount:N2} ({party.Name}), allocated {self.Allocated:N2}"));
        await unit.CommitAsync(cancellationToken);
        return (id, number);
    }

    /// <summary>Applies an open credit (receipt, payment, return) to an open charge (invoice, bill, refund) of the same party.</summary>
    public async Task AllocateAsync(Guid partyId, Guid chargeId, Guid creditId, decimal amount, DateOnly date, CancellationToken cancellationToken = default)
    {
        using var lockHandle = await ledger.AcquireAsync(cancellationToken);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var party = await unit.FindAsync<Party>(partyId, cancellationToken) ?? throw new WorkspaceValidationException("That party no longer exists.");
        var session = authorizer.Demand(PartyService.CreatePermission(party.Kind));
        var items = await OpenItems.LoadAsync(unit, partyId, cancellationToken);
        var charge = items.SingleOrDefault(i => i.Id == chargeId) ?? throw new WorkspaceValidationException("That document is not open for this party.");
        if (!charge.IsCharge) throw new WorkspaceValidationException($"{charge.Number} is not an invoice, bill or refund.");
        Allocate(unit, session, party, items, charge, creditId, amount, date);
        unit.Record(new AuditEvent("Allocated", nameof(Allocation), chargeId.ToString(), $"Allocated {amount:N2} to {charge.Number} ({party.Name})"));
        await unit.CommitAsync(cancellationToken);
    }

    /// <summary>Reverses a settlement and its voucher. Its allocations stop counting, so the documents it paid are open again.</summary>
    public async Task ReverseAsync(Guid id, DateOnly date, string reason, CancellationToken cancellationToken = default)
    {
        using var lockHandle = await ledger.AcquireAsync(cancellationToken);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var settlement = await unit.FindAsync<Settlement>(id, cancellationToken) ?? throw new WorkspaceValidationException("That receipt or payment no longer exists.");
        var session = authorizer.Demand(PartyService.CancelPermission(settlement.Kind.PartyKind()));
        if (await unit.AnyAsync<SettlementReversal>(r => r.SettlementId == id, cancellationToken))
            throw new WorkspaceValidationException($"{settlement.Kind.Label()} {settlement.Number} is already reversed.");
        if (date < settlement.Date) throw new WorkspaceValidationException("A reversal cannot be dated before the receipt or payment.");
        var reversalVoucher = await ledger.ReverseAsync(unit, settlement.VoucherId, date, $"Reversal of {settlement.Kind.Label().ToLowerInvariant()} {settlement.Number}", cancellationToken);
        try
        {
            unit.Add(new SettlementReversal(Guid.NewGuid(), session.CompanyId, id, date, reason, reversalVoucher, clock.UtcNow, session.UserId));
        }
        catch (DomainException ex)
        {
            throw new WorkspaceValidationException(ex.Message);
        }

        unit.Record(new AuditEvent("Reversed", nameof(Settlement), id.ToString(), $"Reversed {settlement.Kind.Label().ToLowerInvariant()} {settlement.Number}: {reason.Trim()}"));
        await unit.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SettlementRow>> ListAsync(PartyKind partyKind, string? search = null, Guid? partyId = null, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(PartyService.ViewPermission(partyKind));
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var kinds = Enum.GetValues<SettlementKind>().Where(k => k.PartyKind() == partyKind).ToList();
        var settlements = await unit.ListAsync<Settlement>(s => kinds.Contains(s.Kind) && (partyId == null || s.PartyId == partyId), cancellationToken);
        var parties = (await unit.ListAsync<Party>(p => p.Kind == partyKind, cancellationToken)).ToDictionary(p => p.Id, p => p.Name);
        var accounts = (await unit.ListAsync<Account>(a => a.Kind == AccountKind.Cash || a.Kind == AccountKind.Bank, cancellationToken)).ToDictionary(a => a.Id, a => a.Name);
        var reversed = (await unit.ListAsync<SettlementReversal>(cancellationToken: cancellationToken)).Select(r => r.SettlementId).ToHashSet();
        var allocated = (await OpenItems.LoadAsync(unit, partyId, cancellationToken)).ToDictionary(i => i.Id, i => i.Allocated);
        var text = (search ?? string.Empty).Trim();
        return settlements
            .Select(s => new SettlementRow(s.Id, s.Kind, s.Number, s.Date, s.PartyId, parties.GetValueOrDefault(s.PartyId, "?"), accounts.GetValueOrDefault(s.AccountId, "?"),
                s.Amount, allocated.GetValueOrDefault(s.Id), s.Method, s.Reference, reversed.Contains(s.Id)))
            .Where(r => text.Length == 0 || r.Number.Contains(text, StringComparison.OrdinalIgnoreCase) || r.PartyName.Contains(text, StringComparison.OrdinalIgnoreCase)
                || r.Reference.Contains(text, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Date).ThenByDescending(r => r.Number, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<SettlementView> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var s = await unit.FindAsync<Settlement>(id, cancellationToken) ?? throw new WorkspaceValidationException("That receipt or payment no longer exists.");
        authorizer.Demand(PartyService.ViewPermission(s.Kind.PartyKind()));
        var party = await unit.FindAsync<Party>(s.PartyId, cancellationToken);
        var account = await unit.FindAsync<Account>(s.AccountId, cancellationToken);
        var reversal = (await unit.ListAsync<SettlementReversal>(r => r.SettlementId == id, cancellationToken)).SingleOrDefault();
        var items = (await OpenItems.LoadAsync(unit, s.PartyId, cancellationToken)).ToDictionary(i => i.Id);
        var allocations = new List<AllocationView>();
        if (reversal is null)
        {
            foreach (var a in await OpenItems.LiveAllocationsAsync(unit, s.PartyId, id, cancellationToken))
            {
                var other = items[a.ChargeId == id ? a.CreditId : a.ChargeId];
                allocations.Add(new AllocationView(other.Id, other.Type, other.Number, a.Date, a.Amount));
            }
        }

        return new SettlementView(s.Id, s.Kind, s.Number, s.Date, s.PartyId, party?.Name ?? "?", party?.Gstin ?? string.Empty, account?.Name ?? "?", s.Amount,
            s.Method, s.Reference, s.Narration, reversal is not null, reversal?.Reason, allocations, reversal is null ? s.Amount - allocations.Sum(a => a.Amount) : 0);
    }

    /// <summary>Cash and bank ledgers that can receive or pay money.</summary>
    public async Task<IReadOnlyList<(Guid Id, string Name)>> CashAndBankAccountsAsync(CancellationToken cancellationToken = default)
    {
        authorizer.RequireSession();
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        return (await unit.ListAsync<Account>(a => (a.Kind == AccountKind.Cash || a.Kind == AccountKind.Bank) && a.IsActive, cancellationToken))
            .OrderBy(a => a.Code, StringComparer.Ordinal).Select(a => (a.Id, a.Name)).ToList();
    }

    /// <summary>Validates and adds one allocation between <paramref name="self"/> and another open item of the party.</summary>
    private void Allocate(IUnitOfWork unit, SessionInfo session, Party party, List<OpenItems.Item> items, OpenItems.Item self, Guid otherId, decimal amount, DateOnly date)
    {
        if (amount <= 0 || !Money.HasValidScale(amount)) throw new WorkspaceValidationException("Allocate an amount more than zero, in rupees and paise.");
        var other = items.SingleOrDefault(i => i.Id == otherId)
            ?? throw new WorkspaceValidationException("That document is not open for this party (it may be cancelled, reversed, paid in cash or for another party).");
        if (other.IsCharge == self.IsCharge)
            throw new WorkspaceValidationException(self.IsCharge
                ? $"{other.Number} cannot settle {self.Number}: apply a receipt, payment or return."
                : $"{self.Number} can only be allocated to invoices, bills or refunds, not to {other.Number}.");
        if (amount > self.Open) throw new WorkspaceValidationException($"Only {self.Open:N2} of {self.Number} is unallocated.");
        if (amount > other.Open) throw new WorkspaceValidationException($"Only {other.Open:N2} of {other.Number} is outstanding.");
        var (charge, credit) = self.IsCharge ? (self, other) : (other, self);
        var on = date < charge.Date || date < credit.Date ? (charge.Date > credit.Date ? charge.Date : credit.Date) : date;
        unit.Add(new Allocation(Guid.NewGuid(), session.CompanyId, party.Id, charge.Source, charge.Id, credit.Source, credit.Id, amount, on, clock.UtcNow, session.UserId));
        charge.Allocated += amount;
        credit.Allocated += amount;
    }

    private static async Task<Party> PartyAsync(IUnitOfWork unit, Guid id, PartyKind kind, CancellationToken cancellationToken)
    {
        var party = await unit.FindAsync<Party>(id, cancellationToken) ?? throw new WorkspaceValidationException("Choose the party.");
        if (party.Kind != kind) throw new WorkspaceValidationException($"Choose a {PartyService.Noun(kind)}.");
        return party;
    }
}
