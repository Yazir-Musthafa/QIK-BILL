namespace Nexora.Printing.Layout;

/// <summary>
/// Flow layout onto pages. Templates place items relative to the content area at <see cref="Y"/> and move down;
/// <see cref="Ensure"/> starts a new page when the next block does not fit, repeating the continuation header
/// (for example a table header). <see cref="Finish"/> applies margins and scale, sizes roll paper to its content
/// and adds "Page n of N" footers. All of this is plain arithmetic, so it is unit-tested without a printer.
/// </summary>
public sealed class PageComposer
{
    private readonly List<List<PageItem>> _pages = [[]];
    private readonly List<double> _heights = [0];
    private readonly bool _pageNumbers;

    public PageComposer(PageSetup setup, ITextMeasurer measure, bool pageNumbers = true)
    {
        setup.Validate();
        Setup = setup;
        Measure = measure;
        _pageNumbers = pageNumbers && !setup.IsRoll;
    }

    public PageSetup Setup { get; }

    public ITextMeasurer Measure { get; }

    public double Width => Setup.ContentWidth;

    public double FontSize => Setup.FontSize;

    public double LineHeight(double size) => size * 1.25;

    /// <summary>Current position from the top of the content area, in layout units.</summary>
    public double Y { get; set; }

    public int PageCount => _pages.Count;

    /// <summary>Drawn at the top of every page after the first (continuation header).</summary>
    public Action<PageComposer>? ContinuationHeader { get; set; }

    /// <summary>Space kept free at the bottom of each page for the page-number footer.</summary>
    private double FooterReserve => _pageNumbers ? LineHeight(FontSize * 0.85) + 4 : 0;

    private double Bottom => Setup.ContentHeight - FooterReserve;

    public bool Fits(double height) => Y + height <= Bottom + 0.001;

    /// <summary>Starts a new page unless <paramref name="height"/> still fits (a block taller than a page is placed anyway).</summary>
    public void Ensure(double height)
    {
        if (Fits(height) || Y == 0) return;
        NewPage();
    }

    public void NewPage()
    {
        _heights[^1] = Math.Max(_heights[^1], Y);
        _pages.Add([]);
        _heights.Add(0);
        Y = 0;
        ContinuationHeader?.Invoke(this);
    }

    public void Add(PageItem item)
    {
        _pages[^1].Add(item);
        _heights[^1] = Math.Max(_heights[^1], ItemBottom(item));
    }

    public void Text(double x, double width, string text, double size, bool bold = false, TextAlign align = TextAlign.Left) =>
        Add(new TextItem(x, Y, width, text, size, bold, align));

    /// <summary>Wrapped text at the current position; returns the height used (Y is not moved).</summary>
    public double Wrapped(double x, double width, string text, double size, bool bold = false, TextAlign align = TextAlign.Left, int maxLines = int.MaxValue)
    {
        var lines = TextFit.Wrap(Measure, text, width, size, bold, maxLines);
        for (var i = 0; i < lines.Count; i++) Add(new TextItem(x, Y + i * LineHeight(size), width, lines[i], size, bold, align));
        return lines.Count * LineHeight(size);
    }

    public void Rule(double thickness = 0.5, double from = 0, double? to = null)
    {
        Add(new LineItem(from, Y, to ?? Width, Y, thickness));
    }

    public void Move(double height) => Y += height;

    public RenderedDocument Finish(string title)
    {
        _heights[^1] = Math.Max(_heights[^1], Y);
        var s = Setup.Scale;
        var left = Setup.MarginsMm.Left * PageSetup.PointsPerMm;
        var top = Setup.MarginsMm.Top * PageSetup.PointsPerMm;
        var bottom = Setup.MarginsMm.Bottom * PageSetup.PointsPerMm;
        var pages = new List<RenderedPage>();
        for (var p = 0; p < _pages.Count; p++)
        {
            var items = _pages[p].Select(i => Transform(i, left, top, s)).ToList();
            var height = Setup.PageHeight ?? top + _heights[p] * s + bottom;
            if (_pageNumbers && _pages.Count > 1)
            {
                var size = FontSize * 0.85;
                items.Add(Transform(new TextItem(0, Setup.ContentHeight - LineHeight(size), Width, $"Page {p + 1} of {_pages.Count}", size, false, TextAlign.Right), left, top, s));
            }

            pages.Add(new RenderedPage(Setup.PageWidth, height, items));
        }

        return new RenderedDocument(title, pages);
    }

    private static double ItemBottom(PageItem item) => item switch
    {
        TextItem t => t.Y + t.Size * 1.25,
        LineItem l => Math.Max(l.Y1, l.Y2),
        RectItem r => r.Y + r.Height,
        ImageItem i => i.Y + i.Height,
        BarcodeItem b => b.Y + b.Height,
        MatrixItem m => m.Y + m.Size,
        _ => 0,
    };

    /// <summary>Moves an item from content coordinates to page coordinates: offset by the margins, scaled by the setup.</summary>
    public static PageItem Transform(PageItem item, double dx, double dy, double s) => item switch
    {
        TextItem t => t with { X = dx + t.X * s, Y = dy + t.Y * s, Width = t.Width * s, Size = t.Size * s },
        LineItem l => l with { X1 = dx + l.X1 * s, Y1 = dy + l.Y1 * s, X2 = dx + l.X2 * s, Y2 = dy + l.Y2 * s, Thickness = l.Thickness * s },
        RectItem r => r with { X = dx + r.X * s, Y = dy + r.Y * s, Width = r.Width * s, Height = r.Height * s, Thickness = r.Thickness * s },
        ImageItem i => i with { X = dx + i.X * s, Y = dy + i.Y * s, Width = i.Width * s, Height = i.Height * s },
        BarcodeItem b => b with { X = dx + b.X * s, Y = dy + b.Y * s, Width = b.Width * s, Height = b.Height * s },
        MatrixItem m => m with { X = dx + m.X * s, Y = dy + m.Y * s, Size = m.Size * s },
        _ => item,
    };
}
