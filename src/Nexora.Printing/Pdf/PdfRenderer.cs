using Nexora.Printing.Layout;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Nexora.Printing.Pdf;

/// <summary>
/// Writes a <see cref="RenderedDocument"/> as PDF with PDFsharp (MIT). The bundled font is embedded (subset), so the
/// file looks the same on any computer. This is the same set of page items that Windows printing draws.
/// </summary>
public static class PdfRenderer
{
    public static byte[] Render(RenderedDocument document)
    {
        var fonts = PrintFonts.Instance;
        using var pdf = new PdfDocument();
        pdf.Info.Title = document.Title;
        pdf.Info.Creator = "QIK Suite";
        foreach (var source in document.Pages)
        {
            var page = pdf.AddPage();
            page.Width = XUnit.FromPoint(source.Width);
            page.Height = XUnit.FromPoint(source.Height);
            using var graphics = XGraphics.FromPdfPage(page);
            foreach (var item in source.Items) Draw(graphics, fonts, item);
        }

        using var stream = new MemoryStream();
        pdf.Save(stream, false);
        return stream.ToArray();
    }

    private static void Draw(XGraphics g, PrintFonts fonts, PageItem item)
    {
        switch (item)
        {
            case TextItem t:
            {
                var font = fonts.Font(t.Size, t.Bold);
                var width = fonts.Width(t.Text, t.Size, t.Bold);
                var x = t.Align switch
                {
                    TextAlign.Right => t.X + t.Width - width,
                    TextAlign.Center => t.X + (t.Width - width) / 2,
                    _ => t.X,
                };
                // Baseline: the line box is 1.25 × size; the ascent of Noto Sans is about 1.07 × size.
                g.DrawString(t.Text, font, XBrushes.Black, new XPoint(x, t.Y + t.Size * 1.07), XStringFormats.BaseLineLeft);
                break;
            }

            case LineItem l:
                g.DrawLine(new XPen(XColors.Black, l.Thickness), l.X1, l.Y1, l.X2, l.Y2);
                break;
            case RectItem r when r.Filled:
                g.DrawRectangle(XBrushes.Black, r.X, r.Y, r.Width, r.Height);
                break;
            case RectItem r:
                g.DrawRectangle(new XPen(XColors.Black, r.Thickness), r.X, r.Y, r.Width, r.Height);
                break;
            case ImageItem image:
                DrawImage(g, image);
                break;
            case BarcodeItem b:
            {
                var module = b.Width / b.Modules.Length;
                for (var i = 0; i < b.Modules.Length; i++)
                {
                    if (!b.Modules[i]) continue;
                    var run = 1;
                    while (i + run < b.Modules.Length && b.Modules[i + run]) run++;
                    g.DrawRectangle(XBrushes.Black, b.X + i * module, b.Y, run * module, b.Height);
                    i += run - 1;
                }

                break;
            }

            case MatrixItem m:
            {
                var count = m.Modules.GetLength(0);
                var module = m.Size / count;
                for (var y = 0; y < count; y++)
                for (var x = 0; x < count; x++)
                    if (m.Modules[x, y]) g.DrawRectangle(XBrushes.Black, m.X + x * module, m.Y + y * module, module, module);
                break;
            }
        }
    }

    private static void DrawImage(XGraphics g, ImageItem image)
    {
        try
        {
            using var stream = new MemoryStream(image.Data);
            using var picture = XImage.FromStream(stream);
            // Keep the aspect ratio inside the box, aligned top-left.
            var ratio = Math.Min(image.Width / picture.PointWidth, image.Height / picture.PointHeight);
            g.DrawImage(picture, image.X, image.Y, picture.PointWidth * ratio, picture.PointHeight * ratio);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // An unreadable logo must not stop the document; it is simply left out.
        }
    }
}
