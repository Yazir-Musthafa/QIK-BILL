using Nexora.Application;
using Nexora.Domain.Billing;
using Nexora.Domain.Companies;
using Nexora.Domain.Expenses;
using Nexora.Domain.Inventory;
using Nexora.Domain.Service;

namespace Nexora.Reporting.Reports.Generators;

/// <summary>
/// Posted expenses grouped by category, date, branch, department or payment method. Like the ledger, an expense counts
/// on its date and a reversal counts negatively on the reversal's date.
/// </summary>
internal sealed class ExpenseSummaryGenerator() : Generator("expenses.summary")
{
    public static readonly string[] Groupings = ["category", "date", "branch", "department", "method"];

    public override async Task<ReportData> RunAsync(ReportContext c, CancellationToken cancellationToken)
    {
        c.EnsureRange();
        var (start, end) = (c.From, c.To);
        var grouping = c.Request.Grouping ?? "category";
        if (!Groupings.Contains(grouping)) throw new WorkspaceValidationException("Group expenses by category, date, branch, department or payment method.");

        var posted = await c.Unit.QueryAsync(q =>
            from l in q.Set<ExpenseLine>()
            join e in q.Set<Expense>() on l.ExpenseId equals e.Id
            where e.Status == ExpenseStatus.Posted && e.Date >= start && e.Date <= end
            select new Fact(e.Id, e.Date, l.CategoryId, e.BranchId, e.Department, e.Method, l.Amount, l.Cgst + l.Sgst + l.Igst, 1), cancellationToken);
        var reversed = await c.Unit.QueryAsync(q =>
            from r in q.Set<ExpenseReversal>()
            join e in q.Set<Expense>() on r.ExpenseId equals e.Id
            join l in q.Set<ExpenseLine>() on e.Id equals l.ExpenseId
            where r.Date >= start && r.Date <= end
            select new Fact(e.Id, r.Date, l.CategoryId, e.BranchId, e.Department, e.Method, l.Amount, l.Cgst + l.Sgst + l.Igst, -1), cancellationToken);
        var categories = (await c.Unit.ListAsync<ExpenseCategory>(cancellationToken: cancellationToken)).ToDictionary(x => x.Id, x => x.Name);
        var branches = (await c.Unit.ListAsync<Branch>(cancellationToken: cancellationToken)).ToDictionary(x => x.Id, x => x.Name);

        string Key(Fact f) => grouping switch
        {
            "category" => categories.GetValueOrDefault(f.CategoryId, "?"),
            "date" => f.Date.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture),
            "branch" => f.BranchId is { } b ? branches.GetValueOrDefault(b, "?") : "(no branch)",
            "department" => f.Department.Length > 0 ? f.Department : "(no department)",
            _ => f.Method.ToString(),
        };

        var rows = posted.Concat(reversed).GroupBy(Key).Select(g => new ReportRow([g.Key, g.Where(f => f.Sign > 0).Select(f => f.ExpenseId).Distinct().Count(),
                g.Sum(f => f.Sign * f.Amount), g.Sum(f => f.Sign * f.Tax), g.Sum(f => f.Sign * (f.Amount + f.Tax))]))
            .OrderBy(r => grouping == "date" ? (object)DateOnly.ParseExact((string)r.Cells[0]!, "dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture) : (object)(-(decimal)r.Cells[4]!))
            .ToList();
        var header = grouping switch { "category" => "Category", "date" => "Date", "branch" => "Branch", "department" => "Department", _ => "Payment method" };
        return new ReportData([Text("group", header), Count("expenses", "Expenses"), Money("amount", "Amount before GST"), Money("tax", "GST"), Money("total", "Total")],
            rows, $"{c.RangeText} · by {header.ToLowerInvariant()}", Notes: ["Drafts and expenses waiting for approval are not included. Reversals count on their own date."]);
    }

    internal sealed record Fact(Guid ExpenseId, DateOnly Date, Guid CategoryId, Guid? BranchId, string Department, PaymentMethod Method, decimal Amount, decimal Tax, int Sign);
}

/// <summary>Line-level GST facts: sales and purchase lines (with document sign) and claimable expense GST.</summary>
internal static class GstFacts
{
    public static async Task<List<(bool Outward, string Hsn, decimal Rate, Guid? UnitId, decimal Quantity, decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst)>> LoadAsync(
        ReportContext c, bool includeExpenses, CancellationToken cancellationToken)
    {
        var (start, end) = (c.From, c.To);
        var kinds = BillingFacts.Sales.Concat(BillingFacts.Purchases).ToArray();
        var lines = await c.Unit.QueryAsync(q =>
            from f in BillingFacts.Query(q, kinds, start, end)
            join l in q.Set<BillingLine>() on f.Id equals l.DocumentId
            group l by new { f.Kind, f.IsCancellation, l.HsnCode, l.TaxRatePercent, l.ProductId } into g
            select new { g.Key.Kind, g.Key.IsCancellation, g.Key.HsnCode, g.Key.TaxRatePercent, g.Key.ProductId, Quantity = g.Sum(l => l.BaseQuantity),
                Taxable = g.Sum(l => l.TaxableAmount), Cgst = g.Sum(l => l.Cgst), Sgst = g.Sum(l => l.Sgst), Igst = g.Sum(l => l.Igst) }, cancellationToken);
        var units = (await c.Unit.QueryAsync(q => q.Set<Product>().Select(p => new { p.Id, p.UnitId }), cancellationToken)).ToDictionary(p => p.Id, p => p.UnitId);

        var result = lines.Select(l =>
        {
            // In "supplies" terms: an invoice or bill adds, a return takes away, a cancellation reverses.
            var s = BillingFacts.Sign(l.Kind, l.IsCancellation);
            return (Outward: l.Kind is BillingDocumentKind.SalesInvoice or BillingDocumentKind.SalesReturn, l.HsnCode, l.TaxRatePercent,
                UnitId: l.ProductId is { } p && units.TryGetValue(p, out var u) ? (Guid?)u : null, s * l.Quantity, s * l.Taxable, s * l.Cgst, s * l.Sgst, s * l.Igst);
        }).ToList();

        if (includeExpenses)
        {
            var expenses = await c.Unit.QueryAsync(q =>
                from l in q.Set<ExpenseLine>()
                join e in q.Set<Expense>() on l.ExpenseId equals e.Id
                where e.Status == ExpenseStatus.Posted && e.Date >= start && e.Date <= end && l.InputTaxEligible
                group l by l.TaxRatePercent into g
                select new { Rate = g.Key, Taxable = g.Sum(l => l.Amount), Cgst = g.Sum(l => l.Cgst), Sgst = g.Sum(l => l.Sgst), Igst = g.Sum(l => l.Igst) }, cancellationToken);
            var reversals = await c.Unit.QueryAsync(q =>
                from r in q.Set<ExpenseReversal>()
                join l in q.Set<ExpenseLine>() on r.ExpenseId equals l.ExpenseId
                where r.Date >= start && r.Date <= end && l.InputTaxEligible
                group l by l.TaxRatePercent into g
                select new { Rate = g.Key, Taxable = g.Sum(l => l.Amount), Cgst = g.Sum(l => l.Cgst), Sgst = g.Sum(l => l.Sgst), Igst = g.Sum(l => l.Igst) }, cancellationToken);
            result.AddRange(expenses.Select(e => (false, "EXPENSES", e.Rate, (Guid?)null, 0m, e.Taxable, e.Cgst, e.Sgst, e.Igst)));
            result.AddRange(reversals.Select(e => (false, "EXPENSES", e.Rate, (Guid?)null, 0m, -e.Taxable, -e.Cgst, -e.Sgst, -e.Igst)));
        }

        return result;
    }
}

/// <summary>Output GST (sales) and input GST (purchases and claimable expenses) by rate, with the net position per tax head.</summary>
internal sealed class GstSummaryGenerator() : Generator("gst.summary")
{
    public override async Task<ReportData> RunAsync(ReportContext c, CancellationToken cancellationToken)
    {
        c.EnsureRange();
        var lines = await GstFacts.LoadAsync(c, includeExpenses: true, cancellationToken);
        var rows = new List<ReportRow>();
        (decimal T, decimal C, decimal S, decimal I) Section(bool outward, string title)
        {
            rows.Add(new ReportRow([title, null, null, null, null, null, null], Emphasis: true));
            var groups = lines.Where(l => l.Outward == outward).GroupBy(l => l.Rate).OrderBy(g => g.Key).ToList();
            foreach (var g in groups)
                rows.Add(new ReportRow([$"GST {g.Key:0.##}%", g.Sum(l => l.Taxable), g.Sum(l => l.Cgst), g.Sum(l => l.Sgst), g.Sum(l => l.Igst), 0m,
                    g.Sum(l => l.Cgst + l.Sgst + l.Igst)], Indent: 1));
            var t = (lines.Where(l => l.Outward == outward).Sum(l => l.Taxable), lines.Where(l => l.Outward == outward).Sum(l => l.Cgst),
                lines.Where(l => l.Outward == outward).Sum(l => l.Sgst), lines.Where(l => l.Outward == outward).Sum(l => l.Igst));
            rows.Add(new ReportRow([$"Total {title.ToLowerInvariant()}", t.Item1, t.Item2, t.Item3, t.Item4, 0m, t.Item2 + t.Item3 + t.Item4], Emphasis: true));
            return t;
        }

        var output = Section(true, "Output GST (sales after returns)");
        var input = Section(false, "Input GST (purchases after returns, and claimable expense GST)");
        rows.Add(new ReportRow(["Output less input", null, output.C - input.C, output.S - input.S, output.I - input.I, 0m,
            output.C + output.S + output.I - input.C - input.S - input.I], Emphasis: true));
        return new ReportData([Text("line", "Line"), Money("taxable", "Taxable value", total: false), Money("cgst", "CGST", total: false), Money("sgst", "SGST", total: false),
            Money("igst", "IGST", total: false), Money("cess", "Cess", total: false), Money("tax", "Total GST", total: false)], rows, c.RangeText,
            Notes: ["Output less input is a plain difference; it does not apply the GST set-off order or any credit carried forward."]);
    }
}

/// <summary>Outward and inward supplies by HSN or SAC code, rate and unit.</summary>
internal sealed class HsnSummaryGenerator() : Generator("gst.hsn")
{
    public override async Task<ReportData> RunAsync(ReportContext c, CancellationToken cancellationToken)
    {
        c.EnsureRange();
        var lines = await GstFacts.LoadAsync(c, includeExpenses: false, cancellationToken);
        var units = (await c.Unit.ListAsync<Unit>(cancellationToken: cancellationToken)).ToDictionary(u => u.Id, u => u.Code);
        var rows = lines.GroupBy(l => (l.Outward, Hsn: l.Hsn.Length > 0 ? l.Hsn : "(none)", l.Rate, l.UnitId))
            .OrderBy(g => g.Key.Outward ? 0 : 1).ThenBy(g => g.Key.Hsn, StringComparer.Ordinal).ThenBy(g => g.Key.Rate)
            .Select(g => new ReportRow([g.Key.Outward ? "Outward" : "Inward", g.Key.Hsn, g.Key.UnitId is { } u ? units.GetValueOrDefault(u, "") : "SERVICE",
                g.Key.Rate, g.Sum(l => l.Quantity), g.Sum(l => l.Taxable), g.Sum(l => l.Cgst), g.Sum(l => l.Sgst), g.Sum(l => l.Igst), 0m,
                g.Sum(l => l.Taxable + l.Cgst + l.Sgst + l.Igst)]))
            .ToList();
        return new ReportData([Text("direction", "Direction"), Text("hsn", "HSN/SAC"), Text("unit", "Unit"), new ReportColumn("rate", "Rate %", ColumnKind.Percent),
            Qty("quantity", "Quantity"), Money("taxable", "Taxable value", total: false), Money("cgst", "CGST", total: false), Money("sgst", "SGST", total: false),
            Money("igst", "IGST", total: false), Money("cess", "Cess", total: false), Money("value", "Total value", total: false)], rows, c.RangeText);
    }
}

/// <summary>Service jobs by technician: received, delivered, cancelled, still open, turnaround and labour.</summary>
internal sealed class ServicePerformanceGenerator() : Generator("service.performance")
{
    public override async Task<ReportData> RunAsync(ReportContext c, CancellationToken cancellationToken)
    {
        c.EnsureRange();
        var (start, end) = (c.From, c.To);
        var jobs = await c.Unit.QueryAsync(q => q.Set<ServiceJob>()
            .Where(j => (j.ReceivedOn >= start && j.ReceivedOn <= end) || (j.DeliveredOn >= start && j.DeliveredOn <= end))
            .Select(j => new { j.Id, j.TechnicianUserId, j.ReceivedOn, j.DeliveredOn, j.Status, j.Charge }), cancellationToken);
        var ids = jobs.Select(j => j.Id).ToList();
        var labour = (await c.Unit.QueryAsync(q => q.Set<ServiceLabour>().Where(l => ids.Contains(l.JobId)).Select(l => new { l.JobId, l.Quantity, l.Rate }), cancellationToken))
            .GroupBy(l => l.JobId).ToDictionary(g => g.Key, g => g.Sum(l => Math.Round(l.Quantity * l.Rate, 2, MidpointRounding.AwayFromZero)));
        var names = await Lookups.UserNamesAsync(c.Unit, cancellationToken);

        var rows = jobs.GroupBy(j => j.TechnicianUserId).Select(g =>
        {
            var received = g.Where(j => j.ReceivedOn >= start && j.ReceivedOn <= end).ToList();
            var delivered = g.Where(j => j.DeliveredOn is { } d && d >= start && d <= end).ToList();
            decimal? turnaround = delivered.Count == 0 ? null : Math.Round((decimal)delivered.Average(j => j.DeliveredOn!.Value.DayNumber - j.ReceivedOn.DayNumber), 1, MidpointRounding.AwayFromZero);
            var name = g.Key is { } id && names.TryGetValue(id, out var n) ? n : "(not assigned)";
            return new ReportRow([name, received.Count, delivered.Count, received.Count(j => j.Status == ServiceStatus.Cancelled),
                received.Count(j => j.Status is not (ServiceStatus.Delivered or ServiceStatus.Cancelled)), turnaround,
                received.Count(j => j.Charge == ServiceCharge.Warranty), delivered.Sum(j => labour.GetValueOrDefault(j.Id))]);
        }).OrderByDescending(r => (int)r.Cells[2]!).ToList();
        return new ReportData([Text("technician", "Technician"), Count("received", "Received"), Count("delivered", "Delivered"), Count("cancelled", "Cancelled"),
            Count("open", "Still open"), new ReportColumn("turnaround", "Average days to deliver", ColumnKind.Quantity), Count("warranty", "Warranty jobs"),
            Money("labour", "Labour on delivered jobs")], rows, c.RangeText,
            Notes: ["Received, cancelled, still open and warranty count jobs received in the range; delivered and labour count jobs delivered in the range."]);
    }
}
