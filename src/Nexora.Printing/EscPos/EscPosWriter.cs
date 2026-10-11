using System.Text;
using Nexora.Application.Billing;
using Nexora.Printing.Templates;

namespace Nexora.Printing.EscPos;

/// <summary>
/// EXPERIMENTAL. Builds ESC/POS command bytes for receipt printers (Epson TM-compatible command set). Only the
/// byte generation is tested; no printer model has been tested, and many printers differ in code pages, cutters and
/// QR support. The Windows driver path (the default) should be preferred. Text is sent as ASCII: characters outside
/// ASCII are replaced (₹ becomes "Rs.").
/// </summary>
public sealed class EscPosWriter
{
    private const byte Esc = 0x1B;
    private const byte Gs = 0x1D;
    private readonly List<byte> _bytes = [];

    public EscPosWriter(int columns)
    {
        if (columns is < 24 or > 64) throw new ArgumentOutOfRangeException(nameof(columns), "Use 24 to 64 columns (32 for 58 mm, 48 for 80 mm).");
        Columns = columns;
        Add(Esc, (byte)'@'); // initialise
    }

    public int Columns { get; }

    public byte[] ToArray() => [.. _bytes];

    public EscPosWriter Align(char alignment) => Add(Esc, (byte)'a', alignment switch { 'C' => (byte)1, 'R' => (byte)2, _ => (byte)0 });

    public EscPosWriter Bold(bool on) => Add(Esc, (byte)'E', on ? (byte)1 : (byte)0);

    /// <summary>GS ! n: double width and height on, or normal size.</summary>
    public EscPosWriter Large(bool on) => Add(Gs, (byte)'!', on ? (byte)0x11 : (byte)0x00);

    public EscPosWriter Line(string text = "")
    {
        foreach (var part in Wrap(Ascii(text), Columns)) AddText(part + "\n");
        return this;
    }

    /// <summary>Label on the left and value on the right of one line.</summary>
    public EscPosWriter Pair(string label, string value)
    {
        var right = Ascii(value);
        var left = Ascii(label);
        var room = Columns - right.Length - 1;
        if (room < 4) return Line(left).Line(right.PadLeft(Columns));
        if (left.Length > room) left = left[..room];
        AddText(left.PadRight(room) + " " + right + "\n");
        return this;
    }

    public EscPosWriter Rule() => Line(new string('-', Columns));

    public EscPosWriter Feed(int lines) => Add(Esc, (byte)'d', (byte)Math.Clamp(lines, 0, 255));

    /// <summary>GS V 66 n: feed and partial cut (printers without a cutter ignore it).</summary>
    public EscPosWriter Cut() => Add(Gs, (byte)'V', 66, 3);

    /// <summary>GS k m=73 (Code 128, function B) with code set B selected; HRI text printed below.</summary>
    public EscPosWriter Code128(string text)
    {
        var data = Encoding.ASCII.GetBytes("{B" + Ascii(text));
        if (data.Length > 255) throw new ArgumentException("Barcode text is too long.", nameof(text));
        Add(Gs, (byte)'H', 2); // HRI below
        Add(Gs, (byte)'h', 60); // height in dots
        Add(Gs, (byte)'w', 2); // module width
        Add(Gs, (byte)'k', 73, (byte)data.Length);
        _bytes.AddRange(data);
        return Add((byte)'\n');
    }

    /// <summary>GS ( k QR model 2: store, size 6, error correction M, then print. Content is UTF-8.</summary>
    public EscPosWriter Qr(string content)
    {
        var data = Encoding.UTF8.GetBytes(content);
        if (data.Length is 0 or > 700) throw new ArgumentException("QR content must be 1 to 700 bytes.", nameof(content));
        Add(Gs, (byte)'(', (byte)'k', 4, 0, 49, 65, 50, 0);   // model 2
        Add(Gs, (byte)'(', (byte)'k', 3, 0, 49, 67, 6);      // module size 6
        Add(Gs, (byte)'(', (byte)'k', 3, 0, 49, 69, 49);     // error correction M
        var length = data.Length + 3;
        Add(Gs, (byte)'(', (byte)'k', (byte)(length % 256), (byte)(length / 256), 49, 80, 48);
        _bytes.AddRange(data);
        return Add(Gs, (byte)'(', (byte)'k', 3, 0, 49, 81, 48); // print
    }

    /// <summary>A receipt for a billing document, from its stored figures (the same content as the thermal template).</summary>
    public static byte[] Receipt(BillingDocumentView d, PrintCompany company, string title, PrintMarks marks, int columns)
    {
        var w = new EscPosWriter(columns);
        w.Align('C').Bold(true).Line(company.Name).Bold(false);
        foreach (var line in company.AddressLines.Where(l => l.Length > 0)) w.Line(line);
        if (company.Gstin.Length > 0) w.Line("GSTIN " + company.Gstin);
        w.Line().Bold(true).Line(title).Bold(false);
        foreach (var banner in marks.Banners) w.Bold(true).Line("*** " + banner + " ***").Bold(false);
        w.Align('L').Pair("No.", d.Number ?? "Draft").Pair("Date", d.Date.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture));
        if (d.PartyName.Length > 0) w.Line(d.PartyName);
        w.Rule();
        foreach (var line in d.Lines)
        {
            w.Line(line.Description);
            w.Pair($" {line.Quantity:0.###} x {line.Rate:0.00}", line.Total.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        }

        w.Rule();
        w.Pair("Taxable value", Money(d.Taxable));
        if (d.IsInterState) w.Pair("IGST", Money(d.Igst));
        else w.Pair("CGST", Money(d.Cgst)).Pair("SGST", Money(d.Sgst));
        if (d.RoundOff != 0) w.Pair("Round off", Money(d.RoundOff));
        w.Bold(true).Pair("TOTAL Rs.", Money(d.Total)).Bold(false);
        if (marks.FooterNote.Length > 0) w.Align('C').Line(marks.FooterNote).Align('L');
        if (marks.QrContent is { Length: > 0 } qr) w.Align('C').Qr(qr).Line("Reference QR. Not an e-invoice QR.").Align('L');
        return w.Feed(3).Cut().ToArray();
    }

    private static string Money(decimal value) => value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Replaces anything outside printable ASCII; ₹ becomes "Rs.".</summary>
    public static string Ascii(string text)
    {
        var builder = new StringBuilder();
        foreach (var ch in text ?? string.Empty)
        {
            if (ch == '₹') builder.Append("Rs.");
            else if (ch is '·' or '•') builder.Append('-');
            else if (ch is '—' or '–' or '−') builder.Append('-');
            else if (ch == '×') builder.Append('x');
            else if (ch is >= ' ' and <= '~') builder.Append(ch);
            else builder.Append('?');
        }

        return builder.ToString();
    }

    private static IEnumerable<string> Wrap(string text, int columns)
    {
        if (text.Length == 0)
        {
            yield return string.Empty;
            yield break;
        }

        for (var i = 0; i < text.Length; i += columns) yield return text.Substring(i, Math.Min(columns, text.Length - i));
    }

    private EscPosWriter Add(params byte[] bytes)
    {
        _bytes.AddRange(bytes);
        return this;
    }

    private void AddText(string text) => _bytes.AddRange(Encoding.ASCII.GetBytes(text));
}
