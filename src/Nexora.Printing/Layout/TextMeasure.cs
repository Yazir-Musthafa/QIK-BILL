using System.Reflection;
using PdfSharp.Drawing;
using PdfSharp.Fonts;

namespace Nexora.Printing.Layout;

/// <summary>Text widths for layout. One implementation (the bundled font) is used everywhere, so layout is deterministic.</summary>
public interface ITextMeasurer
{
    double Width(string text, double size, bool bold);
}

/// <summary>
/// The bundled Noto Sans font (SIL Open Font License 1.1, see Fonts/OFL.txt). It is used for measuring, for PDF output
/// and for Windows printing, so a document looks and paginates the same everywhere, including the ₹ sign.
/// </summary>
public sealed class PrintFonts : IFontResolver, ITextMeasurer
{
    public const string Family = "Noto Sans";
    private const string RegularFace = "NotoSans-Regular";
    private const string BoldFace = "NotoSans-Bold";
    private static readonly object Gate = new();
    private static PrintFonts? _instance;
    private readonly Dictionary<string, byte[]> _faces;
    private readonly XGraphics _measure;
    private readonly Dictionary<(double, bool), XFont> _fonts = [];

    private PrintFonts()
    {
        _faces = new Dictionary<string, byte[]> { [RegularFace] = Load(RegularFace), [BoldFace] = Load(BoldFace) };
        GlobalFontSettings.FontResolver = this;
        _measure = XGraphics.CreateMeasureContext(new XSize(2000, 2000), XGraphicsUnit.Point, XPageDirection.Downwards);
    }

    /// <summary>The single instance; registering a font resolver with PDFsharp is allowed once per process.</summary>
    public static PrintFonts Instance
    {
        get
        {
            lock (Gate) return _instance ??= new PrintFonts();
        }
    }

    public static byte[] Regular => Instance._faces[RegularFace];

    public static byte[] Bold => Instance._faces[BoldFace];

    public double Width(string text, double size, bool bold)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        lock (Gate) return _measure.MeasureString(text, Font(size, bold)).Width;
    }

    internal XFont Font(double size, bool bold)
    {
        lock (Gate)
        {
            if (!_fonts.TryGetValue((size, bold), out var font))
            {
                font = new XFont(Family, size, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);
                _fonts[(size, bold)] = font;
            }

            return font;
        }
    }

    public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) => new(bold ? BoldFace : RegularFace);

    public byte[] GetFont(string faceName) => _faces[faceName];

    private static byte[] Load(string face)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Nexora.Printing.Fonts.{face}.ttf")
            ?? throw new InvalidOperationException($"The bundled font {face} is missing from Nexora.Printing.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}

/// <summary>Word wrapping and truncation with an <see cref="ITextMeasurer"/>.</summary>
public static class TextFit
{
    public static IReadOnlyList<string> Wrap(ITextMeasurer measure, string text, double width, double size, bool bold = false, int maxLines = int.MaxValue)
    {
        var lines = new List<string>();
        foreach (var paragraph in (text ?? string.Empty).Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            var current = string.Empty;
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = current.Length == 0 ? word : current + " " + word;
                if (measure.Width(candidate, size, bold) <= width)
                {
                    current = candidate;
                    continue;
                }

                if (current.Length > 0) lines.Add(current);
                current = word;
                // A single word wider than the box is broken by characters.
                while (measure.Width(current, size, bold) > width && current.Length > 1)
                {
                    var cut = current.Length - 1;
                    while (cut > 1 && measure.Width(current[..cut], size, bold) > width) cut--;
                    lines.Add(current[..cut]);
                    current = current[cut..];
                }
            }

            lines.Add(current);
        }

        if (lines.Count > maxLines)
        {
            lines = lines.Take(maxLines).ToList();
            lines[^1] = Truncate(measure, lines[^1] + "…", width, size, bold);
        }

        return lines;
    }

    public static string Truncate(ITextMeasurer measure, string text, double width, double size, bool bold = false)
    {
        if (measure.Width(text, size, bold) <= width) return text;
        var cut = text.Length;
        while (cut > 0 && measure.Width(text[..cut] + "…", size, bold) > width) cut--;
        return text[..cut] + "…";
    }
}
