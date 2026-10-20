using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Nexora.Application;
using Nexora.Application.Abstractions;
using Nexora.Application.Security;
using Nexora.Domain.Accounting;
using Nexora.Domain.Billing;
using Nexora.Domain.Companies;
using Nexora.Domain.Inventory;
using Nexora.Domain.Security;
using Nexora.Reporting.Export;

namespace Nexora.Reporting.Gst;

public static class GstSupply
{
    public static string Classify(string gstin, bool interState, decimal invoiceTotal, DateOnly invoiceDate) =>
        !string.IsNullOrWhiteSpace(gstin) ? "B2B" : interState && invoiceTotal > LargeThreshold(invoiceDate) ? "B2CL" : "B2CS";

    public static decimal LargeThreshold(DateOnly date) => date >= new DateOnly(2024, 8, 1) ? 100_000m : 250_000m;
}

/// <summary>Only immutable invoice values enter the export, never today's product prices or customer GSTIN.</summary>
public sealed record GstLine(string Hsn, string Unit, decimal Quantity, decimal Rate, decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst);
public sealed record GstInvoice(string Number, DateOnly Date, string Gstin, string PlaceOfSupply, bool InterState, decimal Total,
    bool IsCreditNote, string OriginalCategory, bool Cancelled, IReadOnlyList<GstLine> Lines);

/// <summary>
/// Domestic regular taxable sales upload data. This is not a filing client or a complete return for special supplies.
/// Field shapes follow GSTN Returns Offline Tool 3.2.4; see docs/GST_EXPORT.md for sources and validation limits.
/// </summary>
public sealed partial class Gstr1ExportService(IUnitOfWorkFactory units, IAuthorizer authorizer)
{
    public async Task<ExportFile> ExportAsync(DateOnly from, DateOnly to, bool sixDigitHsn = false, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.ReportsView);
        authorizer.Demand(Permissions.SalesView);
        authorizer.Demand(Permissions.DataExport);
        ValidatePeriod(from, to);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var company = await unit.FindAsync<Company>(session.CompanyId, cancellationToken)
            ?? throw new WorkspaceValidationException("Sign in again.");
        var documents = await unit.ListAsync<BillingDocument>(d => d.Status == BillingStatus.Posted &&
            (d.Kind == BillingDocumentKind.SalesInvoice || d.Kind == BillingDocumentKind.SalesReturn) && d.Date <= to, cancellationToken);
        var byId = documents.ToDictionary(d => d.Id);
        var cancellations = await unit.ListAsync<BillingCancellation>(c => c.Date <= to, cancellationToken);
        var cancelled = cancellations.Select(c => c.DocumentId).ToHashSet();
        // A prior-period invoice may already have been uploaded. A plain cancellation is not a GST amendment.
        var priorCancellation = cancellations.FirstOrDefault(c => c.Date >= from && byId.TryGetValue(c.DocumentId, out var d) && d.Date < from);
        if (priorCancellation is not null)
            throw new WorkspaceValidationException($"{byId[priorCancellation.DocumentId].Number}: a prior-period document was cancelled. Review its GST amendment in the portal before exporting this period.");
        var selected = documents.Where(d => d.Date >= from).OrderBy(d => d.Date).ThenBy(d => d.Number, StringComparer.Ordinal).ToList();
        var ids = selected.Select(d => d.Id).ToArray();
        var lines = (await unit.ListAsync<BillingLine>(l => ids.Contains(l.DocumentId), cancellationToken)).ToLookup(l => l.DocumentId);
        var unitCodes = (await unit.ListAsync<Unit>(cancellationToken: cancellationToken)).ToDictionary(u => u.Id, u => u.Code);
        var invoices = selected.Select(d =>
        {
            var original = d.OriginalDocumentId is { } id && byId.TryGetValue(id, out var source) ? source : d;
            return new GstInvoice(d.Number!, d.Date, d.PartyGstin, d.PlaceOfSupply, d.IsInterState, d.Total, d.Kind.IsReturn(),
                GstSupply.Classify(original.PartyGstin, original.IsInterState, original.Total, original.Date), cancelled.Contains(d.Id),
                lines[d.Id].OrderBy(l => l.LineNumber).Select(l => new GstLine(l.HsnCode,
                    l.UnitId is { } uid ? unitCodes.GetValueOrDefault(uid, "") : "NA", l.Quantity, l.TaxRatePercent,
                    l.TaxableAmount, l.Cgst, l.Sgst, l.Igst)).ToList());
        }).ToList();
        return Build(company.Gstin, from, to, invoices, sixDigitHsn);
    }

    public static ExportFile Build(string gstin, DateOnly from, DateOnly to, IReadOnlyList<GstInvoice> invoices, bool sixDigitHsn = false)
    {
        ValidatePeriod(from, to);
        if (!TaxIdentifiers.IsValidGstin(gstin))
            throw new WorkspaceValidationException("Add your valid GSTIN in Settings → Company profile before exporting GSTR-1.");
        var errors = new List<string>();
        foreach (var d in invoices)
        {
            if (!DocumentNumber().IsMatch(d.Number)) errors.Add($"{d.Number}: GST document numbers need 1–16 letters, digits, / or -.");
            if (d.Date < from || d.Date > to) errors.Add($"{d.Number}: the date is outside the chosen period.");
            if (d.Cancelled) continue;
            if (IndianState.FromCode(d.PlaceOfSupply) is null || d.PlaceOfSupply is "96" or "97") errors.Add($"{d.Number}: select a domestic place of supply.");
            if (d.InterState != (d.PlaceOfSupply != gstin[..2]))
                errors.Add($"{d.Number}: place of supply and the exporting company's GSTIN do not agree with the recorded tax split.");
            if (d.Gstin.Length > 0 && !TaxIdentifiers.IsValidGstin(d.Gstin)) errors.Add($"{d.Number}: customer GSTIN is invalid.");
            if (d.Gstin == gstin) errors.Add($"{d.Number}: supplier and recipient GSTIN cannot be the same.");
            if (d.Lines.Count == 0) errors.Add($"{d.Number}: no invoice lines.");
            foreach (var l in d.Lines)
            {
                if (!(l.Hsn.Length is 4 or 6 or 8) || !l.Hsn.All(char.IsAsciiDigit) || (sixDigitHsn && l.Hsn.Length < 6))
                    errors.Add($"{d.Number}: HSN/SAC {l.Hsn} needs {(sixDigitHsn ? "6 or 8" : "4, 6 or 8")} digits. Correct the source document; posted values are not overwritten.");
                if (Uqc(l.Unit) is null) errors.Add($"{d.Number}: unit {l.Unit} has no GST UQC mapping.");
                if (l.Rate <= 0) errors.Add($"{d.Number}: zero-rate supplies need nil/exempt/non-GST classification. Prepare those sections separately; this export supports taxable domestic sales.");
                if (d.InterState ? l.Cgst != 0 || l.Sgst != 0 : l.Igst != 0)
                    errors.Add($"{d.Number}: tax split and place of supply do not agree.");
            }
            if (Math.Abs(d.Total - d.Lines.Sum(l => l.Taxable + l.Cgst + l.Sgst + l.Igst)) > 0.50m)
                errors.Add($"{d.Number}: invoice total does not reconcile with lines and round-off.");
        }
        if (invoices.GroupBy(d => (d.IsCreditNote, d.Number), StringTupleComparer.Instance).Any(g => g.Count() > 1))
            errors.Add("Duplicate document numbers in this period.");
        if (errors.Count > 0) throw new WorkspaceValidationException(string.Join(Environment.NewLine, errors.Distinct().Take(12)));

        var active = invoices.Where(d => !d.Cancelled).ToList();
        string Category(GstInvoice d) => d.IsCreditNote ? d.OriginalCategory : GstSupply.Classify(d.Gstin, d.InterState, d.Total, d.Date);
        // These are the upload-envelope fields emitted by GSTN Returns Offline Tool 3.2.4.
        // "hash" is its literal format marker, not a signature or a checksum of this file.
        var root = Obj(("gstin", gstin), ("fp", to.ToString("MMyyyy", CultureInfo.InvariantCulture)),
            ("version", "GST3.2.4"), ("hash", "hash"));
        Add("b2b", active.Where(d => !d.IsCreditNote && Category(d) == "B2B").GroupBy(d => d.Gstin)
            .Select(g => Obj(("ctin", g.Key), ("inv", g.Select(d => Invoice(d, true)).ToArray()))));
        Add("b2cl", active.Where(d => !d.IsCreditNote && Category(d) == "B2CL").GroupBy(d => d.PlaceOfSupply)
            .Select(g => Obj(("pos", g.Key), ("inv", g.Select(d => Invoice(d, false)).ToArray()))));
        Add("cdnr", active.Where(d => d.IsCreditNote && Category(d) == "B2B").GroupBy(d => d.Gstin)
            .Select(g => Obj(("ctin", g.Key), ("nt", g.Select(d => Note(d, true)).ToArray()))));
        Add("cdnur", active.Where(d => d.IsCreditNote && Category(d) == "B2CL").Select(d => Note(d, false)));
        var small = active.Where(d => Category(d) == "B2CS").SelectMany(d => d.Lines.Select(l => (Doc: d, Line: l)))
            .GroupBy(x => (x.Doc.PlaceOfSupply, x.Doc.InterState, x.Line.Rate)).Select(g =>
            {
                decimal Sum(Func<GstLine, decimal> f) => Money.Round(g.Sum(x => (x.Doc.IsCreditNote ? -1 : 1) * f(x.Line)));
                var row = Obj(("sply_ty", g.Key.InterState ? "INTER" : "INTRA"), ("typ", "OE"), ("pos", g.Key.PlaceOfSupply),
                    ("rt", g.Key.Rate), ("txval", Sum(l => l.Taxable)), ("csamt", 0m));
                if (g.Key.InterState) row["iamt"] = Sum(l => l.Igst);
                else { row["camt"] = Sum(l => l.Cgst); row["samt"] = Sum(l => l.Sgst); }
                return row;
            });
        Add("b2cs", small);
        if (active.Count > 0)
        {
            var hsn = Obj();
            if (to >= new DateOnly(2025, 5, 1))
            {
                var b2b = Hsn(active.Where(d => d.Gstin.Length > 0));
                var b2c = Hsn(active.Where(d => d.Gstin.Length == 0));
                if (b2b.Length > 0) hsn["hsn_b2b"] = b2b;
                if (b2c.Length > 0) hsn["hsn_b2c"] = b2c;
            }
            else hsn["data"] = Hsn(active);
            root["hsn"] = hsn;
        }
        // One serial range per document is exact even when numbering prefixes changed or gaps exist.
        var docs = invoices.GroupBy(d => d.IsCreditNote).Select(g => Obj(("doc_num", g.Key ? 5 : 1),
            ("docs", g.Select((d, i) => Obj(("num", i + 1), ("from", d.Number), ("to", d.Number), ("totnum", 1),
                ("cancel", d.Cancelled ? 1 : 0), ("net_issue", d.Cancelled ? 0 : 1))).ToArray()))).ToArray();
        if (docs.Length > 0) root["doc_issue"] = Obj(("doc_det", docs));
        if (invoices.Count == 0) throw new WorkspaceValidationException("No posted sales documents in this period. Prepare a nil return in the GST portal if applicable.");
        return new ExportFile($"GSTR1-{gstin}-{to:MMyyyy}.json", "application/json", JsonSerializer.SerializeToUtf8Bytes(root, new JsonSerializerOptions { WriteIndented = true }));

        void Add(string key, IEnumerable<Dictionary<string, object>> rows)
        {
            var values = rows.ToArray();
            if (values.Length > 0) root[key] = values;
        }
    }

    public static void ValidatePeriod(DateOnly from, DateOnly to)
    {
        var monthly = from.Day == 1 && to == from.AddMonths(1).AddDays(-1);
        var quarterly = from.Day == 1 && from.Month is 1 or 4 or 7 or 10 && to == from.AddMonths(3).AddDays(-1);
        if (!monthly && !quarterly) throw new WorkspaceValidationException("GSTR-1: choose one complete calendar month or quarter. The export includes the whole company's period, without report search or customer filters.");
    }

    private static Dictionary<string, object> Invoice(GstInvoice d, bool registered)
    {
        var row = Obj(("inum", d.Number), ("idt", Date(d.Date)), ("val", d.Total), ("itms", Items(d)));
        if (registered) { row["pos"] = d.PlaceOfSupply; row["rchrg"] = "N"; row["inv_typ"] = "R"; }
        return row;
    }

    private static Dictionary<string, object> Note(GstInvoice d, bool registered)
    {
        var row = Obj(("nt_num", d.Number), ("nt_dt", Date(d.Date)), ("ntty", "C"), ("val", d.Total), ("pos", d.PlaceOfSupply), ("itms", Items(d)));
        if (registered) { row["rchrg"] = "N"; row["inv_typ"] = "R"; }
        else row["typ"] = "B2CL";
        return row;
    }

    private static object[] Items(GstInvoice d) => d.Lines.GroupBy(l => l.Rate).OrderBy(g => g.Key).Select((g, i) =>
    {
        var amounts = Obj(("rt", g.Key), ("txval", Money.Round(g.Sum(l => l.Taxable))), ("csamt", 0m));
        if (d.InterState) amounts["iamt"] = Money.Round(g.Sum(l => l.Igst));
        else { amounts["camt"] = Money.Round(g.Sum(l => l.Cgst)); amounts["samt"] = Money.Round(g.Sum(l => l.Sgst)); }
        return (object)Obj(("num", i + 1), ("itm_det", amounts));
    }).ToArray();

    private static object[] Hsn(IEnumerable<GstInvoice> documents) => documents.SelectMany(d => d.Lines.Select(l => (Doc: d, Line: l)))
        .GroupBy(x => (x.Line.Hsn, Unit: Uqc(x.Line.Unit)!, x.Line.Rate)).OrderBy(g => g.Key.Hsn, StringComparer.Ordinal).ThenBy(g => g.Key.Unit, StringComparer.Ordinal).ThenBy(g => g.Key.Rate)
        .Select((g, i) =>
        {
            decimal Sum(Func<GstLine, decimal> f) => Money.Round(g.Sum(x => (x.Doc.IsCreditNote ? -1 : 1) * f(x.Line)));
            return (object)Obj(("num", i + 1), ("hsn_sc", g.Key.Hsn), ("uqc", g.Key.Unit), ("rt", g.Key.Rate),
                ("qty", g.Key.Unit == "NA" ? 0m : g.Sum(x => (x.Doc.IsCreditNote ? -1 : 1) * x.Line.Quantity)),
                ("txval", Sum(l => l.Taxable)), ("iamt", Sum(l => l.Igst)), ("camt", Sum(l => l.Cgst)), ("samt", Sum(l => l.Sgst)), ("csamt", 0m));
        }).ToArray();

    private static string? Uqc(string unit) => unit.ToUpperInvariant() switch
    {
        "KG" => "KGS", "G" => "GMS", "L" => "LTR", "ML" => "MLT", "M" => "MTR", "PACK" => "PAC",
        "BAG" or "BAL" or "BDL" or "BKL" or "BOU" or "BOX" or "BTL" or "BUN" or "CAN" or "CBM" or "CCM" or "CMS" or "CTN" or "DOZ" or "DRM"
        or "GGK" or "GMS" or "GRS" or "GYD" or "KGS" or "KLR" or "KME" or "LTR" or "MLT" or "MTR" or "MTS" or "NOS" or "OTH" or "PAC" or "PCS"
        or "PRS" or "QTL" or "ROL" or "SET" or "SQF" or "SQM" or "SQY" or "TBS" or "TGM" or "THD" or "TON" or "TUB" or "UGS" or "UNT" or "YDS" or "NA" => unit.ToUpperInvariant(),
        _ => null,
    };
    private static Dictionary<string, object> Obj(params (string Key, object Value)[] fields) => fields.ToDictionary(f => f.Key, f => f.Value);
    private static string Date(DateOnly date) => date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
    [GeneratedRegex("^[A-Za-z0-9/-]{1,16}$")]
    private static partial Regex DocumentNumber();

    private sealed class StringTupleComparer : IEqualityComparer<(bool, string)>
    {
        public static readonly StringTupleComparer Instance = new();
        public bool Equals((bool, string) x, (bool, string) y) => x.Item1 == y.Item1 && StringComparer.OrdinalIgnoreCase.Equals(x.Item2, y.Item2);
        public int GetHashCode((bool, string) obj) => HashCode.Combine(obj.Item1, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item2));
    }
}
