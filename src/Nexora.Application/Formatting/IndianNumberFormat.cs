using System.Globalization;

namespace Nexora.Application.Formatting;

/// <summary>
/// Indian digit grouping (12,34,56,789.00) without depending on the OS culture data,
/// so amounts look the same on every Windows installation.
/// </summary>
public static class IndianNumberFormat
{
    public const string RupeeSymbol = "₹";

    private static readonly NumberFormatInfo Format = new()
    {
        NumberGroupSeparator = ",",
        NumberDecimalSeparator = ".",
        NumberGroupSizes = [3, 2],
        NegativeSign = "-",
    };

    /// <summary>Formats with grouping and the given number of decimals, for example 18,42,350.00.</summary>
    public static string Number(decimal value, int decimals = 2) =>
        value.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture), Format);

    /// <summary>Rupee amount, for example ₹18,42,350 or -₹1,250.50.</summary>
    public static string Amount(decimal value, int decimals = 0)
    {
        var magnitude = Number(Math.Abs(value), decimals);
        return value < 0 ? "-" + RupeeSymbol + magnitude : RupeeSymbol + magnitude;
    }
}
