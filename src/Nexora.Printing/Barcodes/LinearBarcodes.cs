using System.Text;
using Nexora.Domain.Inventory;

namespace Nexora.Printing.Barcodes;

/// <summary>
/// A one-dimensional barcode as modules (true = bar), without quiet zones. Renderers add a quiet zone of
/// <see cref="QuietZoneModules"/> on each side and draw each module at the same width.
/// </summary>
public sealed record LinearBarcode(string Symbology, string Text, bool[] Modules)
{
    /// <summary>Recommended light margin on each side, in modules (Code 128: 10; EAN-13: 11 left, 7 right, 11 used for both).</summary>
    public int QuietZoneModules => Symbology == Ean13.Name ? 11 : 10;
}

/// <summary>
/// Code 128 (ISO/IEC 15417) for printable ASCII. Code set B is used for text and code set C for runs of digits
/// (four or more, or the whole value when it is an even number of digits), which keeps numeric codes short.
/// </summary>
public static class Code128
{
    public const string Name = "Code 128";

    private const int StartB = 104;
    private const int StartC = 105;
    private const int SwitchToB = 100;
    private const int SwitchToC = 99;

    // Bar and space widths of each symbol value 0–105; every pattern is 11 modules wide.
    private static readonly string[] Patterns =
    [
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
        "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
        "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
        "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
        "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
        "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
        "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
        "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
        "114131", "311141", "411131", "211412", "211214", "211232",
    ];

    private const string Stop = "2331112";

    public static bool CanEncode(string? text) => !string.IsNullOrEmpty(text) && text.Length <= 80 && text.All(c => c is >= ' ' and <= '~');

    /// <summary>The symbol values including start and check character, but not the stop pattern.</summary>
    public static IReadOnlyList<int> Values(string text)
    {
        if (!CanEncode(text)) throw new ArgumentException("Code 128 here encodes 1 to 80 printable ASCII characters (space to ~).", nameof(text));
        var values = new List<int>();
        var i = 0;
        var inC = DigitRun(text, 0) >= 4 || (DigitRun(text, 0) == text.Length && text.Length % 2 == 0);
        values.Add(inC ? StartC : StartB);
        while (i < text.Length)
        {
            if (inC)
            {
                if (DigitRun(text, i) >= 2)
                {
                    values.Add((text[i] - '0') * 10 + (text[i + 1] - '0'));
                    i += 2;
                    continue;
                }

                values.Add(SwitchToB);
                inC = false;
                continue;
            }

            // Switch to C for a run of at least 4 digits that can be consumed in pairs (an odd run starts with one digit in B).
            var run = DigitRun(text, i);
            if (run >= 4 && run % 2 == 0)
            {
                values.Add(SwitchToC);
                inC = true;
                continue;
            }

            values.Add(text[i] - 32);
            i++;
        }

        var checksum = values[0];
        for (var position = 1; position < values.Count; position++) checksum += values[position] * position;
        values.Add(checksum % 103);
        return values;
    }

    public static LinearBarcode Encode(string text)
    {
        var modules = new List<bool>();
        foreach (var value in Values(text)) Append(modules, Patterns[value]);
        Append(modules, Stop);
        return new LinearBarcode(Name, text, [.. modules]);
    }

    private static int DigitRun(string text, int start)
    {
        var end = start;
        while (end < text.Length && char.IsAsciiDigit(text[end])) end++;
        return end - start;
    }

    private static void Append(List<bool> modules, string widths)
    {
        var bar = true;
        foreach (var width in widths)
        {
            for (var k = 0; k < width - '0'; k++) modules.Add(bar);
            bar = !bar;
        }
    }
}

/// <summary>EAN-13 (GS1): 12 data digits and a mod-10 check digit; 95 modules between the quiet zones.</summary>
public static class Ean13
{
    public const string Name = "EAN-13";

    private static readonly string[] L = ["0001101", "0011001", "0010011", "0111101", "0100011", "0110001", "0101111", "0111011", "0110111", "0001011"];
    private static readonly string[] Parity = ["LLLLLL", "LLGLGG", "LLGGLG", "LLGGGL", "LGLLGG", "LGGLLG", "LGGGLL", "LGLGLG", "LGLGGL", "LGGLGL"];

    /// <summary>Thirteen digits with a correct check digit (the same rule products use: <see cref="Nexora.Domain.Inventory.Barcodes.IsValidEan"/>).</summary>
    public static bool IsValid(string? digits) => digits is { Length: 13 } && Nexora.Domain.Inventory.Barcodes.IsValidEan(digits);

    /// <summary>The check digit for 12 data digits: weights 1 and 3 alternating from the left, then 10 minus the sum mod 10.</summary>
    public static int CheckDigit(string twelveDigits)
    {
        if (twelveDigits is not { Length: 12 } || !twelveDigits.All(char.IsAsciiDigit)) throw new ArgumentException("Enter exactly 12 digits.", nameof(twelveDigits));
        var sum = 0;
        for (var i = 0; i < 12; i++) sum += (twelveDigits[i] - '0') * (i % 2 == 0 ? 1 : 3);
        return (10 - sum % 10) % 10;
    }

    public static LinearBarcode Encode(string digits)
    {
        if (!IsValid(digits)) throw new ArgumentException("An EAN-13 barcode needs 13 digits with a correct check digit.", nameof(digits));
        var bits = new StringBuilder("101");
        var parity = Parity[digits[0] - '0'];
        for (var i = 1; i <= 6; i++)
        {
            var left = L[digits[i] - '0'];
            bits.Append(parity[i - 1] == 'L' ? left : Reverse(Complement(left)));
        }

        bits.Append("01010");
        for (var i = 7; i <= 12; i++) bits.Append(Complement(L[digits[i] - '0']));
        bits.Append("101");
        return new LinearBarcode(Name, digits, bits.ToString().Select(c => c == '1').ToArray());
    }

    private static string Complement(string bits) => new([.. bits.Select(c => c == '1' ? '0' : '1')]);

    private static string Reverse(string bits) => new([.. bits.Reverse()]);
}

/// <summary>Chooses the symbology for a product code: EAN-13 when it is a valid EAN-13, otherwise Code 128.</summary>
public static class ProductBarcode
{
    public static LinearBarcode? For(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var clean = code.Trim();
        if (Ean13.IsValid(clean)) return Ean13.Encode(clean);
        return Code128.CanEncode(clean) ? Code128.Encode(clean) : null;
    }
}
