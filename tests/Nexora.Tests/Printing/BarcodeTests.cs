using Nexora.Printing.Barcodes;
using ZXing;
using ZXing.Common;

namespace Nexora.Tests.Printing;

/// <summary>Encoders are checked against published values and by decoding the result with an independent reader (ZXing.Net).</summary>
public sealed class BarcodeTests
{
    [Theory]
    [InlineData("400638133393", 1)]   // EAN-13 example used in the Phase 4 guide (4006381333931)
    [InlineData("590123412345", 7)]   // GS1 example 5901234123457
    [InlineData("890100000001", 9)]   // an Indian (890) prefix: 8 + 27 + 3 + 3 = 41 → 9
    [InlineData("000000000000", 0)]
    public void Ean13_check_digit_follows_the_gs1_rule(string twelve, int check)
    {
        Assert.Equal(check, Ean13.CheckDigit(twelve));
        Assert.True(Ean13.IsValid(twelve + check));
        Assert.False(Ean13.IsValid(twelve + (check + 1) % 10));
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345678901")]
    [InlineData("12345678901a")]
    public void Ean13_check_digit_needs_twelve_digits(string input) => Assert.Throws<ArgumentException>(() => Ean13.CheckDigit(input));

    [Fact]
    public void Ean13_has_95_modules_with_guards_and_refuses_a_bad_check_digit()
    {
        var code = Ean13.Encode("5901234123457");

        Assert.Equal(95, code.Modules.Length);
        Assert.Equal([true, false, true], code.Modules[..3]);
        Assert.Equal([false, true, false, true, false], code.Modules[45..50]);
        Assert.Equal([true, false, true], code.Modules[^3..]);
        Assert.Throws<ArgumentException>(() => Ean13.Encode("5901234123458"));
    }

    [Theory]
    [InlineData("5901234123457")]
    [InlineData("4006381333931")]
    [InlineData("8901000000019")]
    public void Ean13_decodes_back_to_the_same_digits(string digits)
    {
        Assert.Equal(digits, Decode(Ean13.Encode(digits), BarcodeFormat.EAN_13));
    }

    [Fact]
    public void Code128_checksum_is_the_weighted_sum_mod_103()
    {
        // "PJJ123C" stays in code set B (the digit run is only 3): 104 + 48×1 + 42×2 + 42×3 + 17×4 + 18×5 + 19×6 + 35×7 = 879; 879 mod 103 = 55.
        var values = Code128.Values("PJJ123C");

        Assert.Equal(104, values[0]);
        Assert.Equal(55, values[^1]);
    }

    [Fact]
    public void Code128_uses_code_set_c_for_digits()
    {
        Assert.Equal([105, 12, 34, 56], Code128.Values("123456").Take(4));
        Assert.Equal(11 * (1 + 3 + 1) + 13, Code128.Encode("123456").Modules.Length);
    }

    [Theory]
    [InlineData("LED-18W")]
    [InlineData("SKU 42 / blue")]
    [InlineData("123456")]
    [InlineData("12345")]
    [InlineData("AB1234567890CD")]
    [InlineData("A1234B56789")]
    [InlineData("~!@#$%^&*()_+{}|:<>?`")]
    public void Code128_decodes_back_to_the_same_text(string text)
    {
        Assert.Equal(text, Decode(Code128.Encode(text), BarcodeFormat.CODE_128));
    }

    [Theory]
    [InlineData("")]
    [InlineData("tab\tseparated")]
    [InlineData("₹100")]
    public void Code128_refuses_text_outside_printable_ascii(string text)
    {
        Assert.False(Code128.CanEncode(text));
        Assert.Throws<ArgumentException>(() => Code128.Encode(text));
    }

    [Fact]
    public void Product_barcodes_use_ean13_only_for_valid_ean13_codes()
    {
        Assert.Equal(Ean13.Name, ProductBarcode.For("4006381333931")!.Symbology);
        Assert.Equal(Code128.Name, ProductBarcode.For("4006381333932")!.Symbology);
        Assert.Equal(Code128.Name, ProductBarcode.For("LED-18W")!.Symbology);
        Assert.Null(ProductBarcode.For(" "));
    }

    [Theory]
    [InlineData("LED-18W")]
    [InlineData("INV/2026-27/0001 21-10-2026 1062.00")]
    [InlineData("Meera Electricals · ₹1,062.00")]
    public void Qr_codes_decode_back_to_the_same_content(string content)
    {
        var qr = QrCodes.Encode(content);

        Assert.True(qr.Size >= 21 && (qr.Size - 17) % 4 == 0);
        Assert.Equal(content, DecodeQr(qr));
    }

    [Fact]
    public void Qr_content_is_limited()
    {
        Assert.Throws<ArgumentException>(() => QrCodes.Encode(string.Empty));
        Assert.Throws<ArgumentException>(() => QrCodes.Encode(new string('x', QrCodes.MaxContentLength + 1)));
    }

    [Fact]
    public void Qr_content_format_fills_placeholders_and_reports_unknown_ones()
    {
        var values = new Dictionary<string, string> { ["sku"] = "LED-18W", ["price"] = "450.00" };

        Assert.Equal("LED-18W 450.00", QrContentFormat.Apply("{SKU} {price}", values));
        Assert.Null(QrContentFormat.Validate("{sku}|{price}", QrContentFormat.LabelFields));
        Assert.Null(QrContentFormat.Validate(string.Empty, QrContentFormat.LabelFields));
        Assert.Contains("{colour} is not a known field", QrContentFormat.Validate("{colour}", QrContentFormat.LabelFields), StringComparison.Ordinal);
        Assert.NotNull(QrContentFormat.Validate("{sku", QrContentFormat.LabelFields));
        Assert.NotNull(QrContentFormat.Validate("{12}", QrContentFormat.LabelFields));
        Assert.NotNull(QrContentFormat.Validate("{number}", QrContentFormat.LabelFields));
        Assert.Null(QrContentFormat.Validate("INV {number} {total}", QrContentFormat.DocumentFields));
    }

    internal static string? Decode(LinearBarcode code, BarcodeFormat format)
    {
        const int scale = 3, height = 60;
        var quiet = code.QuietZoneModules;
        var width = (code.Modules.Length + 2 * quiet) * scale;
        var pixels = new byte[width * height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var module = x / scale - quiet;
            pixels[y * width + x] = module >= 0 && module < code.Modules.Length && code.Modules[module] ? (byte)0 : (byte)255;
        }

        var reader = new MultiFormatReader();
        var hints = new Dictionary<DecodeHintType, object> { [DecodeHintType.POSSIBLE_FORMATS] = new List<BarcodeFormat> { format }, [DecodeHintType.TRY_HARDER] = true };
        var source = new RGBLuminanceSource(pixels, width, height, RGBLuminanceSource.BitmapFormat.Gray8);
        return reader.decode(new BinaryBitmap(new HybridBinarizer(source)), hints)?.Text;
    }

    internal static string? DecodeQr(QrMatrix qr)
    {
        const int scale = 4;
        var quiet = QrMatrix.QuietZoneModules;
        var side = (qr.Size + 2 * quiet) * scale;
        var pixels = new byte[side * side];
        for (var y = 0; y < side; y++)
        for (var x = 0; x < side; x++)
        {
            int mx = x / scale - quiet, my = y / scale - quiet;
            pixels[y * side + x] = mx >= 0 && my >= 0 && mx < qr.Size && my < qr.Size && qr.Modules[mx, my] ? (byte)0 : (byte)255;
        }

        var hints = new Dictionary<DecodeHintType, object>
        {
            [DecodeHintType.POSSIBLE_FORMATS] = new List<BarcodeFormat> { BarcodeFormat.QR_CODE },
            [DecodeHintType.CHARACTER_SET] = "UTF-8",
        };
        var source = new RGBLuminanceSource(pixels, side, side, RGBLuminanceSource.BitmapFormat.Gray8);
        return new MultiFormatReader().decode(new BinaryBitmap(new HybridBinarizer(source)), hints)?.Text;
    }
}
