using Nexora.Domain.Billing;

namespace Nexora.Reporting.Reports.Generators;

/// <summary>Sales or purchases by day, month or financial year, after returns and cancellations.</summary>
internal sealed class PeriodSummaryGenerator(string key, bool sales) : Generator(key)
{
    public override async Task<ReportData> RunAsync(ReportContext c, CancellationToken cancellationToken)
    {
        c.EnsureRange();
        var kinds = sales ? BillingFacts.Sales : BillingFacts.Purchases;
        var (start, to) = (c.From, c.To);
        var facts = await c.Unit.QueryAsync(q => BillingFacts.Query(q, kinds, start, to)
            .GroupBy(f => new { f.Date, f.Kind, f.IsCancellation })
            .Select(g => new { g.Key.Date, g.Key.Kind, g.Key.IsCancellation, Count = g.Count(), Taxable = g.Sum(f => f.Taxable),
                Tax = g.Sum(f => f.Cgst + f.Sgst + f.Igst), Total = g.Sum(f => f.Total) }), cancellationToken);

        var period = c.Request.Period;
        var rows = facts.GroupBy(f => Periods.Start(f.Date, period)).OrderBy(g => g.Key).Select(g =>
        {
            var gross = g.Where(f => f.Kind.IsCharge()).Sum(f => (f.IsCancellation ? -1 : 1) * f.Taxable);
            var returns = g.Where(f => !f.Kind.IsCharge()).Sum(f => (f.IsCancellation ? -1 : 1) * f.Taxable);
            var tax = g.Sum(f => BillingFacts.Sign(f.Kind, f.IsCancellation) * f.Tax);
            var total = g.Sum(f => BillingFacts.Sign(f.Kind, f.IsCancellation) * f.Total);
            var documents = g.Where(f => f.Kind.IsCharge() && !f.IsCancellation).Sum(f => f.Count);
            return new ReportRow([Periods.Label(g.Key, period), documents, gross, returns, gross - returns, tax, total]);
        }).ToList();

        var noun = sales ? "Invoices" : "Bills";
        return new ReportData(
        [
            Text("period", "Period"), Count("documents", noun), Money("gross", sales ? "Sales" : "Purchases"), Money("returns", "Returns"),
            Money("net", "Net taxable value"), Money("tax", "GST"), Money("total", "Total"),
        ], rows, c.RangeText);
    }
}

/// <summary>Every posted invoice or bill, return and cancellation in the range, paged in the database.</summary>
internal sealed class RegisterGenerator(string key, bool sales) : Generator(key)
{
    public override async Task<ReportData> RunAsync(ReportContext c, CancellationToken cancellationToken)
    {
        c.EnsureRange();
        var kinds = sales ? BillingFacts.Sales : BillingFacts.Purchases;
        var (start, to, party) = (c.From, c.To, c.Request.PartyId);
        var term = (c.Request.Search ?? string.Empty).Trim().ToLowerInvariant();

        IQueryable<BillingFacts.DocumentFact> Selection(Application.Abstractions.IQuerySource q)
        {
            var query = BillingFacts.Query(q, kinds, start, to, party);
            if (term.Length > 0) query = query.Where(f => (f.Number ?? "").ToLower().Contains(term) || f.PartyName.ToLower().Contains(term) || f.Gstin.ToLower().Contains(term));
            return query;
        }

        var groups = await c.Unit.QueryAsync(q => Selection(q).GroupBy(f => new { f.Kind, f.IsCancellation })
            .Select(g => new { g.Key.Kind, g.Key.IsCancellation, Count = g.Count(), Taxable = g.Sum(f => f.Taxable), Cgst = g.Sum(f => f.Cgst),
                Sgst = g.Sum(f => f.Sgst), Igst = g.Sum(f => f.Igst), RoundOff = g.Sum(f => f.RoundOff), Total = g.Sum(f => f.Total) }), cancellationToken);
        decimal Total(Func<(BillingDocumentKind Kind, bool Cancel, decimal T, decimal C, decimal S, decimal I, decimal R, decimal Tot), decimal> pick) =>
            groups.Sum(g => BillingFacts.Sign(g.Kind, g.IsCancellation) * pick((g.Kind, g.IsCancellation, g.Taxable, g.Cgst, g.Sgst, g.Igst, g.RoundOff, g.Total)));

        var descending = c.Request.SortDescending;
        var sortKey = c.Request.SortKey ?? "date";
        var (page, size) = (c.Request.Page, c.Request.PageSize);
        var facts = await c.Unit.QueryAsync(q =>
        {
            var query = Selection(q);
            query = (sortKey, descending) switch
            {
                ("number", false) => query.OrderBy(f => f.Number).ThenBy(f => f.Date),
                ("number", true) => query.OrderByDescending(f => f.Number).ThenByDescending(f => f.Date),
                ("party", false) => query.OrderBy(f => f.PartyName).ThenBy(f => f.Date),
                ("party", true) => query.OrderByDescending(f => f.PartyName).ThenByDescending(f => f.Date),
                ("total", false) => query.OrderBy(f => f.Total),
                ("total", true) => query.OrderByDescending(f => f.Total),
                (_, true) => query.OrderByDescending(f => f.Date).ThenByDescending(f => f.Number),
                _ => query.OrderBy(f => f.Date).ThenBy(f => f.Number),
            };
            return size > 0 ? query.Skip((page - 1) * size).Take(size) : query;
        }, cancellationToken);

        var rows = facts.Select(f =>
        {
            var s = BillingFacts.Sign(f.Kind, f.IsCancellation);
            var type = f.IsCancellation ? $"{f.Kind.Label()} cancelled" : f.Kind.Label();
            return new ReportRow([f.Date, f.Number ?? string.Empty, type, f.PartyName.Length > 0 ? f.PartyName : "Walk-in", f.Gstin, f.PlaceOfSupply,
                s * f.Taxable, s * f.Cgst, s * f.Sgst, s * f.Igst, 0m, s * f.RoundOff, s * f.Total], new DrillTarget(DrillKind.BillingDocument, f.Id));
        }).ToList();

        var totals = new object?[] { null, null, null, null, null, null, Total(x => x.T), Total(x => x.C), Total(x => x.S), Total(x => x.I), 0m, Total(x => x.R), Total(x => x.Tot) };
        return new ReportData(
        [
            Date("date", "Date"), Text("number", "Number"), Text("type", "Type"), Text("party", sales ? "Customer" : "Supplier"), Text("gstin", "GSTIN"),
            Text("pos", "Place of supply"), Money("taxable", "Taxable value"), Money("cgst", "CGST"), Money("sgst", "SGST"), Money("igst", "IGST"),
            Money("cess", "Cess"), Money("roundoff", "Round off"), Money("total", "Total"),
        ], rows, c.RangeText, totals, groups.Sum(g => g.Count));
    }
}

/// <summary>Sales by product: quantity, taxable value, cost and margin (cost and margin need Inventory: View cost).</summary>
internal sealed class ProductSalesGenerator() : Generator("sales.products")
{
    public override async Task<ReportData> RunAsync(ReportContext c, CancellationToken cancellationToken)
    {
        c.EnsureRange();
        var (start, to, party) = (c.From, c.To, c.Request.PartyId);
        var kinds = BillingFacts.Sales;
        var lines = await c.Unit.QueryAsync(q =>
            from f in BillingFacts.Query(q, kinds, start, to, party)
            join l in q.Set<BillingLine>() on f.Id equals l.DocumentId
            group l by new { f.Kind, f.IsCancellation, l.ProductId, l.HsnCode, Service = l.ProductId == null ? l.Description : "" } into g
            select new { g.Key.Kind, g.Key.IsCancellation, g.Key.ProductId, g.Key.Service, Quantity = g.Sum(l => l.BaseQuantity), Taxable = g.Sum(l => l.TaxableAmount),
                Cost = g.Sum(l => l.CostValue) }, cancellationToken);
        var products = (await c.Unit.QueryAsync(q => q.Set<Domain.Inventory.Product>().Select(p => new { p.Id, p.Sku, p.Name }), cancellationToken)).ToDictionary(p => p.Id);

        var rows = lines.GroupBy(l => (l.ProductId, l.Service)).Select(g =>
        {
            var quantity = g.Sum(l => BillingFacts.Sign(l.Kind, l.IsCancellation) * l.Quantity);
            var value = g.Sum(l => BillingFacts.Sign(l.Kind, l.IsCancellation) * l.Taxable);
            var cost = g.Sum(l => BillingFacts.Sign(l.Kind, l.IsCancellation) * l.Cost);
            var (sku, name) = g.Key.ProductId is { } id && products.TryGetValue(id, out var p) ? (p.Sku, p.Name) : ("SERVICE", g.Key.Service);
            return new ReportRow([sku, name, quantity, value, cost, value - cost, Margin(value, cost)],
                g.Key.ProductId is { } pid ? new DrillTarget(DrillKind.Product, pid) : null);
        }).OrderByDescending(r => (decimal)r.Cells[3]!).ToList();

        return new ReportData(
        [
            Text("sku", "SKU"), Text("product", "Product or service"), Qty("quantity", "Quantity (base units)"), Money("value", "Taxable value"),
            Money("cost", "Cost", cost: true), Money("margin", "Margin", cost: true), Percent("marginpct", "Margin %", cost: true),
        ], rows, c.RangeText, Notes: ["Parts billed from service jobs show no cost here: their cost was posted when the part was issued."]);
    }
}

/// <summary>Sales by customer or purchases by supplier.</summary>
internal sealed class PartySalesGenerator(string key, bool sales) : Generator(key)
{
    public override async Task<ReportData> RunAsync(ReportContext c, CancellationToken cancellationToken)
    {
        c.EnsureRange();
        var kinds = sales ? BillingFacts.Sales : BillingFacts.Purchases;
        var (start, to) = (c.From, c.To);
        var facts = await c.Unit.QueryAsync(q => BillingFacts.Query(q, kinds, start, to)
            .GroupBy(f => new { f.PartyId, f.PartyName, f.Kind, f.IsCancellation })
            .Select(g => new { g.Key.PartyId, g.Key.PartyName, g.Key.Kind, g.Key.IsCancellation, Count = g.Count(), Taxable = g.Sum(f => f.Taxable),
                Tax = g.Sum(f => f.Cgst + f.Sgst + f.Igst), Total = g.Sum(f => f.Total) }), cancellationToken);

        var rows = facts.GroupBy(f => f.PartyId).Select(g =>
        {
            var name = g.Key is null ? "Walk-in (cash, no customer)" : g.First().PartyName;
            var documents = g.Where(f => f.Kind.IsCharge() && !f.IsCancellation).Sum(f => f.Count) - g.Where(f => f.Kind.IsCharge() && f.IsCancellation).Sum(f => f.Count);
            var returns = g.Where(f => !f.Kind.IsCharge()).Sum(f => (f.IsCancellation ? -1 : 1) * f.Taxable);
            var taxable = g.Sum(f => BillingFacts.Sign(f.Kind, f.IsCancellation) * f.Taxable);
            var tax = g.Sum(f => BillingFacts.Sign(f.Kind, f.IsCancellation) * f.Tax);
            var total = g.Sum(f => BillingFacts.Sign(f.Kind, f.IsCancellation) * f.Total);
            return new ReportRow([name, documents, returns, taxable, tax, total], g.Key is { } id ? new DrillTarget(DrillKind.Party, id) : null);
        }).OrderByDescending(r => (decimal)r.Cells[3]!).ToList();

        return new ReportData(
        [
            Text("party", sales ? "Customer" : "Supplier"), Count("documents", sales ? "Invoices" : "Bills"), Money("returns", "Returns"),
            Money("taxable", "Net taxable value"), Money("tax", "GST"), Money("total", "Total"),
        ], rows, c.RangeText);
    }
}

/// <summary>Sales by the user who posted the invoice. QIK Suite has no separate salesperson field.</summary>
internal sealed class SalespersonGenerator() : Generator("sales.salespeople")
{
    public override async Task<ReportData> RunAsync(ReportContext c, CancellationToken cancellationToken)
    {
        c.EnsureRange();
        var (start, to) = (c.From, c.To);
        var kinds = BillingFacts.Sales;
        var facts = await c.Unit.QueryAsync(q => BillingFacts.Query(q, kinds, start, to)
            .GroupBy(f => new { f.PostedBy, f.Kind, f.IsCancellation })
            .Select(g => new { g.Key.PostedBy, g.Key.Kind, g.Key.IsCancellation, Count = g.Count(), Taxable = g.Sum(f => f.Taxable), Cost = g.Sum(f => f.Cost) }),
            cancellationToken);
        var names = await Lookups.UserNamesAsync(c.Unit, cancellationToken);
        var rows = facts.GroupBy(f => f.PostedBy).Select(g =>
        {
            var invoices = g.Where(f => f.Kind.IsCharge()).Sum(f => (f.IsCancellation ? -1 : 1) * f.Count);
            var value = g.Sum(f => BillingFacts.Sign(f.Kind, f.IsCancellation) * f.Taxable);
            var cost = g.Sum(f => BillingFacts.Sign(f.Kind, f.IsCancellation) * f.Cost);
            var name = g.Key is { } id && names.TryGetValue(id, out var n) ? n : "Unknown";
            return new ReportRow([name, invoices, value, invoices == 0 ? 0m : Math.Round(value / invoices, 2, MidpointRounding.AwayFromZero), value - cost, Margin(value, cost)]);
        }).OrderByDescending(r => (decimal)r.Cells[2]!).ToList();
        return new ReportData(
        [
            Text("user", "Posted by"), Count("invoices", "Invoices"), Money("value", "Net taxable value"), Money("average", "Average invoice", total: false),
            Money("margin", "Margin", cost: true), Percent("marginpct", "Margin %", cost: true),
        ], rows, c.RangeText, Notes: ["Salesperson is the user who posted each invoice; QIK Suite has no separate salesperson field."]);
    }
}
