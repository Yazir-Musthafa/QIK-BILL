using Nexora.Domain.Printing;

namespace Nexora.Printing.Layout;

public enum TextAlign
{
    Left,
    Center,
    Right,
}

/// <summary>
/// One thing drawn on a page. Coordinates are in points (1/72 inch) from the top-left corner of the page.
/// Every renderer (PDF, Windows print, on-screen preview) draws the same items, so what is tested is what prints.
/// </summary>
public abstract record PageItem;

/// <summary>Text on one line. <see cref="Y"/> is the top of the line box; <see cref="Width"/> is the box used for alignment.</summary>
public sealed record TextItem(double X, double Y, double Width, string Text, double Size, bool Bold = false, TextAlign Align = TextAlign.Left) : PageItem;

public sealed record LineItem(double X1, double Y1, double X2, double Y2, double Thickness = 0.5) : PageItem;

public sealed record RectItem(double X, double Y, double Width, double Height, bool Filled, double Thickness = 0.5) : PageItem;

/// <summary>A PNG or JPEG image, scaled to the box.</summary>
public sealed record ImageItem(double X, double Y, double Width, double Height, byte[] Data) : PageItem;

/// <summary>A 1D barcode: <see cref="Modules"/> spread evenly across <see cref="Width"/> (quiet zones are outside the box).</summary>
public sealed record BarcodeItem(double X, double Y, double Width, double Height, bool[] Modules) : PageItem;

/// <summary>A square 2D code (QR) of side <see cref="Size"/>.</summary>
public sealed record MatrixItem(double X, double Y, double Size, bool[,] Modules) : PageItem;

public sealed record RenderedPage(double Width, double Height, IReadOnlyList<PageItem> Items);

public sealed record RenderedDocument(string Title, IReadOnlyList<RenderedPage> Pages)
{
    public IEnumerable<TextItem> Texts => Pages.SelectMany(p => p.Items.OfType<TextItem>());

    /// <summary>All text of the document, page by page, for searching in tests and previews.</summary>
    public string AllText => string.Join("\n", Texts.Select(t => t.Text));
}

public sealed record Margins(double Top, double Right, double Bottom, double Left)
{
    public static Margins Uniform(double mm) => new(mm, mm, mm, mm);
}

/// <summary>
/// Page setup for one print: paper, orientation, margins (mm), scale (percent) and base font size (points).
/// Thermal rolls have no fixed height: the page is as long as its content.
/// </summary>
public sealed record PageSetup(PaperKind Paper, PageOrientation Orientation, Margins MarginsMm, double ScalePercent = 100, double FontSize = 9)
{
    public const double PointsPerMm = 72 / 25.4;

    public static PageSetup Default(PaperKind paper) => paper switch
    {
        PaperKind.Thermal58 => new(paper, PageOrientation.Portrait, new Margins(3, 2, 3, 2), 100, 7.5),
        PaperKind.Thermal80 => new(paper, PageOrientation.Portrait, new Margins(3, 3, 3, 3), 100, 8),
        PaperKind.A5 => new(paper, PageOrientation.Portrait, Margins.Uniform(10), 100, 8),
        _ => new(paper, PageOrientation.Portrait, Margins.Uniform(12), 100, 9),
    };

    public bool IsRoll => Paper is PaperKind.Thermal58 or PaperKind.Thermal80;

    /// <summary>Narrow layouts (rolls) stack lines instead of using table columns.</summary>
    public bool IsNarrow => IsRoll || ContentWidth < 90 * PointsPerMm;

    public double PageWidth => (Orientation == PageOrientation.Landscape && !IsRoll ? PaperHeightMm : PaperWidthMm) * PointsPerMm;

    /// <summary>Null for rolls.</summary>
    public double? PageHeight => IsRoll ? null : (Orientation == PageOrientation.Landscape ? PaperWidthMm : PaperHeightMm) * PointsPerMm;

    /// <summary>Width available to content in layout units (before scaling).</summary>
    public double ContentWidth => (PageWidth - (MarginsMm.Left + MarginsMm.Right) * PointsPerMm) / Scale;

    /// <summary>Height available per page in layout units; infinite on rolls.</summary>
    public double ContentHeight => PageHeight is { } h ? (h - (MarginsMm.Top + MarginsMm.Bottom) * PointsPerMm) / Scale : double.PositiveInfinity;

    public double Scale => ScalePercent / 100;

    private double PaperWidthMm => Paper switch
    {
        PaperKind.A5 => 148,
        PaperKind.Thermal58 => 58,
        PaperKind.Thermal80 => 80,
        _ => 210,
    };

    private double PaperHeightMm => Paper switch
    {
        PaperKind.A5 => 210,
        _ => 297,
    };

    public void Validate()
    {
        if (ScalePercent is < 25 or > 200) throw new ArgumentException("Scale must be between 25% and 200%.");
        if (FontSize is < 6 or > 16) throw new ArgumentException("Font size must be between 6 and 16 points.");
        if (new[] { MarginsMm.Top, MarginsMm.Right, MarginsMm.Bottom, MarginsMm.Left }.Any(m => m is < 0 or > 50)) throw new ArgumentException("Margins must be between 0 and 50 mm.");
        if (ContentWidth < 30 * PointsPerMm / Scale * 0.5) throw new ArgumentException("The margins leave too little room on this paper.");
    }
}
