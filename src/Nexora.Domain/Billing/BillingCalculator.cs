using Nexora.Domain.Accounting;
using Nexora.Domain.Common;

namespace Nexora.Domain.Billing;

/// <summary>Money amounts of one document line. Tax is CGST + SGST (intra-state) or IGST (inter-state).</summary>
public sealed record LineAmounts(decimal Gross, decimal Discount, decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst)
{
    public static LineAmounts Zero { get; } = new(0, 0, 0, 0, 0, 0);

    public decimal Tax => Cgst + Sgst + Igst;

    public decimal Total => Taxable + Tax;

    public LineAmounts Plus(LineAmounts other) =>
        new(Gross + other.Gross, Discount + other.Discount, Taxable + other.Taxable, Cgst + other.Cgst, Sgst + other.Sgst, Igst + other.Igst);

    public LineAmounts Minus(LineAmounts other) =>
        new(Gross - other.Gross, Discount - other.Discount, Taxable - other.Taxable, Cgst - other.Cgst, Sgst - other.Sgst, Igst - other.Igst);
}

/// <summary>Totals of a document. <see cref="Total"/> includes the round-off.</summary>
public sealed record DocumentTotals(decimal Gross, decimal Discount, decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst, decimal RoundOff)
{
    public decimal Tax => Cgst + Sgst + Igst;

    public decimal Total => Taxable + Tax + RoundOff;
}

/// <summary>
/// The billing rounding and tax policy, in one place so documents, returns and tests agree. Amounts are recalculated
/// here from quantities, rates and the configured tax rate; totals sent from a screen are never trusted.
/// <list type="number">
/// <item>Gross = quantity × rate, rounded half away from zero to the paisa (<see cref="Money.Round"/>).</item>
/// <item>Discount is a percentage of gross (rounded) or a fixed amount; never more than gross.</item>
/// <item>Tax-exclusive rates: taxable = gross − discount; tax is calculated per line on the taxable value.</item>
/// <item>Tax-inclusive rates: taxable = net × 100 / (100 + rate), rounded; tax is calculated from that taxable value
/// and the taxable value is then set to net − tax, so the line total equals the price charged exactly.</item>
/// <item>Intra-state: CGST and SGST are each half the rate, rounded separately (so they are always equal).
/// Inter-state: IGST at the full rate.</item>
/// <item>The document total may be rounded to whole rupees (half away from zero); the difference is the round-off.</item>
/// </list>
/// The rate is the tax category's configured rate; GST rates are data, never code.
/// </summary>
public static class BillingCalculator
{
    public static LineAmounts Line(decimal quantity, decimal rate, decimal discountPercent, decimal discountAmount, decimal taxRatePercent,
        bool interState, bool pricesIncludeTax)
    {
        if (quantity <= 0 || decimal.Round(quantity, 3) != quantity) throw new DomainException("Quantity must be more than zero, with at most 3 decimal places.");
        Money.EnsureValid(rate, "Rate");
        Money.EnsureValid(discountAmount, "Discount");
        if (discountPercent is < 0 or > 100 || decimal.Round(discountPercent, 2) != discountPercent)
            throw new DomainException("Discount % must be between 0 and 100, with at most 2 decimal places.");
        if (discountPercent > 0 && discountAmount > 0) throw new DomainException("Enter the discount as a percentage or an amount, not both.");
        if (taxRatePercent is < 0 or > 100) throw new DomainException("The tax rate must be between 0 and 100 percent.");

        var gross = Money.Round(quantity * rate);
        if (gross > Money.MaxAmount) throw new DomainException("The line amount is too large.");
        var discount = discountAmount > 0 ? discountAmount : Money.Round(gross * discountPercent / 100m);
        if (discount > gross) throw new DomainException("The discount cannot be more than the line amount.");
        var net = gross - discount;

        if (!pricesIncludeTax)
        {
            var (cgst, sgst, igst) = Taxes(net, taxRatePercent, interState);
            return new LineAmounts(gross, discount, net, cgst, sgst, igst);
        }

        var estimate = Money.Round(net * 100m / (100m + taxRatePercent));
        var (c, s, i) = Taxes(estimate, taxRatePercent, interState);
        return new LineAmounts(gross, discount, net - c - s - i, c, s, i);
    }

    public static decimal RoundOff(decimal total, bool roundToRupee) =>
        roundToRupee ? decimal.Round(total, 0, MidpointRounding.AwayFromZero) - total : 0;

    public static DocumentTotals Totals(IEnumerable<LineAmounts> lines, bool roundToRupee)
    {
        var sum = lines.Aggregate(LineAmounts.Zero, (a, l) => a.Plus(l));
        return new DocumentTotals(sum.Gross, sum.Discount, sum.Taxable, sum.Cgst, sum.Sgst, sum.Igst, RoundOff(sum.Total, roundToRupee));
    }

    /// <summary>
    /// The share of an original line returned now: proportional to quantity, rounded to the paisa, except that the last
    /// return of a line takes exactly what remains, so returning everything always reverses the original amounts exactly.
    /// </summary>
    public static LineAmounts Portion(LineAmounts original, decimal originalQuantity, LineAmounts alreadyReturned, decimal alreadyReturnedQuantity,
        decimal quantity)
    {
        if (quantity <= 0) throw new DomainException("Quantity must be more than zero.");
        if (alreadyReturnedQuantity + quantity > originalQuantity)
            throw new DomainException($"Only {originalQuantity - alreadyReturnedQuantity:0.###} can still be returned.");
        if (alreadyReturnedQuantity + quantity == originalQuantity) return original.Minus(alreadyReturned);
        decimal Share(decimal amount) => Money.Round(amount * quantity / originalQuantity);
        return new LineAmounts(Share(original.Gross), Share(original.Discount), Share(original.Taxable), Share(original.Cgst), Share(original.Sgst), Share(original.Igst));
    }

    /// <summary>The same proportional rule for a single amount, such as the cost of goods returned.</summary>
    public static decimal PortionOf(decimal original, decimal originalQuantity, decimal alreadyReturned, decimal alreadyReturnedQuantity, decimal quantity) =>
        alreadyReturnedQuantity + quantity == originalQuantity ? original - alreadyReturned : Money.Round(original * quantity / originalQuantity);

    private static (decimal Cgst, decimal Sgst, decimal Igst) Taxes(decimal taxable, decimal ratePercent, bool interState)
    {
        if (interState) return (0, 0, Money.Round(taxable * ratePercent / 100m));
        var half = Money.Round(taxable * ratePercent / 200m);
        return (half, half, 0);
    }
}
