using Nexora.Domain.Common;

namespace Nexora.Domain.Accounting;

/// <summary>
/// Money rules. All amounts are <see cref="decimal"/> rupees with at most two decimal places (paise).
///
/// Rounding policy:
/// <list type="bullet">
/// <item>Amounts stored in vouchers and the ledger are never rounded implicitly; more than two decimals is rejected.</item>
/// <item>Calculations that produce fractions of a paisa (tax, discounts, unit price × quantity in later phases)
/// round with <see cref="Round"/>: half away from zero to two decimals (₹0.005 → ₹0.01, −₹0.005 → −₹0.01).</item>
/// <item>Rounding a document total to whole rupees, when a company chooses it, is posted to the Round Off account
/// as a separate line so the ledger always shows the difference.</item>
/// </list>
/// </summary>
public static class Money
{
    /// <summary>Largest single amount accepted: ₹999,99,99,99,999.99 (just under one lakh crore).</summary>
    public const decimal MaxAmount = 99_999_999_999.99m;

    public static decimal Round(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    public static bool HasValidScale(decimal amount) => decimal.Round(amount, 2) == amount;

    /// <summary>Throws unless the amount is non-negative, at most <see cref="MaxAmount"/> and has at most two decimals.</summary>
    public static decimal EnsureValid(decimal amount, string what = "Amount")
    {
        if (amount < 0) throw new DomainException($"{what} cannot be negative.");
        if (amount > MaxAmount) throw new DomainException($"{what} is too large.");
        if (!HasValidScale(amount)) throw new DomainException($"{what} can have at most 2 decimal places (paise).");
        return amount;
    }

    /// <summary>Whole paise, used for exact storage.</summary>
    public static long ToPaise(decimal amount)
    {
        if (!HasValidScale(amount)) throw new DomainException("Amounts can have at most 2 decimal places (paise).");
        return (long)(amount * 100m);
    }

    public static decimal FromPaise(long paise) => paise / 100m;
}
