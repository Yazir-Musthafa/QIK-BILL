using Nexora.Application.Inventory;
using Nexora.Domain.Printing;
using Nexora.Printing.EscPos;
using Nexora.Printing.Layout;
using Nexora.Printing.Templates;

namespace Nexora.Tests.Printing;

public sealed class LayoutTests
{
    private static readonly ITextMeasurer Measure = PrintFonts.Instance;

    [Theory]
    [InlineData(1_062, "Rupees One Thousand Sixty-Two Only")]
    [InlineData(106_250.50, "Rupees One Lakh Six Thousand Two Hundred Fifty and Fifty Paise Only")]
    [InlineData(12_34_56_789, "Rupees Twelve Crore Thirty-Four Lakh Fifty-Six Thousand Seven Hundred Eighty-Nine Only")]
    [InlineData(0.05, "Rupees Zero and Five Paise Only")]
    [InlineData(100, "Rupees One Hundred Only")]
    [InlineData(-19, "Minus Rupees Nineteen Only")]
    public void Amounts_are_written_in_the_indian_system(decimal amount, string words) => Assert.Equal(words, AmountInWords.Rupees(amount));

    [Theory]
    [InlineData(PaperKind.A4, PageOrientation.Portrait, 595.28, 841.89)]
    [InlineData(PaperKind.A4, PageOrientation.Landscape, 841.89, 595.28)]
    [InlineData(PaperKind.A5, PageOrientation.Portrait, 419.53, 595.28)]
    public void Sheet_sizes_are_iso_216_in_points(PaperKind paper, PageOrientation orientation, double width, double height)
    {
        var setup = new PageSetup(paper, orientation, Margins.Uniform(10));

        Assert.Equal(width, setup.PageWidth, 2);
        Assert.Equal(height, setup.PageHeight!.Value, 2);
        Assert.False(setup.IsRoll);
    }

    [Theory]
    [InlineData(PaperKind.Thermal58, 164.41)]
    [InlineData(PaperKind.Thermal80, 226.77)]
    public void Rolls_have_a_fixed_width_and_no_page_height(PaperKind paper, double width)
    {
        var setup = PageSetup.Default(paper) with { Orientation = PageOrientation.Landscape };

        Assert.Equal(width, setup.PageWidth, 2);
        Assert.Null(setup.PageHeight);
        Assert.True(setup.IsNarrow);
        Assert.True(double.IsPositiveInfinity(setup.ContentHeight));
    }

    [Fact]
    public void Composer_breaks_pages_before_overflowing_and_numbers_them()
    {
        var setup = new PageSetup(PaperKind.A5, PageOrientation.Portrait, Margins.Uniform(10), 100, 9);
        var composer = new PageComposer(setup, Measure);
        var headers = 0;
        composer.ContinuationHeader = c =>
        {
            headers++;
            c.Text(0, c.Width, "Continued", 9, true);
            c.Move(c.LineHeight(9));
        };

        for (var i = 0; i < 120; i++)
        {
            composer.Ensure(composer.LineHeight(9));
            composer.Text(0, composer.Width, $"Row {i}", 9);
            composer.Move(composer.LineHeight(9));
        }

        var document = composer.Finish("Rows");
        var bottom = setup.PageHeight!.Value - 10 * PageSetup.PointsPerMm;
        Assert.True(document.Pages.Count > 2);
        Assert.Equal(document.Pages.Count - 1, headers);
        Assert.All(document.Pages, page => Assert.All(page.Items.OfType<TextItem>(), t => Assert.True(t.Y + t.Size * 1.25 <= bottom + 0.01, $"{t.Text} overflows")));
        Assert.Equal($"Page {document.Pages.Count} of {document.Pages.Count}", document.Pages[^1].Items.OfType<TextItem>().Last().Text);
        Assert.Equal(120, document.Texts.Count(t => t.Text.StartsWith("Row ", StringComparison.Ordinal)));
    }

    [Fact]
    public void Scale_shrinks_content_from_the_margin_corner()
    {
        var setup = new PageSetup(PaperKind.A4, PageOrientation.Portrait, Margins.Uniform(10), 50, 10);
        var composer = new PageComposer(setup, Measure);
        composer.Text(100, 50, "x", 10);

        var item = Assert.IsType<TextItem>(Assert.Single(composer.Finish("t").Pages[0].Items));
        var margin = 10 * PageSetup.PointsPerMm;

        Assert.Equal(margin + 50, item.X, 3);
        Assert.Equal(5, item.Size, 3);
        Assert.Equal((595.28 - 2 * margin) * 2, setup.ContentWidth, 1);
    }

    [Fact]
    public void Roll_pages_are_as_long_as_their_content()
    {
        var setup = PageSetup.Default(PaperKind.Thermal80);
        var composer = new PageComposer(setup, Measure);
        for (var i = 0; i < 40; i++)
        {
            composer.Text(0, composer.Width, "Line", 8);
            composer.Move(10);
        }

        var page = Assert.Single(composer.Finish("roll").Pages);
        Assert.Equal(3 * PageSetup.PointsPerMm * 2 + 400, page.Height, 3);
    }

    [Theory]
    [InlineData(20, 10)]   // 58 − 40 mm leaves 18 mm
    [InlineData(5, 1)]     // scale 10%
    [InlineData(5, 25)]    // scale 250%
    public void Invalid_page_setups_are_refused(double margin, double scale)
    {
        var setup = new PageSetup(PaperKind.Thermal58, PageOrientation.Portrait, Margins.Uniform(margin), scale * 10, 9);
        Assert.Throws<ArgumentException>(setup.Validate);
    }

    [Fact]
    public void Wrapping_never_exceeds_the_width_and_keeps_every_word()
    {
        const string text = "LED panel 18 W, cool daylight, with a supercalifragilisticexpialidociously long model name";
        var lines = TextFit.Wrap(Measure, text, 80, 9);

        Assert.All(lines, l => Assert.True(Measure.Width(l, 9, false) <= 80.01, l));
        Assert.Equal(text.Replace(" ", string.Empty, StringComparison.Ordinal), string.Concat(lines).Replace(" ", string.Empty, StringComparison.Ordinal));
        Assert.EndsWith("…", TextFit.Wrap(Measure, text, 80, 9, maxLines: 2)[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void Bundled_font_has_the_rupee_sign_and_measures_bold_wider()
    {
        Assert.True(Measure.Width("₹", 10, false) > 3);
        Assert.True(Measure.Width("Total", 10, true) > Measure.Width("Total", 10, false));
        Assert.Equal(2 * Measure.Width("Total", 10, false), Measure.Width("Total", 20, false), 2);
    }
}

public sealed class LabelLayoutTests
{
    private static readonly LabelGeometry A4Sheet = new(210, 297, 63.5, 33.9, 3, 8, 7.2, 13.1, 2.5, 0, true, true, true, true, LabelCode.Barcode);

    [Theory]
    [InlineData(0, 0, 7.2, 13.1)]
    [InlineData(1, 0, 73.2, 13.1)]
    [InlineData(2, 0, 139.2, 13.1)]
    [InlineData(4, 0, 73.2, 47.0)]
    [InlineData(23, 0, 139.2, 250.4)]
    [InlineData(24, 1, 7.2, 13.1)]
    public void Labels_fill_rows_then_pages(int index, int page, double x, double y)
    {
        var position = A4Sheet.Position(index);

        Assert.Equal(page, position.Page);
        Assert.Equal(x, position.X, 3);
        Assert.Equal(y, position.Y, 3);
    }

    [Fact]
    public void Quantities_and_skipped_labels_decide_the_page_count()
    {
        var led = new LabelData("LED panel 18 W", "LED-18W", 450, 499, "4006381333931", "LED-18W");
        var fan = new LabelData("Ceiling fan 1200 mm", "FAN-1200", 2_150, 2_400, null, "FAN-1200");

        var document = LabelTemplate.Render(A4Sheet, [(led, 20), (fan, 5)], skip: 3, PrintFonts.Instance);

        Assert.Equal(2, document.Pages.Count);                  // 3 skipped + 25 labels = 28 slots → 2 sheets
        Assert.Equal(25, document.Texts.Count(t => t.Text.StartsWith("₹ ", StringComparison.Ordinal)));
        Assert.Equal(20, document.Texts.Count(t => t.Text == "4006381333931"));
        Assert.Equal(10, document.Texts.Count(t => t.Text == "FAN-1200"));   // SKU line, and the Code 128 caption of the SKU when there is no barcode
        Assert.Throws<ArgumentException>(() => LabelTemplate.Render(A4Sheet, [(led, 1)], skip: 24, PrintFonts.Instance));
        Assert.Throws<ArgumentException>(() => LabelTemplate.Render(A4Sheet, [(led, 0)], skip: 0, PrintFonts.Instance));
    }

    [Fact]
    public void Everything_on_a_label_stays_inside_it_and_the_barcode_scans()
    {
        var led = new LabelData("LED panel 18 W cool daylight with a long descriptive name", "LED-18W", 450, 499, "4006381333931", "LED-18W");
        var mm = PageSetup.PointsPerMm;
        var items = LabelTemplate.Label(A4Sheet, led, 0, 0, PrintFonts.Instance);

        foreach (var item in items)
        {
            var (x, y, w, h) = item switch
            {
                TextItem t => (t.X, t.Y, t.Width, t.Size * 1.25),
                BarcodeItem b => (b.X, b.Y, b.Width, b.Height),
                _ => (0d, 0d, 0d, 0d),
            };
            Assert.True(x >= 0 && y >= 0 && x + w <= 63.5 * mm + 0.01 && y + h <= 33.9 * mm + 0.01, $"{item} is outside the label");
        }

        var bar = Assert.Single(items.OfType<BarcodeItem>());
        Assert.Equal("4006381333931", BarcodeTests.Decode(new Nexora.Printing.Barcodes.LinearBarcode("EAN-13", "", bar.Modules), ZXing.BarcodeFormat.EAN_13));
        Assert.True(LabelTemplate.ModuleWidth(A4Sheet, Nexora.Printing.Barcodes.Ean13.Encode("4006381333931")) > 0.7);
    }

    [Fact]
    public void A_qr_label_puts_the_code_on_the_right_and_keeps_text_clear_of_it()
    {
        var roll = new LabelGeometry(50, 25, 50, 25, 1, 1, 0, 0, 0, 0, true, true, true, false, LabelCode.Qr);
        var items = LabelTemplate.Label(roll, new LabelData("LED panel", "LED-18W", 450, 0, null, "LED-18W|450.00"), 0, 0, PrintFonts.Instance);

        var qr = Assert.Single(items.OfType<MatrixItem>());
        Assert.All(items.OfType<TextItem>(), t => Assert.True(t.X + t.Width <= qr.X + 0.01));
        Assert.Equal("LED-18W|450.00", BarcodeTests.DecodeQr(new Nexora.Printing.Barcodes.QrMatrix("", qr.Modules)));
    }

    [Fact]
    public void Layouts_that_do_not_fit_the_page_are_refused()
    {
        var layout = new LabelLayout(Guid.NewGuid(), Guid.NewGuid(), "Bad");

        var error = Assert.Throws<Nexora.Domain.Common.DomainException>(() => layout.Update("Bad", 210, 297, 70, 33.9m, 3, 8, 7.2m, 13.1m, 2.5m, 0, true, true, true, false, LabelCode.Barcode));
        Assert.Contains("do not fit across", error.Message, StringComparison.Ordinal);
        Assert.Throws<Nexora.Domain.Common.DomainException>(() => layout.Update("Bad", 50, 25, 50, 25, 1, 1, 0, 0, 0, 0, false, false, false, false, LabelCode.None));
    }
}

public sealed class EscPosTests
{
    [Fact]
    public void Commands_start_with_initialise_and_end_with_feed_and_cut()
    {
        var bytes = new EscPosWriter(32).Line("Hello").Feed(3).Cut().ToArray();

        Assert.Equal(new byte[] { 0x1B, 0x40 }, bytes[..2]);
        Assert.Equal(new byte[] { 0x1B, (byte)'d', 3, 0x1D, (byte)'V', 66, 3 }, bytes[^7..]);
        Assert.Contains("Hello\n", System.Text.Encoding.ASCII.GetString(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public void Pairs_fill_the_line_and_long_text_wraps_at_the_column_count()
    {
        var text = System.Text.Encoding.ASCII.GetString(new EscPosWriter(32).Pair("Total", "1,062.00").Line(new string('x', 40)).ToArray()[2..]);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("Total" + new string(' ', 32 - 5 - 8) + "1,062.00", lines[0]);
        Assert.Equal(32, lines[1].Length);
        Assert.Equal(8, lines[2].Length);
    }

    [Fact]
    public void Barcode_and_qr_commands_carry_their_lengths()
    {
        var code = new EscPosWriter(48).Code128("LED-18W").ToArray();
        var k = Array.IndexOf(code, (byte)'k');
        Assert.Equal(new byte[] { 0x1D, (byte)'k', 73, 9 }, code[(k - 1)..(k + 3)]);
        Assert.Equal("{BLED-18W", System.Text.Encoding.ASCII.GetString(code, k + 3, 9));

        var qr = new EscPosWriter(48).Qr("ABC").ToArray();
        var store = System.Text.Encoding.ASCII.GetString(qr);
        Assert.Contains("1P0ABC", store, StringComparison.Ordinal);
        var p = store.IndexOf("1P0", StringComparison.Ordinal);
        Assert.Equal(new byte[] { 6, 0 }, qr[(p - 2)..p]);       // 3 data bytes + 3
    }

    [Fact]
    public void Non_ascii_text_is_replaced_not_garbled()
    {
        Assert.Equal("Rs. 1,062.00 - Paid x2 ?", EscPosWriter.Ascii("₹ 1,062.00 — Paid ×2 あ"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EscPosWriter(10));
    }
}

public sealed class ScanDetectorTests
{
    private static readonly DateTime T0 = new(2026, 10, 11, 10, 0, 0, DateTimeKind.Utc);

    private static string? Feed(ScanDetector detector, string text, int gapMs, int enterGapMs)
    {
        var t = T0;
        foreach (var ch in text)
        {
            detector.Character(ch, t);
            t = t.AddMilliseconds(gapMs);
        }

        return detector.Enter(t.AddMilliseconds(enterGapMs - gapMs));
    }

    [Fact]
    public void Fast_input_ending_in_enter_is_a_scan() => Assert.Equal("4006381333931", Feed(new ScanDetector(), "4006381333931", 8, 8));

    [Fact]
    public void Typing_is_not_a_scan()
    {
        Assert.Null(Feed(new ScanDetector(), "4006381333931", 120, 120));
        Assert.Null(Feed(new ScanDetector(), "LED", 5, 5));              // too short
        Assert.Null(Feed(new ScanDetector(), "LED-18W", 5, 400));        // Enter pressed much later
    }

    [Fact]
    public void A_scan_right_after_slow_typing_is_still_recognised()
    {
        var detector = new ScanDetector();
        detector.Character('a', T0);
        detector.Character('b', T0.AddMilliseconds(300));
        var t = T0.AddMilliseconds(900);
        foreach (var ch in "LED-18W")
        {
            detector.Character(ch, t);
            t = t.AddMilliseconds(10);
        }

        Assert.Equal("LED-18W", detector.Enter(t));
        Assert.Null(detector.Enter(t.AddMilliseconds(5)));
    }
}
