using Nexora.Domain.Billing;
using Nexora.Domain.Common;

namespace Nexora.Tests.Billing;

/// <summary>The billing rounding and tax policy (see BillingCalculator).</summary>
public sealed class BillingCalculatorTests
{
    [Fact]
    public void Tax_exclusive_line_rounds_each_step_to_the_paisa()
    {
        var line = BillingCalculator.Line(3, 99.99m, discountPercent: 10, discountAmount: 0, taxRatePercent: 18, interState: false, pricesIncludeTax: false);

        // 3 × 99.99 = 299.97; 10% = 29.997 → 30.00; taxable 269.97; 9% = 24.2973 → 24.30 each.
        Assert.Equal((299.97m, 30.00m, 269.97m, 24.30m, 24.30m, 0m), (line.Gross, line.Discount, line.Taxable, line.Cgst, line.Sgst, line.Igst));
        Assert.Equal(318.57m, line.Total);
    }

    [Fact]
    public void Inter_state_line_uses_igst_at_the_full_rate()
    {
        var line = BillingCalculator.Line(2, 150, 0, 0, 18, interState: true, pricesIncludeTax: false);

        Assert.Equal((300m, 0m, 0m, 54m), (line.Taxable, line.Cgst, line.Sgst, line.Igst));
    }

    [Theory]
    [InlineData(118, 100, 9, 9)]          // exact
    [InlineData(100, 84.74, 7.63, 7.63)]  // 84.745… → 84.75; tax 7.63 + 7.63; taxable set to 100 − 15.26
    [InlineData(0.01, 0.01, 0, 0)]
    public void Tax_inclusive_line_total_equals_the_price_charged(decimal price, decimal taxable, decimal cgst, decimal sgst)
    {
        var line = BillingCalculator.Line(1, price, 0, 0, 18, interState: false, pricesIncludeTax: true);

        Assert.Equal((taxable, cgst, sgst), (line.Taxable, line.Cgst, line.Sgst));
        Assert.Equal(price, line.Total);
        Assert.Equal(line.Cgst, line.Sgst);
    }

    [Fact]
    public void Fixed_discount_reduces_the_taxable_value()
    {
        var line = BillingCalculator.Line(1, 1_000, 0, discountAmount: 50, 5, false, false);

        Assert.Equal((950m, 23.75m, 23.75m, 997.50m), (line.Taxable, line.Cgst, line.Sgst, line.Total));
    }

    [Theory]
    [InlineData(0, 1, 0, 0)]       // quantity zero
    [InlineData(1, 1, 101, 0)]     // discount over 100%
    [InlineData(1, 1, 10, 0.5)]    // both kinds of discount
    [InlineData(1, 1, 0, 2)]       // discount over the amount
    [InlineData(1, 1.001, 0, 0)]   // rate below a paisa
    [InlineData(1.0001, 1, 0, 0)]  // quantity with 4 decimals
    public void Invalid_lines_are_rejected(decimal quantity, decimal rate, decimal percent, decimal amount)
    {
        Assert.Throws<DomainException>(() => BillingCalculator.Line(quantity, rate, percent, amount, 18, false, false));
    }

    [Theory]
    [InlineData(318.57, 0.43)]
    [InlineData(318.50, 0.50)]     // half away from zero
    [InlineData(318.49, -0.49)]
    [InlineData(318.00, 0)]
    public void Totals_round_to_whole_rupees_half_away_from_zero(decimal total, decimal roundOff)
    {
        Assert.Equal(roundOff, BillingCalculator.RoundOff(total, roundToRupee: true));
        Assert.Equal(0m, BillingCalculator.RoundOff(total, roundToRupee: false));
    }

    [Fact]
    public void Document_totals_sum_lines_and_add_the_round_off()
    {
        var a = BillingCalculator.Line(3, 99.99m, 10, 0, 18, false, false);
        var b = BillingCalculator.Line(1, 10, 0, 0, 5, false, false);

        var totals = BillingCalculator.Totals([a, b], roundToRupee: true);

        Assert.Equal(279.97m, totals.Taxable);
        Assert.Equal(24.30m * 2 + 0.25m * 2, totals.Tax);
        Assert.Equal(-0.07m, totals.RoundOff);              // 329.07 → 329
        Assert.Equal(329m, totals.Total);
    }

    [Fact]
    public void Returning_a_line_in_parts_reverses_the_original_amounts_exactly()
    {
        var original = BillingCalculator.Line(3, 33.33m, 0, 0, 18, false, false);   // taxable 99.99; 9% = 8.9991 → 9.00

        var first = BillingCalculator.Portion(original, 3, LineAmounts.Zero, 0, 1);
        var second = BillingCalculator.Portion(original, 3, first, 1, 1);
        var last = BillingCalculator.Portion(original, 3, first.Plus(second), 2, 1);

        Assert.Equal((33.33m, 3.00m), (first.Taxable, first.Cgst));
        Assert.Equal(original, first.Plus(second).Plus(last));
        Assert.Throws<DomainException>(() => BillingCalculator.Portion(original, 3, first.Plus(second).Plus(last), 3, 1));
    }

    [Fact]
    public void Cost_portions_follow_the_same_rule()
    {
        Assert.Equal(33.33m, BillingCalculator.PortionOf(100, 3, 0, 0, 1));
        Assert.Equal(33.34m, BillingCalculator.PortionOf(100, 3, 66.66m, 2, 1));
    }
}
