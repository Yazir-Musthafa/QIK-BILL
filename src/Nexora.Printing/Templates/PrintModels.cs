using System.Globalization;
using System.Text;
using Nexora.Application.Formatting;

namespace Nexora.Printing.Templates;

/// <summary>The issuing company as printed: name, address, GSTIN, state and logo.</summary>
public sealed record PrintCompany(string Name, IReadOnlyList<string> AddressLines, string Gstin, string StateCode, string StateName, string Phone,
    string Email, byte[]? Logo);

/// <summary>The other party as printed. Address and phone come from the party record when printed; name and GSTIN from the posted document.</summary>
public sealed record PrintParty(string Name, IReadOnlyList<string> AddressLines, string Gstin, string StateName, string Phone);

/// <summary>Which document a billing record prints as.</summary>
public enum DocumentStyle
{
    /// <summary>Tax invoice or cash bill, credit note, purchase bill or debit note, by the document's kind.</summary>
    Standard,

    /// <summary>A delivery note from a sales invoice: goods and quantities, no prices.</summary>
    DeliveryNote,

    /// <summary>A purchase order from a draft purchase bill (QIK Suite has no purchase order workflow yet).</summary>
    PurchaseOrder,
}

/// <summary>
/// Extra marks on a print: a banner (DUPLICATE, DRAFT, CANCELLED), an optional reference QR, and the company footer.
/// None of these change any figure.
/// </summary>
public sealed record PrintMarks(IReadOnlyList<string> Banners, string? QrContent = null, string FooterNote = "")
{
    public static PrintMarks None { get; } = new([]);
}

internal static class Fmt
{
    public static string Money(decimal amount) => IndianNumberFormat.Number(amount);

    public static string Rupees(decimal amount) => "₹ " + IndianNumberFormat.Number(amount);

    public static string Date(DateOnly date) => date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

    public static string Quantity(decimal quantity) => quantity.ToString("0.###", CultureInfo.InvariantCulture);

    public static string Percent(decimal rate) => rate.ToString("0.##", CultureInfo.InvariantCulture) + "%";
}

/// <summary>Amounts in words in the Indian system (thousand, lakh, crore), as printed under invoice totals.</summary>
public static class AmountInWords
{
    private static readonly string[] Ones =
    [
        "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen",
        "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen",
    ];

    private static readonly string[] Tens = ["", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"];

    /// <summary>For example 1,06,250.50 → "Rupees One Lakh Six Thousand Two Hundred Fifty and Fifty Paise Only".</summary>
    public static string Rupees(decimal amount)
    {
        var negative = amount < 0;
        amount = Math.Abs(decimal.Round(amount, 2, MidpointRounding.AwayFromZero));
        var rupees = (long)decimal.Truncate(amount);
        var paise = (int)((amount - rupees) * 100);
        var text = new StringBuilder(negative ? "Minus Rupees " : "Rupees ");
        text.Append(Words(rupees));
        if (paise > 0) text.Append(" and ").Append(Words(paise)).Append(" Paise");
        return text.Append(" Only").ToString();
    }

    public static string Words(long number)
    {
        if (number == 0) return Ones[0];
        var parts = new List<string>();
        void Group(long value, string name)
        {
            if (value > 0) parts.Add(BelowThousand((int)value) + (name.Length > 0 ? " " + name : string.Empty));
        }

        var crore = number / 10_000_000;
        if (crore > 0) parts.Add(Words(crore) + " Crore");
        number %= 10_000_000;
        Group(number / 100_000, "Lakh");
        number %= 100_000;
        Group(number / 1_000, "Thousand");
        number %= 1_000;
        Group(number, string.Empty);
        return string.Join(" ", parts);
    }

    private static string BelowThousand(int value)
    {
        var parts = new List<string>();
        if (value >= 100)
        {
            parts.Add(Ones[value / 100] + " Hundred");
            value %= 100;
        }

        if (value >= 20) parts.Add(Tens[value / 10] + (value % 10 > 0 ? "-" + Ones[value % 10] : string.Empty));
        else if (value > 0) parts.Add(Ones[value]);
        return string.Join(" ", parts);
    }
}
