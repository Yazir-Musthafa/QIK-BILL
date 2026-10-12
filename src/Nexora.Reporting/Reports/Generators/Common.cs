using System.Globalization;
using Nexora.Application;
using Nexora.Application.Abstractions;
using Nexora.Domain.Accounting;
using Nexora.Domain.Billing;
using Nexora.Domain.Users;

namespace Nexora.Reporting.Reports.Generators;

/// <summary>Base for generators: a key plus shared query helpers.</summary>
internal abstract class Generator(string key) : IReportGenerator, IReportGeneratorMarker
{
    public string Key { get; } = key;

    public abstract Task<ReportData> RunAsync(ReportContext context, CancellationToken cancellationToken);

    protected static ReportColumn Text(string key, string header) => new(key, header, ColumnKind.Text);

    protected static ReportColumn Date(string key, string header) => new(key, header, ColumnKind.Date);

    protected static ReportColumn Money(string key, string header, bool total = true, bool cost = false) => new(key, header, ColumnKind.Amount, total, cost);

    protected static ReportColumn Qty(string key, string header, bool total = false) => new(key, header, ColumnKind.Quantity, total);

    protected static ReportColumn Count(string key, string header, bool total = true) => new(key, header, ColumnKind.Integer, total);

    protected static ReportColumn Percent(string key, string header, bool cost = false) => new(key, header, ColumnKind.Percent, false, cost);

    protected static decimal Margin(decimal value, decimal cost) => value == 0 ? 0 : Math.Round((value - cost) * 100 / value, 2, MidpointRounding.AwayFromZero);
}

/// <summary>Live billing figures as the ledger sees them: a document counts on its date; a cancellation counts negatively on its own date.</summary>
internal static class BillingFacts
{
    /// <summary>Member-initialised (not a constructor call) so the database can union and page it.</summary>
    internal sealed class DocumentFact
    {
        public Guid Id { get; init; }
        public DateOnly Date { get; init; }
        public string? Number { get; init; }
        public BillingDocumentKind Kind { get; init; }
        public Guid? PartyId { get; init; }
        public string PartyName { get; init; } = "";
        public string Gstin { get; init; } = "";
        public string PlaceOfSupply { get; init; } = "";
        public decimal Taxable { get; init; }
        public decimal Cgst { get; init; }
        public decimal Sgst { get; init; }
        public decimal Igst { get; init; }
        public decimal RoundOff { get; init; }
        public decimal Total { get; init; }
        public decimal Cost { get; init; }
        public Guid? PostedBy { get; init; }
        public bool IsCancellation { get; init; }
    }

    /// <summary>Sign of a document's figures in "sales" (or "purchases") terms: invoices and bills count up, returns down, cancellations reverse.</summary>
    public static int Sign(BillingDocumentKind kind, bool isCancellation) => (kind.IsCharge() ? 1 : -1) * (isCancellation ? -1 : 1);

    /// <summary>Posted documents dated in the range plus cancellations dated in the range, as one database query that can be paged.</summary>
    public static IQueryable<DocumentFact> Query(IQuerySource q, BillingDocumentKind[] kinds, DateOnly start, DateOnly to, Guid? partyId = null)
    {
        var documents = q.Set<BillingDocument>().Where(d => d.Status == BillingStatus.Posted && kinds.Contains(d.Kind) && d.Date >= start && d.Date <= to
                && (partyId == null || d.PartyId == partyId))
            .Select(d => new DocumentFact
            {
                Id = d.Id, Date = d.Date, Number = d.Number, Kind = d.Kind, PartyId = d.PartyId, PartyName = d.PartyName, Gstin = d.PartyGstin,
                PlaceOfSupply = d.PlaceOfSupply, Taxable = d.TaxableAmount, Cgst = d.Cgst, Sgst = d.Sgst, Igst = d.Igst, RoundOff = d.RoundOff, Total = d.Total,
                Cost = d.CostOfGoods, PostedBy = d.PostedBy, IsCancellation = false,
            });
        var cancellations =
            from c in q.Set<BillingCancellation>()
            join d in q.Set<BillingDocument>() on c.DocumentId equals d.Id
            where kinds.Contains(d.Kind) && c.Date >= start && c.Date <= to && (partyId == null || d.PartyId == partyId)
            select new DocumentFact
            {
                Id = d.Id, Date = c.Date, Number = d.Number, Kind = d.Kind, PartyId = d.PartyId, PartyName = d.PartyName, Gstin = d.PartyGstin,
                PlaceOfSupply = d.PlaceOfSupply, Taxable = d.TaxableAmount, Cgst = d.Cgst, Sgst = d.Sgst, Igst = d.Igst, RoundOff = d.RoundOff, Total = d.Total,
                Cost = d.CostOfGoods, PostedBy = d.PostedBy, IsCancellation = true,
            };
        return documents.Concat(cancellations);
    }

    public static readonly BillingDocumentKind[] Sales = [BillingDocumentKind.SalesInvoice, BillingDocumentKind.SalesReturn];

    public static readonly BillingDocumentKind[] Purchases = [BillingDocumentKind.PurchaseBill, BillingDocumentKind.PurchaseReturn];
}

internal static class Periods
{
    public static DateOnly Start(DateOnly date, ReportPeriod period) => period switch
    {
        ReportPeriod.Day => date,
        ReportPeriod.Month => new DateOnly(date.Year, date.Month, 1),
        _ => Domain.Companies.FinancialYear.Containing(date).StartDate,
    };

    public static string Label(DateOnly start, ReportPeriod period) => period switch
    {
        ReportPeriod.Day => start.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
        ReportPeriod.Month => start.ToString("MMM yyyy", CultureInfo.InvariantCulture),
        _ => "FY " + Domain.Companies.FinancialYear.Containing(start).Label,
    };
}

internal static class Lookups
{
    public static async Task<Dictionary<Guid, string>> UserNamesAsync(IUnitOfWork unit, CancellationToken cancellationToken) =>
        (await unit.QueryAsync(q => q.Set<User>().Select(u => new { u.Id, u.DisplayName }), cancellationToken)).ToDictionary(u => u.Id, u => u.DisplayName);

    public static async Task<Dictionary<Guid, Account>> AccountsAsync(IUnitOfWork unit, CancellationToken cancellationToken) =>
        (await unit.ListAsync<Account>(cancellationToken: cancellationToken)).ToDictionary(a => a.Id);

    /// <summary>True when the account is the group or sits anywhere below it.</summary>
    public static bool Under(Account account, Guid groupId, IReadOnlyDictionary<Guid, Account> accounts)
    {
        for (Account? a = account; a is not null; a = a.ParentId is { } p && accounts.TryGetValue(p, out var parent) ? parent : null)
            if (a.Id == groupId) return true;
        return false;
    }
}
