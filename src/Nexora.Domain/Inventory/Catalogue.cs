using System.Text.RegularExpressions;
using Nexora.Domain.Accounting;
using Nexora.Domain.Common;

namespace Nexora.Domain.Inventory;

/// <summary>Quantities are decimals with at most three decimal places; each unit may allow fewer.</summary>
public static class Quantity
{
    public const decimal Max = 999_999_999.999m;

    public static decimal EnsureValid(decimal quantity, int decimalPlaces, string what = "Quantity")
    {
        if (quantity <= 0) throw new DomainException($"{what} must be more than zero.");
        if (quantity > Max) throw new DomainException($"{what} is too large.");
        if (decimal.Round(quantity, decimalPlaces) != quantity)
            throw new DomainException(decimalPlaces == 0 ? $"{what} must be a whole number for this unit." : $"{what} can have at most {decimalPlaces} decimal places.");
        return quantity;
    }

    public static decimal EnsureNonNegative(decimal quantity, string what)
    {
        if (quantity < 0) throw new DomainException($"{what} cannot be negative.");
        if (decimal.Round(quantity, 3) != quantity) throw new DomainException($"{what} can have at most 3 decimal places.");
        return quantity;
    }
}

/// <summary>A unit of measure such as PCS, BOX or KG. Decimal places limit how quantities may be entered.</summary>
public sealed partial class Unit : ICompanyOwned, IAudited
{
    private Unit()
    {
        Code = Name = string.Empty;
    }

    public Unit(Guid id, Guid companyId, string code, string name, int decimalPlaces)
    {
        Id = id;
        CompanyId = companyId;
        Code = string.Empty;
        Name = string.Empty;
        IsActive = true;
        Update(code, name, decimalPlaces);
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public int DecimalPlaces { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(string code, string name, int decimalPlaces)
    {
        var cleanCode = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (!CodePattern().IsMatch(cleanCode)) throw new DomainException("Unit code must be 1 to 10 letters or digits.");
        var cleanName = (name ?? string.Empty).Trim();
        if (cleanName.Length is 0 or > 40) throw new DomainException("Enter a unit name of up to 40 characters.");
        if (decimalPlaces is < 0 or > 3) throw new DomainException("Units allow 0 to 3 decimal places.");
        Code = cleanCode;
        Name = cleanName;
        DecimalPlaces = decimalPlaces;
    }

    public void SetActive(bool active) => IsActive = active;

    [GeneratedRegex("^[A-Z0-9]{1,10}$")]
    private static partial Regex CodePattern();
}

/// <summary>
/// A product's tax classification. The reference rate is informational in Phase 4; Phase 5 adds dated GST rates
/// and the rules that use them, so posted documents keep the rate that applied on their date.
/// </summary>
public sealed class TaxCategory : ICompanyOwned, IAudited
{
    private TaxCategory()
    {
        Name = string.Empty;
    }

    public TaxCategory(Guid id, Guid companyId, string name, decimal referenceRatePercent)
    {
        Id = id;
        CompanyId = companyId;
        Name = string.Empty;
        IsActive = true;
        Update(name, referenceRatePercent);
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Name { get; private set; }

    public decimal ReferenceRatePercent { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(string name, decimal referenceRatePercent)
    {
        var clean = (name ?? string.Empty).Trim();
        if (clean.Length is 0 or > 60) throw new DomainException("Enter a tax category name of up to 60 characters.");
        if (referenceRatePercent is < 0 or > 100 || decimal.Round(referenceRatePercent, 3) != referenceRatePercent)
            throw new DomainException("The reference rate must be between 0 and 100 percent.");
        Name = clean;
        ReferenceRatePercent = referenceRatePercent;
    }

    public void SetActive(bool active) => IsActive = active;
}

public enum TrackingMode
{
    None,
    Batch,
    Serial,
}

/// <summary>Barcode rules: EAN-13 and EAN-8 are checked with their check digit; anything else must be valid Code 128 text.</summary>
public static partial class Barcodes
{
    public static bool IsValidEan(string digits)
    {
        if (digits.Length is not (8 or 13) || !digits.All(char.IsAsciiDigit)) return false;
        var sum = 0;
        for (var i = 0; i < digits.Length - 1; i++)
        {
            // From the right of the payload, weights alternate 3, 1, 3, ...
            var weight = (digits.Length - 1 - i) % 2 == 1 ? 3 : 1;
            sum += (digits[i] - '0') * weight;
        }

        return (10 - sum % 10) % 10 == digits[^1] - '0';
    }

    /// <summary>Returns a user-facing problem, or null if the code is acceptable.</summary>
    public static string? Check(string code)
    {
        if (code.Length > 48) return "Barcodes can have at most 48 characters.";
        if (code.All(char.IsAsciiDigit) && code.Length is 8 or 13)
            return IsValidEan(code) ? null : $"This looks like an EAN-{code.Length} barcode but its last (check) digit is wrong.";
        return Code128Text().IsMatch(code) ? null : "Barcodes can use letters, digits, spaces and common symbols only.";
    }

    [GeneratedRegex(@"^[\x20-\x7E]{1,48}$")]
    private static partial Regex Code128Text();
}

/// <summary>
/// A stocked or sold item. Variants (size, colour, model) are products of their own, with their own SKU, barcode and
/// stock, linked to a parent product; a product that has variants does not hold stock itself.
/// </summary>
public sealed partial class Product : ICompanyOwned, IAudited
{
    private Product()
    {
        Sku = Name = Description = Category = Brand = HsnCode = VariantSize = VariantColour = VariantModel = string.Empty;
    }

    public Product(Guid id, Guid companyId, ProductDetails details, Guid unitId, TrackingMode tracking, bool tracksExpiry, Guid? parentProductId)
    {
        if (id == Guid.Empty || companyId == Guid.Empty || unitId == Guid.Empty) throw new ArgumentException("Ids are required.");
        if (tracksExpiry && tracking != TrackingMode.Batch) throw new DomainException("Expiry dates are tracked per batch. Choose batch tracking to track expiry.");
        Id = id;
        CompanyId = companyId;
        UnitId = unitId;
        Tracking = tracking;
        TracksExpiry = tracksExpiry;
        ParentProductId = parentProductId;
        Sku = Name = Description = Category = Brand = HsnCode = VariantSize = VariantColour = VariantModel = string.Empty;
        Update(details);
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Sku { get; private set; }

    public string Name { get; private set; }

    public string Description { get; private set; }

    public string Category { get; private set; }

    public string Brand { get; private set; }

    /// <summary>Barcode (EAN-13, EAN-8 or Code 128 text). Unique within the company when set.</summary>
    public string? Barcode { get; private set; }

    /// <summary>Identifier encoded in the product's QR code. Unique within the company when set. The QR content format is defined in Phase 6.</summary>
    public string? QrCode { get; private set; }

    /// <summary>HSN (goods) or SAC (services) code: 4, 6 or 8 digits.</summary>
    public string HsnCode { get; private set; }

    public Guid UnitId { get; private set; }

    public Guid? TaxCategoryId { get; private set; }

    public decimal PurchasePrice { get; private set; }

    public decimal SellingPrice { get; private set; }

    public decimal Mrp { get; private set; }

    public decimal ReorderLevel { get; private set; }

    public TrackingMode Tracking { get; private set; }

    public bool TracksExpiry { get; private set; }

    public Guid? ParentProductId { get; private set; }

    public string VariantSize { get; private set; }

    public string VariantColour { get; private set; }

    public string VariantModel { get; private set; }

    public bool IsArchived { get; private set; }

    public string DisplayName => string.Join(" / ", new[] { Name, VariantSize, VariantColour, VariantModel }.Where(p => p.Length > 0));

    public void Update(ProductDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        var sku = (details.Sku ?? string.Empty).Trim().ToUpperInvariant();
        if (!SkuPattern().IsMatch(sku)) throw new DomainException("SKU must be 1 to 30 letters, digits, dots, hyphens or slashes.");
        var name = Text(details.Name, 120, "Product name", required: true);
        var barcode = string.IsNullOrWhiteSpace(details.Barcode) ? null : details.Barcode.Trim();
        if (barcode is not null && Barcodes.Check(barcode) is { } problem) throw new DomainException(problem);
        var qr = string.IsNullOrWhiteSpace(details.QrCode) ? null : Text(details.QrCode, 120, "QR identifier", required: true);
        var hsn = (details.HsnCode ?? string.Empty).Trim();
        if (hsn.Length > 0 && !HsnPattern().IsMatch(hsn)) throw new DomainException("HSN/SAC code must be 4, 6 or 8 digits.");
        Money.EnsureValid(details.PurchasePrice, "Purchase price");
        Money.EnsureValid(details.SellingPrice, "Selling price");
        Money.EnsureValid(details.Mrp, "MRP");
        if (details.Mrp > 0 && details.SellingPrice > details.Mrp) throw new DomainException("The selling price cannot be more than the MRP.");
        Quantity.EnsureNonNegative(details.ReorderLevel, "Reorder level");

        Sku = sku;
        Name = name;
        Description = Text(details.Description, 1000, "Description");
        Category = Text(details.Category, 60, "Category");
        Brand = Text(details.Brand, 60, "Brand");
        Barcode = barcode;
        QrCode = qr;
        HsnCode = hsn;
        TaxCategoryId = details.TaxCategoryId == Guid.Empty ? null : details.TaxCategoryId;
        PurchasePrice = details.PurchasePrice;
        SellingPrice = details.SellingPrice;
        Mrp = details.Mrp;
        ReorderLevel = details.ReorderLevel;
        VariantSize = Text(details.VariantSize, 30, "Size");
        VariantColour = Text(details.VariantColour, 30, "Colour");
        VariantModel = Text(details.VariantModel, 30, "Model");
        if (ParentProductId is not null && VariantSize.Length + VariantColour.Length + VariantModel.Length == 0)
            throw new DomainException("A variant needs a size, colour or model.");
    }

    public void SetArchived(bool archived) => IsArchived = archived;

    private static string Text(string? value, int max, string what, bool required = false)
    {
        var clean = (value ?? string.Empty).Trim();
        if (required && clean.Length == 0) throw new DomainException($"Enter the {what.ToLowerInvariant()}.");
        if (clean.Length > max) throw new DomainException($"{what} must be {max} characters or fewer.");
        return clean;
    }

    [GeneratedRegex("^[A-Z0-9][A-Z0-9./-]{0,29}$")]
    private static partial Regex SkuPattern();

    [GeneratedRegex("^([0-9]{4}|[0-9]{6}|[0-9]{8})$")]
    private static partial Regex HsnPattern();
}

public sealed record ProductDetails(
    string Sku,
    string Name,
    string Description = "",
    string Category = "",
    string Brand = "",
    string? Barcode = null,
    string? QrCode = null,
    string HsnCode = "",
    Guid? TaxCategoryId = null,
    decimal PurchasePrice = 0,
    decimal SellingPrice = 0,
    decimal Mrp = 0,
    decimal ReorderLevel = 0,
    string VariantSize = "",
    string VariantColour = "",
    string VariantModel = "");

/// <summary>An alternate unit for a product: one <c>Unit</c> equals <see cref="Factor"/> base units (for example 1 BOX = 12 PCS).</summary>
public sealed class UnitConversion : ICompanyOwned, IAudited
{
    private UnitConversion()
    {
    }

    public UnitConversion(Guid id, Guid companyId, Guid productId, Guid unitId, decimal factor)
    {
        if (factor <= 0 || factor > 1_000_000 || decimal.Round(factor, 6) != factor)
            throw new DomainException("The conversion factor must be more than zero, at most 1,000,000, with up to 6 decimals.");
        Id = id;
        CompanyId = companyId;
        ProductId = productId;
        UnitId = unitId;
        Factor = factor;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid ProductId { get; private set; }

    public Guid UnitId { get; private set; }

    public decimal Factor { get; private set; }
}

/// <summary>A batch (lot) of a batch-tracked product, with optional manufacturing and expiry dates.</summary>
public sealed class Batch : ICompanyOwned, IAudited
{
    private Batch()
    {
        Number = string.Empty;
    }

    public Batch(Guid id, Guid companyId, Guid productId, string number, DateOnly? manufacturedOn, DateOnly? expiresOn)
    {
        var clean = (number ?? string.Empty).Trim().ToUpperInvariant();
        if (clean.Length is 0 or > 40) throw new DomainException("Enter a batch number of up to 40 characters.");
        if (manufacturedOn is { } m && expiresOn is { } e && e < m) throw new DomainException("The expiry date cannot be before the manufacturing date.");
        Id = id;
        CompanyId = companyId;
        ProductId = productId;
        Number = clean;
        ManufacturedOn = manufacturedOn;
        ExpiresOn = expiresOn;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid ProductId { get; private set; }

    public string Number { get; private set; }

    public DateOnly? ManufacturedOn { get; private set; }

    public DateOnly? ExpiresOn { get; private set; }

    public bool IsExpiredOn(DateOnly date) => ExpiresOn is { } expiry && expiry < date;
}

public enum CostingMethod
{
    WeightedAverage,
    Fifo,
}

/// <summary>
/// Company inventory policy. The costing method is chosen before the first stock movement and cannot change after,
/// because it determines every stored value. Negative stock is possible only with weighted average costing.
/// </summary>
public sealed class InventorySettings : ICompanyOwned, IAudited
{
    private InventorySettings()
    {
    }

    public InventorySettings(Guid id, Guid companyId)
    {
        Id = id;
        CompanyId = companyId;
        CostingMethod = CostingMethod.WeightedAverage;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public CostingMethod CostingMethod { get; private set; }

    public bool AllowNegativeStock { get; private set; }

    public void Change(CostingMethod method, bool allowNegativeStock, bool hasMovements)
    {
        if (method != CostingMethod && hasMovements)
            throw new DomainException("The costing method cannot change after stock has moved, because it decides every stored value.");
        if (allowNegativeStock && method == CostingMethod.Fifo)
            throw new DomainException("Negative stock can only be allowed with weighted average costing.");
        CostingMethod = method;
        AllowNegativeStock = allowNegativeStock;
    }
}
