using Nexora.Domain.Companies;
using Nexora.Domain.Inventory;

namespace Nexora.Reporting.Reports.Generators;

internal static class ProductNames
{
    internal sealed record Info(Guid Id, string Sku, string Name, string Unit, decimal ReorderLevel, bool IsArchived, bool HasChildren);

    public static async Task<Dictionary<Guid, Info>> LoadAsync(Application.Abstractions.IUnitOfWork unit, CancellationToken cancellationToken)
    {
        var rows = await unit.QueryAsync(q =>
            from p in q.Set<Product>()
            join u in q.Set<Unit>() on p.UnitId equals u.Id
            select new { p.Id, p.Sku, p.Name, p.VariantSize, p.VariantColour, p.VariantModel, Unit = u.Code, p.ReorderLevel, p.IsArchived, p.ParentProductId },
            cancellationToken);
        var parents = rows.Where(r => r.ParentProductId is not null).Select(r => r.ParentProductId!.Value).ToHashSet();
        return rows.ToDictionary(r => r.Id, r => new Info(r.Id, r.Sku,
            string.Join(" / ", new[] { r.Name, r.VariantSize, r.VariantColour, r.VariantModel }.Where(p => p.Length > 0)), r.Unit, r.ReorderLevel, r.IsArchived,
            parents.Contains(r.Id)));
    }
}

/// <summary>
/// Stock by product on a date from the movement ledger. The value column is the product's pooled stock value, which
/// equals the Inventory ledger in total. With a warehouse chosen, value is that warehouse's share at the average cost.
/// </summary>
internal sealed class ValuationGenerator() : Generator("inventory.valuation")
{
    public override async Task<ReportData> RunAsync(ReportContext c, CancellationToken cancellationToken)
    {
        var (asOf, warehouse) = (c.AsOf, c.Request.WarehouseId);
        var pool = await c.Unit.QueryAsync(q => q.Set<StockMovement>().Where(m => m.Date <= asOf).GroupBy(m => m.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(m => m.Quantity), Value = g.Sum(m => m.Value) }), cancellationToken);
        var here = warehouse is null ? null : (await c.Unit.QueryAsync(q => q.Set<StockMovement>().Where(m => m.Date <= asOf && m.WarehouseId == warehouse)
            .GroupBy(m => m.ProductId).Select(g => new { ProductId = g.Key, Quantity = g.Sum(m => m.Quantity) }), cancellationToken)).ToDictionary(r => r.ProductId, r => r.Quantity);
        var products = await ProductNames.LoadAsync(c.Unit, cancellationToken);

        var rows = new List<ReportRow>();
        foreach (var p in pool.OrderBy(p => products.TryGetValue(p.ProductId, out var i) ? i.Sku : "", StringComparer.Ordinal))
        {
            var quantity = here is null ? p.Quantity : here.GetValueOrDefault(p.ProductId);
            var unitCost = p.Quantity > 0 ? Math.Round(p.Value / p.Quantity, 2, MidpointRounding.AwayFromZero) : 0;
            var value = here is null ? p.Value : Math.Round(unitCost * quantity, 2, MidpointRounding.AwayFromZero);
            if (quantity == 0 && value == 0) continue;
            var info = products.GetValueOrDefault(p.ProductId);
            rows.Add(new ReportRow([info?.Sku ?? "?", info?.Name ?? "?", info?.Unit ?? "", quantity, unitCost, value], new DrillTarget(DrillKind.Product, p.ProductId)));
        }

        var warehouseName = warehouse is null ? "all warehouses" : (await c.Unit.FindAsync<Warehouse>(warehouse.Value, cancellationToken))?.Name ?? "?";
        return new ReportData(
        [
            Text("sku", "SKU"), Text("product", "Product"), Text("unit", "Unit"), Qty("quantity", "Quantity", total: true),
            Money("unitcost", "Unit cost", total: false, cost: true), Money("value", "Value", cost: true),
        ], rows, $"{c.AsOfText} · {warehouseName}",
            Notes: warehouse is null ? null : ["Cost is pooled per product across warehouses; a warehouse's value is its quantity at the product's average cost."]);
    }
}

/// <summary>Every stock movement in the range, paged in the database.</summary>
internal sealed class MovementGenerator() : Generator("inventory.movements")
{
    public override async Task<ReportData> RunAsync(ReportContext c, CancellationToken cancellationToken)
    {
        c.EnsureRange();
        var (start, to, product, warehouse) = (c.From, c.To, c.Request.ProductId, c.Request.WarehouseId);
        var term = (c.Request.Search ?? string.Empty).Trim().ToLowerInvariant();
        IQueryable<Row> Selection(Application.Abstractions.IQuerySource q) =>
            from m in q.Set<StockMovement>()
            join d in q.Set<StockDocument>() on m.DocumentId equals d.Id
            join p in q.Set<Product>() on m.ProductId equals p.Id
            join w in q.Set<Warehouse>() on m.WarehouseId equals w.Id
            join b in q.Set<Batch>() on m.BatchId equals b.Id into bs
            from b in bs.DefaultIfEmpty()
            where m.Date >= start && m.Date <= to && (product == null || m.ProductId == product) && (warehouse == null || m.WarehouseId == warehouse)
                && (term == "" || d.Number.ToLower().Contains(term) || p.Sku.ToLower().Contains(term) || p.Name.ToLower().Contains(term))
            select new Row
            {
                Sequence = m.Sequence, Date = m.Date, DocumentId = d.Id, Number = d.Number, Type = d.Type, Sku = p.Sku, Name = p.Name, Warehouse = w.Name,
                State = m.State, Batch = b == null ? "" : b.Number, Serial = m.SerialNumber, Quantity = m.Quantity, Value = m.Value,
            };

        var totals = (await c.Unit.QueryAsync(q => Selection(q).GroupBy(r => 1).Select(g => new { Count = g.Count(), In = g.Sum(r => r.Quantity > 0 ? r.Quantity : 0),
            Out = g.Sum(r => r.Quantity < 0 ? -r.Quantity : 0), Value = g.Sum(r => r.Value) }), cancellationToken)).SingleOrDefault();
        var (page, size, descending) = (c.Request.Page, c.Request.PageSize, c.Request.SortDescending);
        var rows = await c.Unit.QueryAsync(q =>
        {
            var query = descending ? Selection(q).OrderByDescending(r => r.Date).ThenByDescending(r => r.Sequence) : Selection(q).OrderBy(r => r.Date).ThenBy(r => r.Sequence);
            return size > 0 ? query.Skip((page - 1) * size).Take(size) : query;
        }, cancellationToken);

        return new ReportData(
        [
            Date("date", "Date"), Text("document", "Document"), Text("type", "Type"), Text("sku", "SKU"), Text("product", "Product"), Text("warehouse", "Warehouse"),
            Text("state", "State"), Text("batch", "Batch"), Text("serial", "Serial"), Qty("in", "In", total: true), Qty("out", "Out", total: true), Money("value", "Value change", cost: true),
        ], rows.Select(r => new ReportRow([r.Date, r.Number, r.Type.Label(), r.Sku, r.Name, r.Warehouse, r.State.ToString(), r.Batch, r.Serial ?? "",
            r.Quantity > 0 ? r.Quantity : 0m, r.Quantity < 0 ? -r.Quantity : 0m, r.Value], new DrillTarget(DrillKind.StockDocument, r.DocumentId))).ToList(),
            c.RangeText, [null, null, null, null, null, null, null, null, null, totals?.In ?? 0, totals?.Out ?? 0, totals?.Value ?? 0], totals?.Count ?? 0);
    }

    internal sealed class Row
    {
        public long Sequence { get; init; }
        public DateOnly Date { get; init; }
        public Guid DocumentId { get; init; }
        public string Number { get; init; } = "";
        public StockDocumentType Type { get; init; }
        public string Sku { get; init; } = "";
        public string Name { get; init; } = "";
        public string Warehouse { get; init; } = "";
        public StockState State { get; init; }
        public string Batch { get; init; } = "";
        public string? Serial { get; init; }
        public decimal Quantity { get; init; }
        public decimal Value { get; init; }
    }
}

internal sealed class LowStockGenerator() : Generator("inventory.lowstock")
{
    public override async Task<ReportData> RunAsync(ReportContext c, CancellationToken cancellationToken)
    {
        var available = (await c.Unit.QueryAsync(q => q.Set<StockMovement>().Where(m => m.State == StockState.Good).GroupBy(m => m.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(m => m.Quantity) }), cancellationToken)).ToDictionary(r => r.ProductId, r => r.Quantity);
        var rows = (await ProductNames.LoadAsync(c.Unit, cancellationToken)).Values
            .Where(p => !p.IsArchived && !p.HasChildren && p.ReorderLevel > 0 && available.GetValueOrDefault(p.Id) <= p.ReorderLevel)
            .OrderBy(p => p.Sku, StringComparer.Ordinal)
            .Select(p => new ReportRow([p.Sku, p.Name, p.Unit, available.GetValueOrDefault(p.Id), p.ReorderLevel, p.ReorderLevel - available.GetValueOrDefault(p.Id)],
                new DrillTarget(DrillKind.Product, p.Id)))
            .ToList();
        return new ReportData([Text("sku", "SKU"), Text("product", "Product"), Text("unit", "Unit"), Qty("available", "Available (good)"),
            Qty("reorder", "Reorder level"), Qty("short", "Short by")], rows, $"As of now · {c.Today:dd-MM-yyyy}");
    }
}

/// <summary>Batches with stock on a date and their expiry status.</summary>
internal sealed class BatchGenerator() : Generator("inventory.batches")
{
    public override async Task<ReportData> RunAsync(ReportContext c, CancellationToken cancellationToken)
    {
        var asOf = c.AsOf;
        var quantities = await c.Unit.QueryAsync(q => q.Set<StockMovement>().Where(m => m.BatchId != null && m.Date <= asOf).GroupBy(m => m.BatchId)
            .Select(g => new { BatchId = g.Key!.Value, Quantity = g.Sum(m => m.Quantity) }), cancellationToken);
        var info = (await c.Unit.ListAsync<Batch>(cancellationToken: cancellationToken)).ToDictionary(b => b.Id);
        var batches = quantities.Where(x => info.ContainsKey(x.BatchId)).Select(x => new { info[x.BatchId].Id, info[x.BatchId].ProductId, info[x.BatchId].Number,
            info[x.BatchId].ManufacturedOn, info[x.BatchId].ExpiresOn, x.Quantity }).ToList();
        var products = await ProductNames.LoadAsync(c.Unit, cancellationToken);
        var rows = batches.Where(b => b.Quantity != 0).OrderBy(b => b.ExpiresOn ?? DateOnly.MaxValue).ThenBy(b => b.Number, StringComparer.Ordinal).Select(b =>
        {
            var p = products.GetValueOrDefault(b.ProductId);
            int? days = b.ExpiresOn is { } e ? e.DayNumber - asOf.DayNumber : null;
            var status = days switch { null => "No expiry", < 0 => "Expired", <= 30 => "Expires within 30 days", _ => "In date" };
            return new ReportRow([p?.Sku ?? "?", p?.Name ?? "?", b.Number, b.ManufacturedOn, b.ExpiresOn, days, b.Quantity, status], new DrillTarget(DrillKind.Product, b.ProductId));
        }).ToList();
        return new ReportData([Text("sku", "SKU"), Text("product", "Product"), Text("batch", "Batch"), Date("made", "Manufactured"), Date("expires", "Expires"),
            new ReportColumn("days", "Days left", ColumnKind.Integer), Qty("quantity", "Quantity", total: true), Text("status", "Status")], rows, c.AsOfText);
    }
}
