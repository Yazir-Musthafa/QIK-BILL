using System.Globalization;
using Nexora.Application;
using Nexora.Application.Abstractions;
using Nexora.Application.Billing;
using Nexora.Application.Inventory;
using Nexora.Application.Security;
using Nexora.Domain.Accounting;
using Nexora.Domain.Audit;
using Nexora.Domain.Billing;
using Nexora.Domain.Common;
using Nexora.Domain.Companies;
using Nexora.Domain.Inventory;
using Nexora.Domain.Security;
using Nexora.Reporting.Export;
using Nexora.Reporting.Reports;

namespace Nexora.Reporting.Import;

public enum ImportKind
{
    Products,
    Customers,
    Suppliers,
    OpeningStock,
    ChartOfAccounts,
}

public sealed record ImportColumn(string Header, bool Required, string Help);

public sealed record ImportRowResult(int RowNumber, string Summary, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>What an import would do, row by row, before anything is saved.</summary>
public sealed record ImportPreview(ImportKind Kind, string FileName, IReadOnlyList<string> FileErrors, IReadOnlyList<ImportRowResult> Rows)
{
    public int ValidRows => Rows.Count(r => r.IsValid);

    public int ErrorRows => Rows.Count(r => !r.IsValid);

    public bool CanImport => FileErrors.Count == 0 && Rows.Count > 0 && ErrorRows == 0;
}

public sealed record ImportResult(int Created, string Summary);

/// <summary>Thrown when an import is refused; nothing was saved. <see cref="Preview"/> lists the errors.</summary>
public sealed class ImportRejectedException(ImportPreview preview)
    : WorkspaceValidationException(preview.FileErrors.Count > 0 ? preview.FileErrors[0] : $"{preview.ErrorRows} row(s) have errors. Nothing was imported; fix them and try again.")
{
    public ImportPreview Preview { get; } = preview;
}

/// <summary>
/// CSV and Excel import and export of products, customers, suppliers, opening stock and the chart of accounts.
/// Every row is validated (with the same domain rules as the screens) and shown in a preview before anything is
/// saved. An import saves everything in one transaction or nothing. Imports only add new records; rows matching
/// existing records are reported, never overwritten. Exports protect text cells against formula injection.
/// </summary>
public sealed class DataTransferService(IUnitOfWorkFactory units, IAuthorizer authorizer, ILedgerPoster ledger, PartyService parties, StockService stock, IClock clock)
{
    public static IReadOnlyList<ImportColumn> Columns(ImportKind kind) => kind switch
    {
        ImportKind.Products =>
        [
            new("SKU", true, "Unique product code, up to 40 characters."), new("Name", true, "Product name."), new("Description", false, ""),
            new("Category", false, ""), new("Brand", false, ""), new("Barcode", false, "EAN-13/EAN-8 with a correct check digit, or any text."),
            new("QR code", false, "Identifier printed in the product's QR code."), new("HSN/SAC", false, ""),
            new("Tax category", false, "Name of an existing tax category, for example GST 18%."), new("Unit", true, "Code of an existing unit, for example PCS."),
            new("Tracking", false, "None, Batch or Serial (default None)."), new("Track expiry", false, "Yes or No; only with Batch."),
            new("Purchase price", false, "Needs Inventory: View cost."), new("Selling price", false, ""), new("MRP", false, ""), new("Reorder level", false, ""),
            new("Warranty months", false, "0 to 120."),
        ],
        ImportKind.Customers or ImportKind.Suppliers =>
        [
            new("Name", true, "Unique among customers (or suppliers)."), new("Phone", false, ""), new("Email", false, ""),
            new("GSTIN", false, "15 characters with a correct check character."), new("State code", false, "Two digits, for example 27; taken from the GSTIN when empty."),
            new("Address", false, ""), new("City", false, ""), new("PIN code", false, "Six digits."), new("Credit limit", false, "0 means no limit."),
        ],
        ImportKind.OpeningStock =>
        [
            new("Date", true, "dd-mm-yyyy, inside an open financial year."), new("Warehouse", true, "Code of an active warehouse."),
            new("SKU", true, "An existing product (not one split into variants)."), new("Quantity", true, "More than 0, in the product's base unit."),
            new("Unit cost", true, "Cost per base unit."), new("Batch", false, "Required for batch-tracked products."),
            new("Expiry date", false, "Required when the product tracks expiry."), new("Serial", false, "Required for serial-tracked products; quantity 1."),
        ],
        _ =>
        [
            new("Code", true, "Unique account code."), new("Name", true, "Unique account name."),
            new("Parent code", true, "Code of an existing group, or of a group earlier in the file."), new("Type", true, "Group, General, Cash, Bank or Tax."),
        ],
    };

    public static string Label(ImportKind kind) => kind switch
    {
        ImportKind.Products => "Products",
        ImportKind.Customers => "Customers",
        ImportKind.Suppliers => "Suppliers",
        ImportKind.OpeningStock => "Opening stock",
        _ => "Chart of accounts",
    };

    /// <summary>An empty file with the right headers, to fill in and import.</summary>
    public ExportFile Template(ImportKind kind, ExportFormat format)
    {
        Demand(kind, forImport: true);
        var table = new ExportTable(Label(kind), [], Columns(kind).Select(c => new ReportColumn(c.Header, c.Header, ColumnKind.Text)).ToList(), []);
        return File(table, $"{Label(kind)} template", format);
    }

    public async Task<ImportPreview> PreviewAsync(ImportKind kind, byte[] content, string fileName, CancellationToken cancellationToken = default)
    {
        var session = Demand(kind, forImport: true);
        var data = TabularReader.Read(content, fileName);
        using var lockHandle = await ledger.AcquireAsync(cancellationToken);
        // A writable unit that is never committed: opening stock is tried in full, so every stock rule is checked now.
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var plan = await PlanAsync(kind, data, fileName, unit, session, cancellationToken);
        if (plan.Preview.CanImport && kind == ImportKind.OpeningStock)
        {
            try
            {
                await plan.Apply(unit);
            }
            catch (WorkspaceValidationException ex)
            {
                return plan.Preview with { FileErrors = [ex.Message] };
            }
        }

        return plan.Preview;
    }

    public async Task<ImportResult> ImportAsync(ImportKind kind, byte[] content, string fileName, CancellationToken cancellationToken = default)
    {
        var session = Demand(kind, forImport: true);
        var data = TabularReader.Read(content, fileName);
        using var lockHandle = await ledger.AcquireAsync(cancellationToken);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var plan = await PlanAsync(kind, data, fileName, unit, session, cancellationToken);
        if (!plan.Preview.CanImport) throw new ImportRejectedException(plan.Preview);
        var created = await plan.Apply(unit);
        unit.Record(new AuditEvent("Imported", Label(kind), string.Empty, $"Imported {created} {Label(kind).ToLowerInvariant()} row(s) from {Path.GetFileName(fileName)}"));
        await unit.CommitAsync(cancellationToken);
        return new ImportResult(created, $"Imported {created} {Label(kind).ToLowerInvariant()} row(s).");
    }

    public async Task<ExportFile> ExportAsync(ImportKind kind, ExportFormat format, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.DataExport);
        var session = Demand(kind, forImport: false);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var rows = new List<IReadOnlyList<object?>>();
        switch (kind)
        {
            case ImportKind.Products:
            {
                var unitsById = (await unit.ListAsync<Unit>(cancellationToken: cancellationToken)).ToDictionary(u => u.Id, u => u.Code);
                var taxes = (await unit.ListAsync<TaxCategory>(cancellationToken: cancellationToken)).ToDictionary(t => t.Id, t => t.Name);
                var cost = session.Has(Permissions.InventoryViewCost);
                foreach (var p in (await unit.ListAsync<Product>(p => !p.IsArchived, cancellationToken)).OrderBy(p => p.Sku, StringComparer.Ordinal))
                    rows.Add([p.Sku, p.Name, p.Description, p.Category, p.Brand, p.Barcode ?? "", p.QrCode ?? "", p.HsnCode,
                        p.TaxCategoryId is { } t ? taxes.GetValueOrDefault(t, "") : "", unitsById.GetValueOrDefault(p.UnitId, ""), p.Tracking.ToString(),
                        p.TracksExpiry ? "Yes" : "No", cost ? p.PurchasePrice : null, p.SellingPrice, p.Mrp, p.ReorderLevel, p.WarrantyMonths]);
                break;
            }

            case ImportKind.Customers or ImportKind.Suppliers:
            {
                var partyKind = kind == ImportKind.Customers ? PartyKind.Customer : PartyKind.Supplier;
                foreach (var p in (await unit.ListAsync<Party>(p => p.Kind == partyKind && p.IsActive, cancellationToken)).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
                    rows.Add([p.Name, p.Phone, p.Email, p.Gstin, p.StateCode, p.Address, p.City, p.Pincode, p.CreditLimit]);
                break;
            }

            case ImportKind.OpeningStock:
            {
                // Current stock as an opening-stock file (for example to start a new company): quantity and average unit cost.
                var today = DateOnly.FromDateTime(clock.UtcNow.ToLocalTime());
                var balances = await unit.QueryAsync(q => q.Set<StockMovement>().Where(m => m.State == StockState.Good)
                    .GroupBy(m => new { m.ProductId, m.WarehouseId, m.BatchId, m.SerialNumber })
                    .Select(g => new { g.Key.ProductId, g.Key.WarehouseId, g.Key.BatchId, g.Key.SerialNumber, Quantity = g.Sum(m => m.Quantity) }), cancellationToken);
                var pools = (await unit.QueryAsync(q => q.Set<StockMovement>().GroupBy(m => m.ProductId)
                    .Select(g => new { ProductId = g.Key, Quantity = g.Sum(m => m.Quantity), Value = g.Sum(m => m.Value) }), cancellationToken)).ToDictionary(p => p.ProductId);
                var products = (await unit.ListAsync<Product>(cancellationToken: cancellationToken)).ToDictionary(p => p.Id);
                var warehouses = (await unit.ListAsync<Warehouse>(cancellationToken: cancellationToken)).ToDictionary(w => w.Id, w => w.Code);
                var batches = (await unit.ListAsync<Batch>(cancellationToken: cancellationToken)).ToDictionary(b => b.Id);
                foreach (var b in balances.Where(b => b.Quantity > 0).OrderBy(b => products.TryGetValue(b.ProductId, out var p) ? p.Sku : "", StringComparer.Ordinal))
                {
                    var pool = pools.GetValueOrDefault(b.ProductId);
                    var unitCost = pool is { Quantity: > 0 } ? Math.Round(pool.Value / pool.Quantity, 2, MidpointRounding.AwayFromZero) : 0;
                    var batch = b.BatchId is { } id && batches.TryGetValue(id, out var x) ? x : null;
                    rows.Add([today, warehouses.GetValueOrDefault(b.WarehouseId, ""), products.TryGetValue(b.ProductId, out var product) ? product.Sku : "", b.Quantity, unitCost,
                        batch?.Number ?? "", batch?.ExpiresOn, b.SerialNumber ?? ""]);
                }

                break;
            }

            default:
            {
                var accounts = (await unit.ListAsync<Account>(cancellationToken: cancellationToken)).ToDictionary(a => a.Id);
                foreach (var a in accounts.Values.Where(a => a.ParentId is not null).OrderBy(a => a.Code, StringComparer.Ordinal))
                    rows.Add([a.Code, a.Name, accounts.TryGetValue(a.ParentId!.Value, out var parent) ? parent.Code : "", a.Kind == AccountKind.Group ? "Group" : a.Kind.ToString()]);
                break;
            }
        }

        var columns = Columns(kind).Select(c => new ReportColumn(c.Header, c.Header, ColumnKindOf(kind, c.Header))).ToList();
        return File(new ExportTable(Label(kind), [], columns, rows), Label(kind), format);
    }

    // ----- Validation -----

    private sealed record Plan(ImportPreview Preview, Func<IUnitOfWork, Task<int>> Apply);

    private sealed class RowReader(TabularRow row, IReadOnlyDictionary<string, int> index)
    {
        public List<string> Errors { get; } = [];

        public int RowNumber => row.RowNumber;

        public string Text(string header) => index.TryGetValue(Key(header), out var i) && i < row.Cells.Count ? row.Cells[i].Trim() : string.Empty;

        public string Required(string header)
        {
            var value = Text(header);
            if (value.Length == 0) Errors.Add($"{header} is required.");
            return value;
        }

        public decimal Number(string header, decimal fallback = 0)
        {
            var text = Text(header).Replace("₹", "", StringComparison.Ordinal).Replace(",", "", StringComparison.Ordinal).Trim();
            if (text.Length == 0) return fallback;
            if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)) return value;
            Errors.Add($"{header}: \"{Text(header)}\" is not a number.");
            return fallback;
        }

        public DateOnly? Date(string header)
        {
            var text = Text(header);
            if (text.Length == 0) return null;
            if (DateOnly.TryParseExact(text, ["dd-MM-yyyy", "d-M-yyyy", "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return date;
            Errors.Add($"{header}: enter a date as dd-mm-yyyy.");
            return null;
        }

        public void Guard(Action action)
        {
            try
            {
                action();
            }
            catch (DomainException ex)
            {
                Errors.Add(ex.Message);
            }
            catch (ArgumentException ex)
            {
                Errors.Add(ex.Message);
            }
        }
    }

    internal static string Key(string header) => new string(header.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    private async Task<Plan> PlanAsync(ImportKind kind, TabularData data, string fileName, IUnitOfWork unit, SessionInfo session, CancellationToken cancellationToken)
    {
        var columns = Columns(kind);
        var known = columns.ToDictionary(c => Key(c.Header));
        var index = new Dictionary<string, int>();
        var fileErrors = new List<string>();
        for (var i = 0; i < data.Headers.Count; i++)
        {
            var key = Key(data.Headers[i]);
            if (key.Length == 0) continue;
            if (!known.ContainsKey(key)) fileErrors.Add($"Unknown column \"{data.Headers[i]}\". Use the template's column names.");
            else if (!index.TryAdd(key, i)) fileErrors.Add($"Column \"{data.Headers[i]}\" appears twice.");
        }

        fileErrors.AddRange(columns.Where(c => c.Required && !index.ContainsKey(Key(c.Header))).Select(c => $"The required column \"{c.Header}\" is missing."));
        if (data.Rows.Count == 0) fileErrors.Add("The file has a header but no rows.");
        var readers = data.Rows.Select(r => new RowReader(r, index)).ToList();
        if (fileErrors.Count > 0) return new Plan(new ImportPreview(kind, fileName, fileErrors, []), _ => Task.FromResult(0));

        var (summaries, apply) = kind switch
        {
            ImportKind.Products => await ProductsAsync(readers, unit, session, cancellationToken),
            ImportKind.Customers => await PartiesAsync(readers, PartyKind.Customer, unit, session, cancellationToken),
            ImportKind.Suppliers => await PartiesAsync(readers, PartyKind.Supplier, unit, session, cancellationToken),
            ImportKind.OpeningStock => await OpeningStockAsync(readers, unit, session, cancellationToken),
            _ => await AccountsAsync(readers, unit, session, cancellationToken),
        };

        var rows = readers.Select((r, i) => new ImportRowResult(r.RowNumber, summaries[i], r.Errors)).ToList();
        return new Plan(new ImportPreview(kind, fileName, fileErrors, rows), apply);
    }

    private static async Task<(List<string>, Func<IUnitOfWork, Task<int>>)> ProductsAsync(List<RowReader> rows, IUnitOfWork unit, SessionInfo session, CancellationToken cancellationToken)
    {
        var existing = await unit.ListAsync<Product>(cancellationToken: cancellationToken);
        var skus = existing.Select(p => p.Sku.ToUpperInvariant()).ToHashSet();
        var codes = existing.SelectMany(p => new[] { p.Barcode, p.QrCode }).Where(c => !string.IsNullOrEmpty(c)).Select(c => c!.ToUpperInvariant()).ToHashSet();
        var unitsByCode = (await unit.ListAsync<Unit>(u => u.IsActive, cancellationToken)).ToDictionary(u => u.Code.ToUpperInvariant());
        var taxes = (await unit.ListAsync<TaxCategory>(t => t.IsActive, cancellationToken)).ToDictionary(t => t.Name.Trim().ToUpperInvariant());
        var canCost = session.Has(Permissions.InventoryViewCost);
        var products = new List<Product>();
        var summaries = new List<string>();
        foreach (var r in rows)
        {
            var sku = r.Required("SKU").ToUpperInvariant();
            var name = r.Required("Name");
            summaries.Add($"{sku} · {name}");
            var unitCode = r.Required("Unit").ToUpperInvariant();
            Unit? baseUnit = null;
            if (unitCode.Length > 0 && !unitsByCode.TryGetValue(unitCode, out baseUnit)) r.Errors.Add($"Unit \"{unitCode}\" does not exist. Add it under Settings → Inventory first.");
            var taxName = r.Text("Tax category");
            TaxCategory? tax = null;
            if (taxName.Length > 0 && !taxes.TryGetValue(taxName.ToUpperInvariant(), out tax)) r.Errors.Add($"Tax category \"{taxName}\" does not exist.");
            var tracking = TrackingMode.None;
            var trackingText = r.Text("Tracking");
            if (trackingText.Length > 0 && !Enum.TryParse(trackingText, true, out tracking)) r.Errors.Add("Tracking must be None, Batch or Serial.");
            var expiry = YesNo(r, "Track expiry");
            var purchase = r.Number("Purchase price");
            if (purchase != 0 && !canCost) r.Errors.Add("Your role cannot set purchase prices (Inventory: View cost). Leave the column empty.");
            var warranty = r.Number("Warranty months");
            if (warranty != decimal.Truncate(warranty)) r.Errors.Add("Warranty months must be a whole number.");
            var (selling, mrp, reorder) = (r.Number("Selling price"), r.Number("MRP"), r.Number("Reorder level"));
            var barcode = r.Text("Barcode");
            var qr = r.Text("QR code");
            if (sku.Length > 0 && !skus.Add(sku)) r.Errors.Add($"SKU {sku} already exists (in QIK Suite or earlier in this file).");
            foreach (var code in new[] { barcode, qr }.Where(c => c.Length > 0))
                if (!codes.Add(code.ToUpperInvariant())) r.Errors.Add($"Code {code} is already used by another product.");
            if (r.Errors.Count > 0 || baseUnit is null) continue;
            var details = new ProductDetails(sku, name, r.Text("Description"), r.Text("Category"), r.Text("Brand"), barcode.Length > 0 ? barcode : null, qr.Length > 0 ? qr : null,
                r.Text("HSN/SAC"), tax?.Id, purchase, selling, mrp, reorder, WarrantyMonths: (int)warranty);
            r.Guard(() => products.Add(new Product(Guid.NewGuid(), session.CompanyId, details, baseUnit.Id, tracking, expiry, null)));
        }

        return (summaries, u =>
        {
            foreach (var p in products) u.Add(p);
            return Task.FromResult(products.Count);
        });
    }

    private async Task<(List<string>, Func<IUnitOfWork, Task<int>>)> PartiesAsync(List<RowReader> rows, PartyKind kind, IUnitOfWork unit, SessionInfo session,
        CancellationToken cancellationToken)
    {
        var names = (await unit.ListAsync<Party>(p => p.Kind == kind, cancellationToken)).Select(p => p.Name.ToUpperInvariant()).ToHashSet();
        var inputs = new List<PartyDetails>();
        var summaries = new List<string>();
        foreach (var r in rows)
        {
            var name = r.Required("Name");
            summaries.Add(name);
            if (name.Length > 0 && !names.Add(name.ToUpperInvariant())) r.Errors.Add($"A {(kind == PartyKind.Customer ? "customer" : "supplier")} named \"{name}\" already exists (in QIK Suite or earlier in this file).");
            var details = new PartyDetails(name, r.Text("Phone"), r.Text("Email"), r.Text("GSTIN").ToUpperInvariant(), r.Text("State code"), r.Text("Address"),
                r.Text("City"), r.Text("PIN code"), r.Number("Credit limit"));
            if (r.Errors.Count > 0) continue;
            r.Guard(() => _ = new Party(Guid.NewGuid(), session.CompanyId, kind, Guid.NewGuid(), details));
            if (r.Errors.Count == 0) inputs.Add(details);
        }

        return (summaries, async u =>
        {
            var batch = new PartyBatch();
            foreach (var details in inputs) await parties.CreateCoreAsync(u, session, kind, details, cancellationToken, batch);
            return inputs.Count;
        });
    }

    private async Task<(List<string>, Func<IUnitOfWork, Task<int>>)> OpeningStockAsync(List<RowReader> rows, IUnitOfWork unit, SessionInfo session, CancellationToken cancellationToken)
    {
        var products = (await unit.ListAsync<Product>(cancellationToken: cancellationToken)).ToList();
        var bySku = products.ToDictionary(p => p.Sku.ToUpperInvariant());
        var parents = products.Where(p => p.ParentProductId is not null).Select(p => p.ParentProductId!.Value).ToHashSet();
        var warehouses = (await unit.ListAsync<Warehouse>(w => w.IsActive, cancellationToken)).ToDictionary(w => w.Code.ToUpperInvariant());
        var serials = new HashSet<(Guid, string)>();
        var lines = new List<(DateOnly Date, Guid Warehouse, StockLineInput Line)>();
        var summaries = new List<string>();
        foreach (var r in rows)
        {
            var sku = r.Required("SKU").ToUpperInvariant();
            var warehouseCode = r.Required("Warehouse").ToUpperInvariant();
            var date = r.Date("Date");
            if (date is null && r.Text("Date").Length == 0) r.Errors.Add("Date is required.");
            var quantity = r.Number("Quantity");
            var cost = r.Number("Unit cost");
            summaries.Add($"{sku} · {quantity.ToString("0.###", CultureInfo.InvariantCulture)} in {warehouseCode}");
            if (r.Text("Quantity").Length == 0) r.Errors.Add("Quantity is required.");
            if (r.Text("Unit cost").Length == 0) r.Errors.Add("Unit cost is required.");
            if (quantity <= 0 && r.Text("Quantity").Length > 0) r.Errors.Add("Quantity must be more than 0.");
            if (cost < 0) r.Errors.Add("Unit cost cannot be negative.");
            Product? product = null;
            if (sku.Length > 0 && !bySku.TryGetValue(sku, out product)) r.Errors.Add($"There is no product with SKU {sku}. Import products first.");
            if (product is { IsArchived: true }) r.Errors.Add($"{sku} is archived.");
            if (product is not null && parents.Contains(product.Id)) r.Errors.Add($"{sku} is split into variants; give opening stock to each variant.");
            Warehouse? warehouse = null;
            if (warehouseCode.Length > 0 && !warehouses.TryGetValue(warehouseCode, out warehouse)) r.Errors.Add($"There is no active warehouse with code {warehouseCode}.");
            var batch = r.Text("Batch");
            var expires = r.Date("Expiry date");
            var serial = r.Text("Serial").ToUpperInvariant();
            if (product is not null)
            {
                if (product.Tracking == TrackingMode.Batch && batch.Length == 0) r.Errors.Add($"{sku} is batch-tracked: enter the batch.");
                if (product.Tracking != TrackingMode.Batch && batch.Length > 0) r.Errors.Add($"{sku} is not batch-tracked: leave Batch empty.");
                if (product.TracksExpiry && expires is null) r.Errors.Add($"{sku} tracks expiry: enter the expiry date.");
                if (product.Tracking == TrackingMode.Serial && (serial.Length == 0 || quantity != 1)) r.Errors.Add($"{sku} is serial-tracked: enter one serial per row with quantity 1.");
                if (product.Tracking != TrackingMode.Serial && serial.Length > 0) r.Errors.Add($"{sku} is not serial-tracked: leave Serial empty.");
                if (serial.Length > 0 && !serials.Add((product.Id, serial))) r.Errors.Add($"Serial {serial} of {sku} appears twice in this file.");
            }

            if (r.Errors.Count == 0 && product is not null && warehouse is not null && date is { } d)
                lines.Add((d, warehouse.Id, new StockLineInput(product.Id, quantity, UnitCost: cost, BatchNumber: batch.Length > 0 ? batch : null, ExpiresOn: expires,
                    SerialNumber: serial.Length > 0 ? serial : null)));
        }

        return (summaries, async u =>
        {
            // One opening-stock document per date and warehouse, at most 500 lines each, all in this transaction.
            foreach (var group in lines.GroupBy(l => (l.Date, l.Warehouse)).OrderBy(g => g.Key.Date))
                foreach (var chunk in group.Select(g => g.Line).Chunk(500))
                    await stock.PostWithVoucherCoreAsync(u, session, new StockDocumentInput(StockDocumentType.Opening, group.Key.Date, group.Key.Warehouse, "Imported opening stock", chunk),
                        cancellationToken);
            return lines.Count;
        });
    }

    private static async Task<(List<string>, Func<IUnitOfWork, Task<int>>)> AccountsAsync(List<RowReader> rows, IUnitOfWork unit, SessionInfo session, CancellationToken cancellationToken)
    {
        var existing = await unit.ListAsync<Account>(cancellationToken: cancellationToken);
        var codes = existing.Select(a => a.Code.ToUpperInvariant()).ToHashSet();
        var names = existing.Select(a => a.Name.ToUpperInvariant()).ToHashSet();
        var groups = existing.Where(a => a.IsGroup).ToDictionary(a => a.Code.ToUpperInvariant());
        var created = new List<Account>();
        var summaries = new List<string>();
        foreach (var r in rows)
        {
            var code = r.Required("Code").ToUpperInvariant();
            var name = r.Required("Name");
            var parentCode = r.Required("Parent code").ToUpperInvariant();
            var typeText = r.Required("Type");
            summaries.Add($"{code} · {name}");
            AccountKind kind = AccountKind.General;
            if (typeText.Length > 0 && (!Enum.TryParse(typeText, true, out kind) || kind is not (AccountKind.Group or AccountKind.General or AccountKind.Cash or AccountKind.Bank or AccountKind.Tax)))
                r.Errors.Add("Type must be Group, General, Cash, Bank or Tax. Customer and supplier ledgers are created with the party.");
            Account? parent = null;
            if (parentCode.Length > 0 && !groups.TryGetValue(parentCode, out parent)) r.Errors.Add($"There is no group with code {parentCode} (in QIK Suite or earlier in this file).");
            if (parent is not null && kind is AccountKind.Cash or AccountKind.Bank && parent.Nature != AccountNature.Asset) r.Errors.Add("Cash and bank accounts go under an asset group.");
            if (code.Length > 0 && !codes.Add(code)) r.Errors.Add($"Account code {code} is already used.");
            if (name.Length > 0 && !names.Add(name.ToUpperInvariant())) r.Errors.Add($"An account named \"{name}\" already exists.");
            if (r.Errors.Count > 0 || parent is null) continue;
            r.Guard(() =>
            {
                var account = new Account(Guid.NewGuid(), session.CompanyId, code, name, parent.Nature, kind, parent.Id, null);
                created.Add(account);
                if (account.IsGroup) groups[code] = account;
            });
        }

        return (summaries, u =>
        {
            foreach (var a in created) u.Add(a);
            return Task.FromResult(created.Count);
        });
    }

    private static bool YesNo(RowReader r, string header)
    {
        var text = r.Text(header).ToUpperInvariant();
        if (text is "" or "NO" or "N" or "FALSE" or "0") return false;
        if (text is "YES" or "Y" or "TRUE" or "1") return true;
        r.Errors.Add($"{header} must be Yes or No.");
        return false;
    }

    // ----- Helpers -----

    private SessionInfo Demand(ImportKind kind, bool forImport)
    {
        var permissions = (kind, forImport) switch
        {
            (ImportKind.Products, true) => new[] { Permissions.InventoryManage },
            (ImportKind.Products, false) => [Permissions.InventoryView],
            (ImportKind.Customers, true) => [Permissions.SalesCreate],
            (ImportKind.Customers, false) => [Permissions.SalesView],
            (ImportKind.Suppliers, true) => [Permissions.PurchasesCreate],
            (ImportKind.Suppliers, false) => [Permissions.PurchasesView],
            (ImportKind.OpeningStock, true) => [Permissions.InventoryAdjust, Permissions.InventoryViewCost],
            (ImportKind.OpeningStock, false) => [Permissions.InventoryView, Permissions.InventoryViewCost],
            (_, true) => [Permissions.AccountsManage],
            _ => [Permissions.AccountsView],
        };
        SessionInfo? session = null;
        foreach (var permission in permissions) session = authorizer.Demand(permission);
        return session!;
    }

    private static ColumnKind ColumnKindOf(ImportKind kind, string header) => (kind, header) switch
    {
        (ImportKind.Products, "Purchase price" or "Selling price" or "MRP") => ColumnKind.Amount,
        (ImportKind.Products, "Reorder level") => ColumnKind.Quantity,
        (ImportKind.Products, "Warranty months") => ColumnKind.Integer,
        (ImportKind.Customers or ImportKind.Suppliers, "Credit limit") => ColumnKind.Amount,
        (ImportKind.OpeningStock, "Date" or "Expiry date") => ColumnKind.Date,
        (ImportKind.OpeningStock, "Quantity") => ColumnKind.Quantity,
        (ImportKind.OpeningStock, "Unit cost") => ColumnKind.Amount,
        _ => ColumnKind.Text,
    };

    private static ExportFile File(ExportTable table, string name, ExportFormat format) => format switch
    {
        ExportFormat.Excel => new ExportFile(name + ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", XlsxWriter.Write(table, includeTitle: false)),
        ExportFormat.Csv => new ExportFile(name + ".csv", "text/csv", CsvWriter.Write(table, includeTitle: false)),
        _ => throw new WorkspaceValidationException("Data exports are CSV or Excel files."),
    };
}
