using Nexora.Domain.Printing;
using Nexora.Printing.Barcodes;
using Nexora.Printing.Layout;

namespace Nexora.Printing.Templates;

/// <summary>What one product label shows. <see cref="Price"/> and <see cref="Mrp"/> are the stored selling price and MRP.</summary>
public sealed record LabelData(string Name, string Sku, decimal Price, decimal Mrp, string? Barcode, string QrContent);

/// <summary>The label geometry in millimetres, from a <see cref="LabelLayout"/>.</summary>
public sealed record LabelGeometry(double PageWidth, double PageHeight, double LabelWidth, double LabelHeight, int Columns, int Rows, double MarginLeft,
    double MarginTop, double HorizontalGap, double VerticalGap, bool ShowName, bool ShowSku, bool ShowPrice, bool ShowMrp, LabelCode Code)
{
    public int PerPage => Columns * Rows;

    public static LabelGeometry From(LabelLayout l) => new((double)l.PageWidthMm, (double)l.PageHeightMm, (double)l.LabelWidthMm, (double)l.LabelHeightMm,
        l.Columns, l.Rows, (double)l.MarginLeftMm, (double)l.MarginTopMm, (double)l.HorizontalGapMm, (double)l.VerticalGapMm, l.ShowName, l.ShowSku, l.ShowPrice,
        l.ShowMrp, l.Code);

    /// <summary>Top-left corner of label <paramref name="index"/> (0-based across all pages), in mm, and its page.</summary>
    public (int Page, double X, double Y) Position(int index)
    {
        var page = index / PerPage;
        var onPage = index % PerPage;
        var column = onPage % Columns;
        var row = onPage / Columns;
        return (page, MarginLeft + column * (LabelWidth + HorizontalGap), MarginTop + row * (LabelHeight + VerticalGap));
    }
}

/// <summary>
/// Label sheets and rolls: labels fill each page left to right, top to bottom, starting at <c>skip</c> (to reuse a
/// partly used sheet). Each label has a 1.5 mm inner padding. Text shrinks with the label height; barcodes fill the
/// remaining width at a whole number of points per module where possible.
/// </summary>
public static class LabelTemplate
{
    public const double PaddingMm = 1.5;

    public static RenderedDocument Render(LabelGeometry g, IReadOnlyList<(LabelData Label, int Quantity)> items, int skip, ITextMeasurer measure)
    {
        if (skip < 0 || skip >= g.PerPage) throw new ArgumentException($"Skip 0 to {g.PerPage - 1} labels on the first page.");
        if (items.Any(i => i.Quantity is < 0 or > 1000)) throw new ArgumentException("Print 0 to 1000 labels of each product.");
        var total = items.Sum(i => i.Quantity);
        if (total == 0) throw new ArgumentException("Choose at least one label to print.");
        if (total > 5000) throw new ArgumentException("Print at most 5000 labels at a time.");

        var mm = PageSetup.PointsPerMm;
        var pages = new List<List<PageItem>>();
        var index = skip;
        foreach (var (label, quantity) in items)
        {
            for (var copy = 0; copy < quantity; copy++, index++)
            {
                var (page, x, y) = g.Position(index);
                while (pages.Count <= page) pages.Add([]);
                pages[page].AddRange(Label(g, label, x * mm, y * mm, measure));
            }
        }

        return new RenderedDocument($"Labels ({total})", pages.Select(p => new RenderedPage(g.PageWidth * mm, g.PageHeight * mm, p)).ToList());
    }

    /// <summary>The items of one label with its top-left corner at (<paramref name="left"/>, <paramref name="top"/>) points.</summary>
    public static IReadOnlyList<PageItem> Label(LabelGeometry g, LabelData data, double left, double top, ITextMeasurer measure)
    {
        var mm = PageSetup.PointsPerMm;
        var items = new List<PageItem>();
        var pad = PaddingMm * mm;
        var x = left + pad;
        var y = top + pad;
        var width = g.LabelWidth * mm - 2 * pad;
        var height = g.LabelHeight * mm - 2 * pad;
        var bottom = y + height;
        var size = Math.Clamp(g.LabelHeight * mm / 9, 5, 10);
        double LineHeight(double s) => s * 1.25;

        var qr = g.Code == LabelCode.Qr && data.QrContent.Length > 0 ? QrCodes.Encode(data.QrContent) : null;
        var qrSide = qr is null ? 0 : Math.Min(height, width * 0.45);
        var textWidth = width - (qr is null ? 0 : qrSide + 2);
        if (qr is not null) items.Add(new MatrixItem(x + width - qrSide, y, qrSide, qr.Modules));

        void Line(string text, double s, bool bold)
        {
            if (y + LineHeight(s) > bottom + 0.01) return;
            items.Add(new TextItem(x, y, textWidth, TextFit.Truncate(measure, text, textWidth, s, bold), s, bold));
            y += LineHeight(s);
        }

        if (g.ShowName)
        {
            foreach (var line in TextFit.Wrap(measure, data.Name, textWidth, size, true, 2)) Line(line, size, true);
        }

        if (g.ShowSku) Line(data.Sku, size * 0.85, false);
        if (g.ShowPrice) Line("₹ " + Application.Formatting.IndianNumberFormat.Number(data.Price), size * 1.1, true);
        if (g.ShowMrp && data.Mrp > 0) Line("MRP ₹ " + Application.Formatting.IndianNumberFormat.Number(data.Mrp) + " (incl. of all taxes)", size * 0.75, false);

        if (g.Code == LabelCode.Barcode)
        {
            var code = ProductBarcode.For(data.Barcode) ?? ProductBarcode.For(data.Sku);
            if (code is not null)
            {
                var textSize = Math.Max(size * 0.7, 4.5);
                var barHeight = bottom - y - LineHeight(textSize) - 1;
                if (barHeight >= 8)
                {
                    var modules = code.Modules.Length + 2 * code.QuietZoneModules;
                    var module = width / modules;
                    if (module >= 1) module = Math.Floor(module * 4) / 4; // quarter points keep bars even on most printers
                    var barWidth = module * code.Modules.Length;
                    var barX = x + (width - barWidth) / 2;
                    items.Add(new BarcodeItem(barX, y, barWidth, barHeight, code.Modules));
                    items.Add(new TextItem(x, y + barHeight + 0.5, width, code.Text, textSize, false, TextAlign.Center));
                }
            }
        }

        return items;
    }

    /// <summary>The width of one barcode module on a label in points; below about 0.7 pt (0.25 mm) many printers smear bars.</summary>
    public static double ModuleWidth(LabelGeometry g, LinearBarcode code) =>
        (g.LabelWidth * PageSetup.PointsPerMm - 2 * PaddingMm * PageSetup.PointsPerMm) / (code.Modules.Length + 2 * code.QuietZoneModules);
}
