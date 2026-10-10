using Nexora.Domain.Common;
using Nexora.Domain.Inventory;

namespace Nexora.Tests.Inventory;

public sealed class BarcodeTests
{
    // Published examples with correct check digits (EAN-13 and EAN-8).
    [Theory]
    [InlineData("4006381333931")]
    [InlineData("5901234123457")]
    [InlineData("96385074")]
    [InlineData("73513537")]
    public void Valid_ean_codes_pass(string code)
    {
        Assert.True(Barcodes.IsValidEan(code));
        Assert.Null(Barcodes.Check(code));
    }

    [Theory]
    [InlineData("4006381333932")]
    [InlineData("96385075")]
    public void Wrong_ean_check_digit_is_reported(string code) =>
        Assert.Contains("check) digit is wrong", Barcodes.Check(code), StringComparison.Ordinal);

    [Theory]
    [InlineData("LED-18W/WHITE", true)]
    [InlineData("123456", true)]
    [InlineData("café", false)]
    [InlineData("tab\there", false)]
    public void Other_codes_must_be_code_128_text(string code, bool valid) => Assert.Equal(valid, Barcodes.Check(code) is null);

    [Fact]
    public void Barcodes_are_limited_to_48_characters() => Assert.NotNull(Barcodes.Check(new string('A', 49)));
}

public sealed class ProductTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid UnitId = Guid.NewGuid();

    private static Product Make(ProductDetails details, TrackingMode tracking = TrackingMode.None, bool expiry = false, Guid? parent = null) =>
        new(Guid.NewGuid(), CompanyId, details, UnitId, tracking, expiry, parent);

    [Fact]
    public void Creates_a_product_with_normalised_values()
    {
        var product = Make(new ProductDetails(" led-18w ", " LED panel 18 W ", Barcode: "4006381333931", HsnCode: "9405", SellingPrice: 450, Mrp: 499, PurchasePrice: 310.5m, ReorderLevel: 20));

        Assert.Equal("LED-18W", product.Sku);
        Assert.Equal("LED panel 18 W", product.Name);
        Assert.Equal(310.5m, product.PurchasePrice);
    }

    [Theory]
    [InlineData("has space", 10, 0, "")]
    [InlineData("OK", 500, 499, "")]
    [InlineData("OK", 10, 0, "12345")]
    [InlineData("OK", 10.001, 0, "")]
    public void Rejects_invalid_details(string sku, decimal selling, decimal mrp, string hsn) =>
        Assert.Throws<DomainException>(() => Make(new ProductDetails(sku, "Name", SellingPrice: selling, Mrp: mrp, HsnCode: hsn)));

    [Fact]
    public void Expiry_requires_batch_tracking() =>
        Assert.Throws<DomainException>(() => Make(new ProductDetails("MED-1", "Syrup"), TrackingMode.None, expiry: true));

    [Fact]
    public void Variant_needs_a_size_colour_or_model()
    {
        Assert.Throws<DomainException>(() => Make(new ProductDetails("TS-1", "T-shirt"), parent: Guid.NewGuid()));
        var variant = Make(new ProductDetails("TS-1-M-RED", "T-shirt", VariantSize: "M", VariantColour: "Red"), parent: Guid.NewGuid());
        Assert.Equal("T-shirt / M / Red", variant.DisplayName);
    }
}

public sealed class InventoryRuleTests
{
    [Theory]
    [InlineData(1.5, 0)]
    [InlineData(0, 3)]
    [InlineData(-2, 3)]
    [InlineData(1.0001, 3)]
    public void Quantities_respect_the_units_decimal_places(decimal quantity, int decimals) =>
        Assert.Throws<DomainException>(() => Quantity.EnsureValid(quantity, decimals));

    [Fact]
    public void Batch_expiry_cannot_precede_manufacture() =>
        Assert.Throws<DomainException>(() => new Batch(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "B1", new DateOnly(2026, 5, 1), new DateOnly(2026, 4, 1)));

    [Fact]
    public void Settings_fix_the_costing_method_after_movements_and_forbid_negative_fifo()
    {
        var settings = new InventorySettings(Guid.NewGuid(), Guid.NewGuid());
        Assert.Throws<DomainException>(() => settings.Change(CostingMethod.Fifo, allowNegativeStock: true, hasMovements: false));
        settings.Change(CostingMethod.Fifo, false, hasMovements: false);
        Assert.Throws<DomainException>(() => settings.Change(CostingMethod.WeightedAverage, false, hasMovements: true));
        settings.Change(CostingMethod.Fifo, false, hasMovements: true);
    }

    [Fact]
    public void Movements_keep_value_consistent_with_quantity()
    {
        Assert.Throws<DomainException>(() => Movement(5, 100, isInternal: true));
        Assert.Throws<DomainException>(() => Movement(-5, 100, isInternal: false));
        Assert.Throws<DomainException>(() => Movement(0, 0, isInternal: true));
        Assert.Throws<DomainException>(() => Movement(1.0001m, 0, isInternal: true));
    }

    internal static StockMovement Movement(decimal quantity, decimal value, bool isInternal = false, long sequence = 1, Guid? document = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), document ?? Guid.NewGuid(), 1, sequence, Guid.NewGuid(), Guid.NewGuid(), StockState.Good, null, null,
            new DateOnly(2026, 10, 10), quantity, value, isInternal, null, null);
}

public sealed class CostPoolTests
{
    [Fact]
    public void Weighted_average_issues_at_the_running_average()
    {
        var pool = new CostPool(CostingMethod.WeightedAverage);
        pool.Receive(10, 1_000);
        pool.Receive(10, 1_300);

        Assert.Equal(115m, pool.CurrentUnitCost);
        Assert.Equal(575m, pool.Issue(5, allowNegative: false));
        Assert.Equal(1_725m, pool.Issue(15, allowNegative: false));
        Assert.Equal((0m, 0m), (pool.Quantity, pool.Value));
    }

    [Fact]
    public void Weighted_average_leaves_no_rounding_residue()
    {
        var pool = new CostPool(CostingMethod.WeightedAverage);
        pool.Receive(3, 100);

        var issued = new[] { pool.Issue(1, false), pool.Issue(1, false), pool.Issue(1, false) };

        // 100/3 = 33.333 → 33.33; then 66.67/2 = 33.335 → 33.34 (half away from zero); the last unit takes the rest.
        Assert.Equal([33.33m, 33.34m, 33.33m], issued);
        Assert.Equal(100m, issued.Sum());
        Assert.Equal(0m, pool.Value);
    }

    [Fact]
    public void Fifo_consumes_the_oldest_layers_first()
    {
        var pool = new CostPool(CostingMethod.Fifo);
        pool.Receive(10, 1_000);
        pool.Receive(10, 1_300);

        Assert.Equal(1_260m, pool.Issue(12, allowNegative: false));
        Assert.Equal((8m, 1_040m), (pool.Quantity, pool.Value));
        Assert.Equal(130m, pool.CurrentUnitCost);
    }

    [Fact]
    public void Fifo_takes_a_partial_layer_at_its_own_rate()
    {
        var pool = new CostPool(CostingMethod.Fifo);
        pool.Receive(3, 100);

        Assert.Equal(33.33m, pool.Issue(1, false));
        Assert.Equal(66.67m, pool.Issue(2, false));
    }

    [Fact]
    public void Fifo_never_goes_negative()
    {
        var pool = new CostPool(CostingMethod.Fifo);
        pool.Receive(2, 200);
        Assert.Throws<DomainException>(() => pool.Issue(3, allowNegative: true));
    }

    [Fact]
    public void Weighted_average_negative_stock_uses_the_last_unit_cost()
    {
        var pool = new CostPool(CostingMethod.WeightedAverage);
        pool.Receive(2, 200);

        Assert.Throws<DomainException>(() => pool.Issue(3, allowNegative: false));
        Assert.Equal(300m, pool.Issue(3, allowNegative: true));
        Assert.Equal((-1m, -100m), (pool.Quantity, pool.Value));
    }

    [Fact]
    public void Replay_matches_the_recorded_ledger_and_skips_reversed_documents()
    {
        var reversed = Guid.NewGuid();
        var reversal = Guid.NewGuid();
        var movements = new List<StockMovement>
        {
            InventoryRuleTests.Movement(10, 1_000, sequence: 1),
            InventoryRuleTests.Movement(-4, -400, sequence: 2),
            InventoryRuleTests.Movement(5, 0, isInternal: true, sequence: 3),
            InventoryRuleTests.Movement(10, 2_000, sequence: 4, document: reversed),
            InventoryRuleTests.Movement(-10, -2_000, sequence: 5, document: reversal),
        };

        var pool = CostPool.Replay(CostingMethod.WeightedAverage, movements, new HashSet<Guid> { reversed, reversal });

        Assert.Equal((6m, 600m), (pool.Quantity, pool.Value));
        Assert.Equal(100m, pool.CurrentUnitCost);
    }
}
