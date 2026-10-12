using Nexora.Application.Abstractions;
using Nexora.Domain.Accounting;
using Nexora.Domain.Billing;

namespace Nexora.Reporting.Reports.Generators;

/// <summary>
/// Open documents of customers or suppliers on a date: posted credit documents and settlements not cancelled or
/// reversed by then, less allocations made by then. Amounts are in the party's usual direction (owed to us for
/// customers, owed by us for suppliers).
/// </summary>
internal static class OpenItemsAsOf
{
    internal sealed record Item(Guid Id, Guid PartyId, DateOnly Date, string Number, bool IsCharge, decimal Open, DrillTarget Drill);

    internal sealed record PartyInfo(Guid Id, string Name, string Phone, decimal CreditLimit, Guid AccountId, decimal Balance);

    public static async Task<(List<PartyInfo> Parties, List<Item> Items)> LoadAsync(IUnitOfWork unit, PartyKind kind, DateOnly asOf, CancellationToken cancellationToken)
    {
        var sales = kind == PartyKind.Customer;
        var documentKinds = sales ? BillingFacts.Sales : BillingFacts.Purchases;
        SettlementKind[] settlementKinds = sales ? [SettlementKind.CustomerReceipt, SettlementKind.CustomerRefund] : [SettlementKind.SupplierPayment, SettlementKind.SupplierRefund];

        var parties = await unit.QueryAsync(q => q.Set<Party>().Where(p => p.Kind == kind).Select(p => new { p.Id, p.Name, p.Phone, p.CreditLimit, p.AccountId }), cancellationToken);
        var accountIds = parties.Select(p => p.AccountId).ToList();
        var balances = (await unit.QueryAsync(q => q.Set<LedgerEntry>().Where(e => accountIds.Contains(e.AccountId) && e.Date <= asOf).GroupBy(e => e.AccountId)
            .Select(g => new { AccountId = g.Key, Net = g.Sum(e => e.Debit - e.Credit) }), cancellationToken)).ToDictionary(b => b.AccountId, b => b.Net);

        var documents = await unit.QueryAsync(q => q.Set<BillingDocument>()
            .Where(d => d.Status == BillingStatus.Posted && d.Mode == SettlementMode.Credit && d.PartyId != null && documentKinds.Contains(d.Kind) && d.Date <= asOf
                && !q.Set<BillingCancellation>().Any(c => c.DocumentId == d.Id && c.Date <= asOf))
            .Select(d => new { d.Id, PartyId = d.PartyId!.Value, d.Date, d.Number, d.Kind, d.Total }), cancellationToken);
        var settlements = await unit.QueryAsync(q => q.Set<Settlement>()
            .Where(s => settlementKinds.Contains(s.Kind) && s.Date <= asOf && !q.Set<SettlementReversal>().Any(r => r.SettlementId == s.Id && r.Date <= asOf))
            .Select(s => new { s.Id, s.PartyId, s.Date, s.Number, s.Kind, s.Amount }), cancellationToken);
        var partyIds = parties.Select(p => p.Id).ToList();
        var allocations = await unit.QueryAsync(q => q.Set<Allocation>().Where(a => a.Date <= asOf && partyIds.Contains(a.PartyId))
            .Select(a => new { a.ChargeId, a.CreditId, a.Amount }), cancellationToken);

        var items = documents.Select(d => (d.Id, d.PartyId, d.Date, Number: d.Number ?? "", IsCharge: d.Kind.IsCharge(), Amount: d.Total, Drill: new DrillTarget(DrillKind.BillingDocument, d.Id)))
            .Concat(settlements.Select(s => (s.Id, s.PartyId, s.Date, s.Number, IsCharge: s.Kind.IsCharge(), Amount: s.Amount, Drill: new DrillTarget(DrillKind.Settlement, s.Id))))
            .ToList();
        var allocated = new Dictionary<Guid, decimal>();
        var live = items.Select(i => i.Id).ToHashSet();
        foreach (var a in allocations.Where(a => live.Contains(a.ChargeId) && live.Contains(a.CreditId)))
        {
            allocated[a.ChargeId] = allocated.GetValueOrDefault(a.ChargeId) + a.Amount;
            allocated[a.CreditId] = allocated.GetValueOrDefault(a.CreditId) + a.Amount;
        }

        var open = items.Select(i => new Item(i.Id, i.PartyId, i.Date, i.Number, i.IsCharge, i.Amount - allocated.GetValueOrDefault(i.Id), i.Drill)).Where(i => i.Open != 0).ToList();
        // Customers' balances are debits; suppliers' are credits. Both are shown as positive amounts owed.
        var direction = sales ? 1 : -1;
        var infos = parties.Select(p => new PartyInfo(p.Id, p.Name, p.Phone, p.CreditLimit, p.AccountId, direction * balances.GetValueOrDefault(p.AccountId))).ToList();
        return (infos, open);
    }

    /// <summary>Balance of the Sundry Debtors or Sundry Creditors group (every ledger under it) on the date, in the party direction.</summary>
    public static async Task<decimal> ControlBalanceAsync(IUnitOfWork unit, PartyKind kind, DateOnly asOf, CancellationToken cancellationToken)
    {
        var accounts = await Lookups.AccountsAsync(unit, cancellationToken);
        var groupKey = kind == PartyKind.Customer ? "sundry-debtors" : "sundry-creditors";
        var group = accounts.Values.Single(a => a.SystemKey == groupKey);
        var ids = accounts.Values.Where(a => !a.IsGroup && Lookups.Under(a, group.Id, accounts)).Select(a => a.Id).ToList();
        var net = (await unit.QueryAsync(q => q.Set<LedgerEntry>().Where(e => ids.Contains(e.AccountId) && e.Date <= asOf).GroupBy(e => 1)
            .Select(g => g.Sum(e => e.Debit - e.Credit)), cancellationToken)).SingleOrDefault();
        return kind == PartyKind.Customer ? net : -net;
    }
}

/// <summary>Receivables or payables by party on a date.</summary>
internal sealed class BalancesGenerator(string key, PartyKind kind) : Generator(key)
{
    public override async Task<ReportData> RunAsync(ReportContext c, CancellationToken cancellationToken)
    {
        var (parties, items) = await OpenItemsAsOf.LoadAsync(c.Unit, kind, c.AsOf, cancellationToken);
        var byParty = items.ToLookup(i => i.PartyId);
        var rows = parties.Select(p =>
        {
            var charges = byParty[p.Id].Where(i => i.IsCharge).Sum(i => i.Open);
            var credits = byParty[p.Id].Where(i => !i.IsCharge).Sum(i => i.Open);
            return (p, charges, credits);
        }).Where(x => x.p.Balance != 0 || x.charges != 0 || x.credits != 0).OrderByDescending(x => x.p.Balance).Select(x =>
            new ReportRow([x.p.Name, x.p.Phone, x.charges, -x.credits, x.p.Balance - (x.charges - x.credits), x.p.Balance, x.p.CreditLimit],
                new DrillTarget(DrillKind.Party, x.p.Id))).ToList();
        var control = await OpenItemsAsOf.ControlBalanceAsync(c.Unit, kind, c.AsOf, cancellationToken);
        var partyTotal = parties.Sum(p => p.Balance);
        var notes = new List<string>();
        if (control != partyTotal)
            notes.Add($"Ledgers under {(kind == PartyKind.Customer ? "Sundry Debtors" : "Sundry Creditors")} that are not {(kind == PartyKind.Customer ? "customers" : "suppliers")} hold {control - partyTotal:N2}; the group's balance is {control:N2}.");
        return new ReportData(
        [
            Text("party", kind == PartyKind.Customer ? "Customer" : "Supplier"), Text("phone", "Phone"), Money("open", "Open documents"),
            Money("unapplied", "Unapplied credits"), Money("other", "Other ledger postings"), Money("balance", "Balance"), Money("limit", "Credit limit", total: false),
        ], rows, c.AsOfText, Notes: notes);
    }
}

/// <summary>Open documents by age on a date (age = days since the document date). The total equals the parties' ledger balances.</summary>
internal sealed class AgeingGenerator(string key, PartyKind kind) : Generator(key)
{
    public override async Task<ReportData> RunAsync(ReportContext c, CancellationToken cancellationToken)
    {
        var asOf = c.AsOf;
        var buckets = c.Settings.Buckets;
        var (parties, items) = await OpenItemsAsOf.LoadAsync(c.Unit, kind, asOf, cancellationToken);
        var byParty = items.ToLookup(i => i.PartyId);
        var rows = new List<ReportRow>();
        foreach (var p in parties.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var aged = new decimal[buckets.Count + 1];
            foreach (var item in byParty[p.Id].Where(i => i.IsCharge)) aged[Bucket(asOf.DayNumber - item.Date.DayNumber, buckets)] += item.Open;
            var credits = byParty[p.Id].Where(i => !i.IsCharge).Sum(i => i.Open);
            var other = p.Balance - (aged.Sum() - credits);
            if (aged.All(a => a == 0) && credits == 0 && p.Balance == 0) continue;
            rows.Add(new ReportRow([p.Name, .. aged.Cast<object?>(), -credits, other, p.Balance], new DrillTarget(DrillKind.Party, p.Id)));
        }

        var columns = new List<ReportColumn> { Text("party", kind == PartyKind.Customer ? "Customer" : "Supplier") };
        for (var i = 0; i <= buckets.Count; i++)
            columns.Add(Money($"b{i}", i == 0 ? $"0–{buckets[0]} days" : i < buckets.Count ? $"{buckets[i - 1] + 1}–{buckets[i]} days" : $"Over {buckets[^1]} days"));
        columns.Add(Money("unapplied", "Unapplied credits"));
        columns.Add(Money("other", "Other ledger postings"));
        columns.Add(Money("balance", "Balance"));
        return new ReportData(columns, rows, c.AsOfText,
            Notes: ["Age is counted from the document date (QIK Suite has no due dates). Unapplied credits are receipts, payments or returns not yet set against a document."]);
    }

    /// <summary>Index of the ageing column for an age in days: up to the first boundary is column 0, beyond the last is the final column.</summary>
    internal static int Bucket(int days, IReadOnlyList<int> boundaries)
    {
        for (var i = 0; i < boundaries.Count; i++)
            if (days <= boundaries[i]) return i;
        return boundaries.Count;
    }
}
