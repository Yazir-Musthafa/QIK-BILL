using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ZXing.QrCode.Internal;

namespace Nexora.Printing.Barcodes;

/// <summary>A QR code as a square module matrix (true = dark), without the quiet zone (renderers add 4 modules).</summary>
public sealed record QrMatrix(string Content, bool[,] Modules)
{
    public const int QuietZoneModules = 4;

    public int Size => Modules.GetLength(0);
}

/// <summary>QR encoding (ISO/IEC 18004) through ZXing.Net's encoder: UTF-8 byte mode, error correction level M.</summary>
public static class QrCodes
{
    public const int MaxContentLength = 500;

    public static QrMatrix Encode(string content)
    {
        if (string.IsNullOrEmpty(content)) throw new ArgumentException("QR content is empty.", nameof(content));
        if (content.Length > MaxContentLength) throw new ArgumentException($"QR content can have at most {MaxContentLength} characters.", nameof(content));
        var hints = new Dictionary<ZXing.EncodeHintType, object> { [ZXing.EncodeHintType.CHARACTER_SET] = "UTF-8" };
        var code = ZXing.QrCode.Internal.Encoder.encode(content, ErrorCorrectionLevel.M, hints);
        var matrix = code.Matrix;
        var modules = new bool[matrix.Width, matrix.Height];
        for (var y = 0; y < matrix.Height; y++)
        for (var x = 0; x < matrix.Width; x++)
            modules[x, y] = matrix[x, y] == 1;
        return new QrMatrix(content, modules);
    }
}

/// <summary>
/// The configurable QR content format: plain text with placeholders in braces, for example <c>{sku}</c> or
/// <c>INV {number} {date} {total}</c>. Placeholder names are case-insensitive; an unknown name is an error, so a
/// typo is found when the format is saved, not on a printed label. Braces can't be escaped.
/// Labels offer: sku, name, barcode, price, mrp. Documents offer: number, date, total, gstin, party, company.
/// A QR code made this way is a reference for scanning only; it is not a GST e-invoice QR and does not make a
/// document GST-compliant.
/// </summary>
public static partial class QrContentFormat
{
    public static IReadOnlyList<string> LabelFields { get; } = ["sku", "name", "barcode", "price", "mrp"];

    public static IReadOnlyList<string> DocumentFields { get; } = ["number", "date", "total", "gstin", "party", "company"];

    /// <summary>Returns the error for <paramref name="format"/>, or null when it is valid (empty means "no QR code").</summary>
    public static string? Validate(string? format, IReadOnlyList<string> fields)
    {
        if (string.IsNullOrEmpty(format)) return null;
        if (format.Length > 200) return "The QR format can have at most 200 characters.";
        if (format.Count(c => c == '{') != format.Count(c => c == '}')) return "Every { in the QR format needs a matching }.";
        foreach (Match match in Placeholder().Matches(format))
        {
            var name = match.Groups[1].Value.ToLowerInvariant();
            if (!fields.Contains(name)) return $"{{{match.Groups[1].Value}}} is not a known field. Use: {string.Join(", ", fields.Select(f => "{" + f + "}"))}.";
        }

        return Placeholder().Replace(format, string.Empty).IndexOfAny(['{', '}']) >= 0 ? "Placeholders look like {sku}: letters only, inside braces." : null;
    }

    public static string Apply(string format, IReadOnlyDictionary<string, string> values)
    {
        var builder = new StringBuilder();
        var last = 0;
        foreach (Match match in Placeholder().Matches(format))
        {
            builder.Append(format, last, match.Index - last);
            builder.Append(values.TryGetValue(match.Groups[1].Value.ToLowerInvariant(), out var value) ? value : string.Empty);
            last = match.Index + match.Length;
        }

        builder.Append(format, last, format.Length - last);
        return builder.ToString().Trim();
    }

    public static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\{([A-Za-z]+)\}")]
    private static partial Regex Placeholder();
}
