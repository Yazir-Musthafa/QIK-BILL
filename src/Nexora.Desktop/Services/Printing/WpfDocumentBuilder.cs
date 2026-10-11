using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexora.Printing.Layout;
using WpfSize = System.Windows.Size;

namespace Nexora.Desktop.Services.Printing;

/// <summary>
/// Turns rendered pages into a WPF <see cref="FixedDocument"/> for preview and printing. It draws exactly the items the
/// PDF renderer draws, with the same bundled font, so the screen, the PDF and the paper match.
/// UI unverified at runtime: this path has not been run on Windows.
/// </summary>
public static class WpfDocumentBuilder
{
    private const double DipPerPoint = 96.0 / 72.0;
    private static readonly Lazy<FontFamily> Family = new(LoadFamily);

    public static FixedDocument Build(RenderedDocument document)
    {
        var fixedDocument = new FixedDocument();
        foreach (var page in document.Pages)
        {
            var size = new WpfSize(page.Width * DipPerPoint, page.Height * DipPerPoint);
            var fixedPage = new FixedPage { Width = size.Width, Height = size.Height, Background = Brushes.White };
            fixedPage.Children.Add(new PageVisual(page) { Width = size.Width, Height = size.Height });
            var content = new PageContent();
            ((System.Windows.Markup.IAddChild)content).AddChild(fixedPage);
            fixedDocument.Pages.Add(content);
        }

        fixedDocument.DocumentPaginator.PageSize = document.Pages.Count > 0
            ? new WpfSize(document.Pages[0].Width * DipPerPoint, document.Pages[0].Height * DipPerPoint)
            : new WpfSize(793.7, 1122.5);
        return fixedDocument;
    }

    /// <summary>The bundled Noto Sans, written once to the app's data folder so WPF can load it by file.</summary>
    private static FontFamily LoadFamily()
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QIKSuite", "fonts");
            Directory.CreateDirectory(folder);
            foreach (var (name, bytes) in new[] { ("NotoSans-Regular.ttf", PrintFonts.Regular), ("NotoSans-Bold.ttf", PrintFonts.Bold) })
            {
                var path = Path.Combine(folder, name);
                if (!File.Exists(path) || new FileInfo(path).Length != bytes.Length) File.WriteAllBytes(path, bytes);
            }

            return new FontFamily(new Uri(folder + Path.DirectorySeparatorChar), "./#" + PrintFonts.Family);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Falls back to a Windows font; widths differ slightly from the layout, but nothing is lost.
            return new FontFamily("Segoe UI");
        }
    }

    /// <summary>Draws one page's items in a single render pass.</summary>
    private sealed class PageVisual(RenderedPage page) : FrameworkElement
    {
        protected override void OnRender(DrawingContext dc)
        {
            var regular = new Typeface(Family.Value, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var bold = new Typeface(Family.Value, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            var pixels = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            dc.PushTransform(new ScaleTransform(DipPerPoint, DipPerPoint));
            foreach (var item in page.Items)
            {
                switch (item)
                {
                    case TextItem t:
                    {
                        var text = new FormattedText(t.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, t.Bold ? bold : regular, t.Size, Brushes.Black, pixels);
                        var x = t.Align switch
                        {
                            Nexora.Printing.Layout.TextAlign.Right => t.X + t.Width - text.WidthIncludingTrailingWhitespace,
                            Nexora.Printing.Layout.TextAlign.Center => t.X + (t.Width - text.WidthIncludingTrailingWhitespace) / 2,
                            _ => t.X,
                        };
                        // Align the baseline with the PDF renderer (1.07 × size below the line top).
                        dc.DrawText(text, new Point(x, t.Y + t.Size * 1.07 - text.Baseline));
                        break;
                    }

                    case LineItem l:
                        dc.DrawLine(new Pen(Brushes.Black, l.Thickness), new Point(l.X1, l.Y1), new Point(l.X2, l.Y2));
                        break;
                    case RectItem r:
                        dc.DrawRectangle(r.Filled ? Brushes.Black : null, r.Filled ? null : new Pen(Brushes.Black, r.Thickness), new Rect(r.X, r.Y, r.Width, r.Height));
                        break;
                    case ImageItem i:
                        DrawImage(dc, i);
                        break;
                    case BarcodeItem b:
                    {
                        var module = b.Width / b.Modules.Length;
                        var geometry = new StreamGeometry();
                        using (var context = geometry.Open())
                        {
                            for (var k = 0; k < b.Modules.Length; k++)
                            {
                                if (!b.Modules[k]) continue;
                                var run = 1;
                                while (k + run < b.Modules.Length && b.Modules[k + run]) run++;
                                AddRect(context, new Rect(b.X + k * module, b.Y, run * module, b.Height));
                                k += run - 1;
                            }
                        }

                        geometry.Freeze();
                        dc.DrawGeometry(Brushes.Black, null, geometry);
                        break;
                    }

                    case MatrixItem m:
                    {
                        var count = m.Modules.GetLength(0);
                        var module = m.Size / count;
                        var geometry = new StreamGeometry();
                        using (var context = geometry.Open())
                        {
                            for (var y = 0; y < count; y++)
                            for (var x = 0; x < count; x++)
                                if (m.Modules[x, y]) AddRect(context, new Rect(m.X + x * module, m.Y + y * module, module, module));
                        }

                        geometry.Freeze();
                        dc.DrawGeometry(Brushes.Black, null, geometry);
                        break;
                    }
                }
            }

            dc.Pop();
        }

        private static void AddRect(StreamGeometryContext context, Rect rect)
        {
            context.BeginFigure(rect.TopLeft, true, true);
            context.LineTo(rect.TopRight, false, false);
            context.LineTo(rect.BottomRight, false, false);
            context.LineTo(rect.BottomLeft, false, false);
        }

        private static void DrawImage(DrawingContext dc, ImageItem item)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = new MemoryStream(item.Data);
                bitmap.EndInit();
                bitmap.Freeze();
                var ratio = Math.Min(item.Width / bitmap.Width, item.Height / bitmap.Height);
                dc.DrawImage(bitmap, new Rect(item.X, item.Y, bitmap.Width * ratio, bitmap.Height * ratio));
            }
            catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException)
            {
                // An unreadable logo is left out rather than stopping the document.
            }
        }
    }
}
