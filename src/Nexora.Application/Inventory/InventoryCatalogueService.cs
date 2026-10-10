using Nexora.Application.Abstractions;
using Nexora.Application.Security;
using Nexora.Domain.Inventory;
using Nexora.Domain.Security;

namespace Nexora.Application.Inventory;

/// <summary>
/// A product as shown to a user. Cost fields (purchase price) are null unless the user holds inventory.view-cost:
/// cost prices are withheld by the service, not just hidden by the screen.
/// </summary>
public sealed record ProductRow(Guid Id, string Sku, string Name, string DisplayName, string Description, string Category, string Brand,
    string? Barcode, string? QrCode, string HsnCode, Guid UnitId, string UnitCode, Guid? TaxCategoryId, string TaxCategoryName,
    decimal? PurchasePrice, decimal SellingPrice, decimal Mrp, decimal ReorderLevel, TrackingMode Tracking, bool TracksExpiry,
    Guid? ParentProductId, string VariantSize, string VariantColour, string VariantModel, bool HasVariants, bool IsArchived,
    decimal OnHand, decimal Available);

public sealed record ProductInput(Guid? Id, ProductDetails Details, Guid UnitId, TrackingMode Tracking, bool TracksExpiry);

public sealed record UnitRow(Guid Id, string Code, string Name, int DecimalPlaces, bool IsActive);

public sealed record TaxCategoryRow(Guid Id, string Name, decimal ReferenceRatePercent, bool IsActive);

public sealed record ConversionRow(Guid Id, Guid UnitId, string UnitCode, decimal Factor);

public sealed record InventorySettingsRow(CostingMethod CostingMethod, bool AllowNegativeStock, bool HasMovements);

/// <summary>Products, variants, units, unit conversions, tax categories and the company's inventory settings.</summary>
public sealed class InventoryCatalogueService(IUnitOfWorkFactory units, IAuthorizer authorizer)
{
    public async Task<IReadOnlyList<ProductRow>> ListProductsAsync(bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.InventoryView);
        var showCost = authorizer.Has(Permissions.InventoryViewCost);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var products = await unit.ListAsync<Product>(p => includeArchived || !p.IsArchived, cancellationToken);
        var unitCodes = (await unit.ListAsync<Unit>(cancellationToken: cancellationToken)).ToDictionary(u => u.Id, u => u.Code);
        var taxes = (await unit.ListAsync<TaxCategory>(cancellationToken: cancellationToken)).ToDictionary(t => t.Id, t => t.Name);
        var parents = (await unit.ListAsync<Product>(p => p.ParentProductId != null, cancellationToken)).Select(p => p.ParentProductId!.Value).ToHashSet();
        var movements = await unit.ListAsync<StockMovement>(cancellationToken: cancellationToken);
        var onHand = movements.GroupBy(m => m.ProductId).ToDictionary(g => g.Key, g => g.Sum(m => m.Quantity));
        var available = movements.Where(m => m.State == StockState.Good).GroupBy(m => m.ProductId).ToDictionary(g => g.Key, g => g.Sum(m => m.Quantity));

        return products
            .OrderBy(p => p.Sku, StringComparer.Ordinal)
            .Select(p => new ProductRow(p.Id, p.Sku, p.Name, p.DisplayName, p.Description, p.Category, p.Brand, p.Barcode, p.QrCode, p.HsnCode,
                p.UnitId, unitCodes.GetValueOrDefault(p.UnitId, "?"), p.TaxCategoryId,
                p.TaxCategoryId is { } t ? taxes.GetValueOrDefault(t, string.Empty) : string.Empty,
                showCost ? p.PurchasePrice : null, p.SellingPrice, p.Mrp, p.ReorderLevel, p.Tracking, p.TracksExpiry,
                p.ParentProductId, p.VariantSize, p.VariantColour, p.VariantModel, parents.Contains(p.Id), p.IsArchived,
                onHand.GetValueOrDefault(p.Id), available.GetValueOrDefault(p.Id)))
            .ToList();
    }

    /// <summary>
    /// The active product a scanned or typed code belongs to: its barcode, QR identifier or SKU (in that order, ignoring
    /// case for SKUs). Null when nothing matches. Codes are unique per company, so at most one product matches each kind.
    /// </summary>
    public async Task<ProductRow?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var clean = (code ?? string.Empty).Trim();
        if (clean.Length == 0) return null;
        var products = await ListProductsAsync(cancellationToken: cancellationToken);
        return products.FirstOrDefault(p => p.Barcode == clean)
            ?? products.FirstOrDefault(p => p.QrCode == clean)
            ?? products.FirstOrDefault(p => string.Equals(p.Sku, clean, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Creates or updates a product. Users without inventory.view-cost keep the stored purchase price unchanged.</summary>
    public async Task<Guid> SaveProductAsync(ProductInput input, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.InventoryManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        await EnsureUnitAndTaxAsync(unit, input.UnitId, input.Details.TaxCategoryId, cancellationToken);
        var details = input.Details;

        Product product;
        if (input.Id is { } id)
        {
            product = await unit.FindAsync<Product>(id, cancellationToken) ?? throw NotFound();
            if (!authorizer.Has(Permissions.InventoryViewCost)) details = details with { PurchasePrice = product.PurchasePrice };
            if (product.UnitId != input.UnitId || product.Tracking != input.Tracking || product.TracksExpiry != input.TracksExpiry)
                throw new WorkspaceValidationException("A product's unit and tracking are set when it is created. Create a new product to change them.");

            product.Update(details);
        }
        else
        {
            if (!authorizer.Has(Permissions.InventoryViewCost)) details = details with { PurchasePrice = 0 };
            product = new Product(Guid.NewGuid(), session.CompanyId, details, input.UnitId, input.Tracking, input.TracksExpiry, null);
            unit.Add(product);
        }

        await EnsureUniqueAsync(unit, product, cancellationToken);
        await unit.CommitAsync(cancellationToken);
        return product.Id;
    }

    /// <summary>Creates a variant (size, colour or model) of a product. The variant copies the parent's unit, tax and tracking.</summary>
    public async Task<Guid> AddVariantAsync(Guid parentId, ProductDetails details, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.InventoryManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var parent = await unit.FindAsync<Product>(parentId, cancellationToken) ?? throw NotFound();
        if (parent.ParentProductId is not null) throw new WorkspaceValidationException("Variants are added to the main product, not to another variant.");
        if (await unit.AnyAsync<StockMovement>(m => m.ProductId == parentId, cancellationToken))
            throw new WorkspaceValidationException("This product already holds stock, so it cannot be split into variants. Create a new main product for variants.");
        var merged = details with
        {
            Name = string.IsNullOrWhiteSpace(details.Name) ? parent.Name : details.Name,
            Category = parent.Category,
            Brand = parent.Brand,
            HsnCode = parent.HsnCode,
            TaxCategoryId = parent.TaxCategoryId,
            PurchasePrice = authorizer.Has(Permissions.InventoryViewCost) ? details.PurchasePrice : parent.PurchasePrice,
        };
        var variant = new Product(Guid.NewGuid(), session.CompanyId, merged, parent.UnitId, parent.Tracking, parent.TracksExpiry, parent.Id);
        unit.Add(variant);
        await EnsureUniqueAsync(unit, variant, cancellationToken);
        await unit.CommitAsync(cancellationToken);
        return variant.Id;
    }

    public async Task SetArchivedAsync(Guid productId, bool archived, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.InventoryManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var product = await unit.FindAsync<Product>(productId, cancellationToken) ?? throw NotFound();
        if (archived)
        {
            var quantity = (await unit.ListAsync<StockMovement>(m => m.ProductId == productId, cancellationToken)).Sum(m => m.Quantity);
            if (quantity != 0) throw new WorkspaceValidationException($"This product still has {quantity:0.###} in stock. Issue or adjust it to zero before archiving.");
        }

        product.SetArchived(archived);
        await unit.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ConversionRow>> ListConversionsAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.InventoryView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var codes = (await unit.ListAsync<Unit>(cancellationToken: cancellationToken)).ToDictionary(u => u.Id, u => u.Code);
        return (await unit.ListAsync<UnitConversion>(c => c.ProductId == productId, cancellationToken))
            .Select(c => new ConversionRow(c.Id, c.UnitId, codes.GetValueOrDefault(c.UnitId, "?"), c.Factor)).OrderBy(c => c.UnitCode).ToList();
    }

    /// <summary>Adds an alternate unit, for example 1 BOX = 12 PCS. Conversions are not edited once added; remove and re-add instead.</summary>
    public async Task AddConversionAsync(Guid productId, Guid unitId, decimal factor, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.InventoryManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var product = await unit.FindAsync<Product>(productId, cancellationToken) ?? throw NotFound();
        if (product.UnitId == unitId) throw new WorkspaceValidationException("That is already the product's base unit.");
        if (await unit.FindAsync<Unit>(unitId, cancellationToken) is null) throw new WorkspaceValidationException("That unit no longer exists.");
        if (await unit.AnyAsync<UnitConversion>(c => c.ProductId == productId && c.UnitId == unitId, cancellationToken))
            throw new WorkspaceValidationException("This product already has a conversion for that unit.");
        unit.Add(new UnitConversion(Guid.NewGuid(), session.CompanyId, productId, unitId, factor));
        await unit.CommitAsync(cancellationToken);
    }

    public async Task RemoveConversionAsync(Guid conversionId, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.InventoryManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var conversion = await unit.FindAsync<UnitConversion>(conversionId, cancellationToken) ?? throw new WorkspaceValidationException("That conversion no longer exists.");
        unit.Remove(conversion);
        await unit.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UnitRow>> ListUnitsAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.InventoryView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        return (await unit.ListAsync<Unit>(cancellationToken: cancellationToken)).OrderBy(u => u.Code)
            .Select(u => new UnitRow(u.Id, u.Code, u.Name, u.DecimalPlaces, u.IsActive)).ToList();
    }

    public async Task<Guid> SaveUnitAsync(Guid? id, string code, string name, int decimalPlaces, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.InventoryManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var clean = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (await unit.AnyAsync<Unit>(u => u.Code == clean && u.Id != (id ?? Guid.Empty), cancellationToken))
            throw new WorkspaceValidationException($"Unit {clean} already exists.");
        Unit entity;
        if (id is { } existing)
        {
            entity = await unit.FindAsync<Unit>(existing, cancellationToken) ?? throw new WorkspaceValidationException("That unit no longer exists.");
            if (decimalPlaces < entity.DecimalPlaces && await unit.AnyAsync<Product>(p => p.UnitId == existing, cancellationToken))
                throw new WorkspaceValidationException("Products use this unit, so its decimal places cannot be reduced.");
            entity.Update(clean, name, decimalPlaces);
        }
        else
        {
            entity = new Unit(Guid.NewGuid(), session.CompanyId, clean, name, decimalPlaces);
            unit.Add(entity);
        }

        await unit.CommitAsync(cancellationToken);
        return entity.Id;
    }

    public async Task<IReadOnlyList<TaxCategoryRow>> ListTaxCategoriesAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.InventoryView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        return (await unit.ListAsync<TaxCategory>(cancellationToken: cancellationToken)).OrderBy(t => t.ReferenceRatePercent).ThenBy(t => t.Name)
            .Select(t => new TaxCategoryRow(t.Id, t.Name, t.ReferenceRatePercent, t.IsActive)).ToList();
    }

    public async Task<Guid> SaveTaxCategoryAsync(Guid? id, string name, decimal referenceRatePercent, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.InventoryManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var clean = (name ?? string.Empty).Trim();
        if (await unit.AnyAsync<TaxCategory>(t => t.Name == clean && t.Id != (id ?? Guid.Empty), cancellationToken))
            throw new WorkspaceValidationException($"A tax category named \"{clean}\" already exists.");
        TaxCategory entity;
        if (id is { } existing)
        {
            entity = await unit.FindAsync<TaxCategory>(existing, cancellationToken) ?? throw new WorkspaceValidationException("That tax category no longer exists.");
            entity.Update(clean, referenceRatePercent);
        }
        else
        {
            entity = new TaxCategory(Guid.NewGuid(), session.CompanyId, clean, referenceRatePercent);
            unit.Add(entity);
        }

        await unit.CommitAsync(cancellationToken);
        return entity.Id;
    }

    public async Task<InventorySettingsRow> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.InventoryView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var settings = await SettingsAsync(unit, cancellationToken);
        return new InventorySettingsRow(settings.CostingMethod, settings.AllowNegativeStock,
            await unit.AnyAsync<StockMovement>(m => m.Quantity != 0, cancellationToken));
    }

    /// <summary>Changes costing and negative-stock policy. The costing method is fixed once stock has moved.</summary>
    public async Task SaveSettingsAsync(CostingMethod method, bool allowNegativeStock, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.InventoryManage);
        authorizer.Demand(Permissions.CompanyManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var settings = await SettingsAsync(unit, cancellationToken);
        settings.Change(method, allowNegativeStock, await unit.AnyAsync<StockMovement>(m => m.Quantity != 0, cancellationToken));
        await unit.CommitAsync(cancellationToken);
    }

    internal static async Task<InventorySettings> SettingsAsync(IUnitOfWork unit, CancellationToken cancellationToken) =>
        (await unit.ListAsync<InventorySettings>(cancellationToken: cancellationToken)).SingleOrDefault()
        ?? throw new WorkspaceValidationException("Inventory settings are missing for this company. Restart QIK Suite to repair them.");

    private static async Task EnsureUnitAndTaxAsync(IUnitOfWork unit, Guid unitId, Guid? taxId, CancellationToken cancellationToken)
    {
        if (await unit.FindAsync<Unit>(unitId, cancellationToken) is not { IsActive: true }) throw new WorkspaceValidationException("Choose an active unit.");
        if (taxId is { } t && t != Guid.Empty && await unit.FindAsync<TaxCategory>(t, cancellationToken) is null)
            throw new WorkspaceValidationException("That tax category no longer exists.");
    }

    /// <summary>Duplicate-code detection: SKU, barcode and QR identifier are each unique within the company.</summary>
    private static async Task EnsureUniqueAsync(IUnitOfWork unit, Product product, CancellationToken cancellationToken)
    {
        var id = product.Id;
        var sku = product.Sku;
        if (await unit.AnyAsync<Product>(p => p.Sku == sku && p.Id != id, cancellationToken))
            throw new WorkspaceValidationException($"SKU {sku} is already used by another product.");
        if (product.Barcode is { } barcode && await unit.AnyAsync<Product>(p => p.Barcode == barcode && p.Id != id, cancellationToken))
            throw new WorkspaceValidationException($"Barcode {barcode} is already used by another product.");
        if (product.QrCode is { } qr && await unit.AnyAsync<Product>(p => p.QrCode == qr && p.Id != id, cancellationToken))
            throw new WorkspaceValidationException("That QR identifier is already used by another product.");
    }

    private static WorkspaceValidationException NotFound() => new("That product no longer exists. Refresh and try again.");
}
