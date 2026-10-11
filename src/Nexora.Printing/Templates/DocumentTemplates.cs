using Nexora.Application.Billing;
using Nexora.Domain.Billing;
using Nexora.Printing.Barcodes;
using Nexora.Printing.Layout;

namespace Nexora.Printing.Templates;

/// <summary>
/// Shared pieces of the business document templates: company header, banners, totals rows and the signature block.
/// Templates print figures exactly as stored on the posted document; they never add up lines to make a total.
/// </summary>
internal static class Blocks
{
    public static double Lh(PageComposer c, double size) => c.LineHeight(size);

    /// <summary>Logo, company name, address and GSTIN on the left; title and document details on the right.</summary>
    public static void WideHeader(PageComposer c, PrintCompany company, string title, IReadOnlyList<(string Label, string Value)> details)
    {
        var f = c.FontSize;
        var top = c.Y;
        double x = 0;
        if (company.Logo is { Length: > 0 } logo)
        {
            c.Add(new ImageItem(0, top, 54, 40, logo));
            x = 62;
        }

        var leftWidth = c.Width * 0.58 - x;
        var y = top;
        y += WrappedAt(c, x, y, leftWidth, company.Name, f + 4, bold: true, maxLines: 2);
        foreach (var line in company.AddressLines.Where(l => l.Length > 0)) y += WrappedAt(c, x, y, leftWidth, line, f - 0.5, maxLines: 2);
        if (company.Gstin.Length > 0) y += WrappedAt(c, x, y, leftWidth, $"GSTIN {company.Gstin} · State {company.StateCode} {company.StateName}", f - 0.5, bold: false);
        else if (company.StateName.Length > 0) y += WrappedAt(c, x, y, leftWidth, $"State {company.StateCode} {company.StateName}", f - 0.5);
        var contact = string.Join(" · ", new[] { company.Phone, company.Email }.Where(s => s.Length > 0));
        if (contact.Length > 0) y += WrappedAt(c, x, y, leftWidth, contact, f - 0.5);

        var rightX = c.Width * 0.6;
        var rightWidth = c.Width - rightX;
        var ry = top;
        c.Add(new TextItem(rightX, ry, rightWidth, title, f + 3, true, TextAlign.Right));
        ry += Lh(c, f + 3) + 2;
        foreach (var (label, value) in details.Where(d => d.Value.Length > 0))
        {
            c.Add(new TextItem(rightX, ry, rightWidth * 0.42, label, f - 0.5, false, TextAlign.Left));
            c.Add(new TextItem(rightX + rightWidth * 0.42, ry, rightWidth * 0.58, value, f - 0.5, true, TextAlign.Right));
            ry += Lh(c, f - 0.5);
        }

        c.Y = Math.Max(Math.Max(y, ry), top + (company.Logo is { Length: > 0 } ? 40 : 0)) + 6;
        c.Rule(0.8);
        c.Move(6);
    }

    /// <summary>Centred company block for receipts on roll paper.</summary>
    public static void NarrowHeader(PageComposer c, PrintCompany company, string title, IReadOnlyList<(string Label, string Value)> details)
    {
        var f = c.FontSize;
        c.Move(Centered(c, company.Name, f + 2, true, 3));
        foreach (var line in company.AddressLines.Where(l => l.Length > 0)) c.Move(Centered(c, line, f - 0.5, false, 3));
        if (company.Gstin.Length > 0) c.Move(Centered(c, "GSTIN " + company.Gstin, f - 0.5, false, 1));
        if (company.Phone.Length > 0) c.Move(Centered(c, company.Phone, f - 0.5, false, 1));
        c.Move(3);
        c.Move(Centered(c, title, f + 1, true, 2));
        foreach (var (label, value) in details.Where(d => d.Value.Length > 0)) Pair(c, label, value, f);
        Dashes(c);
    }

    public static double Centered(PageComposer c, string text, double size, bool bold, int maxLines) =>
        c.Wrapped(0, c.Width, text, size, bold, TextAlign.Center, maxLines);

    /// <summary>A label on the left and a value on the right, on one line; moves down.</summary>
    public static void Pair(PageComposer c, string label, string value, double size, bool bold = false)
    {
        c.Ensure(Lh(c, size));
        var valueWidth = Math.Min(c.Measure.Width(value, size, bold) + 2, c.Width * 0.7);
        c.Text(0, c.Width - valueWidth - 4, TextFit.Truncate(c.Measure, label, c.Width - valueWidth - 4, size, bold), size, bold);
        c.Text(c.Width - valueWidth, valueWidth, value, size, bold, TextAlign.Right);
        c.Move(Lh(c, size));
    }

    public static void Dashes(PageComposer c)
    {
        c.Move(2);
        c.Rule(0.5);
        c.Move(3);
    }

    public static void Banners(PageComposer c, IReadOnlyList<string> banners)
    {
        foreach (var banner in banners)
        {
            var size = c.FontSize + 2;
            c.Ensure(Lh(c, size) + 6);
            var width = Math.Min(c.Measure.Width(banner, size, true) + 16, c.Width);
            var x = (c.Width - width) / 2;
            c.Add(new RectItem(x, c.Y, width, Lh(c, size) + 4, false, 1));
            c.Add(new TextItem(x, c.Y + 2, width, banner, size, true, TextAlign.Center));
            c.Move(Lh(c, size) + 8);
        }
    }

    /// <summary>Right-aligned totals: labels then amounts, in a block on the right of wide pages.</summary>
    public static void Totals(PageComposer c, IReadOnlyList<(string Label, decimal Amount, bool Bold)> rows, double blockWidth)
    {
        var f = c.FontSize;
        c.Ensure(rows.Count * Lh(c, f + 1) + 6);
        var x = c.Width - blockWidth;
        foreach (var (label, amount, bold) in rows)
        {
            var size = bold ? f + 1 : f;
            if (bold)
            {
                c.Add(new LineItem(x, c.Y, c.Width, c.Y, 0.8));
                c.Move(2);
            }

            c.Text(x, blockWidth * 0.55, label, size, bold);
            c.Text(x + blockWidth * 0.55, blockWidth * 0.45, bold ? Fmt.Rupees(amount) : Fmt.Money(amount), size, bold, TextAlign.Right);
            c.Move(Lh(c, size));
        }
    }

    public static void Footer(PageComposer c, PrintCompany company, PrintMarks marks, bool signature, bool narrow)
    {
        var f = c.FontSize;
        if (marks.FooterNote.Length > 0)
        {
            c.Move(4);
            var height = TextFit.Wrap(c.Measure, marks.FooterNote, c.Width, f - 0.5).Count * Lh(c, f - 0.5);
            c.Ensure(height);
            c.Move(c.Wrapped(0, c.Width, marks.FooterNote, f - 0.5, false, narrow ? TextAlign.Center : TextAlign.Left));
        }

        if (marks.QrContent is { Length: > 0 } qrText)
        {
            var qr = QrCodes.Encode(qrText);
            var side = narrow ? Math.Min(c.Width * 0.5, 90) : 70;
            c.Ensure(side + Lh(c, f - 1) + 6);
            c.Move(4);
            var x = narrow ? (c.Width - side) / 2 : 0;
            c.Add(new MatrixItem(x, c.Y, side, qr.Modules));
            c.Move(side + 2);
            c.Text(narrow ? 0 : 0, narrow ? c.Width : c.Width * 0.5, "Reference QR. Not an e-invoice QR.", f - 1.5, false, narrow ? TextAlign.Center : TextAlign.Left);
            c.Move(Lh(c, f - 1.5));
        }

        if (signature && !narrow)
        {
            c.Ensure(Lh(c, f) * 4);
            c.Move(Lh(c, f));
            c.Text(c.Width * 0.5, c.Width * 0.5, "For " + company.Name, f, true, TextAlign.Right);
            c.Move(Lh(c, f) * 2.5);
            c.Text(c.Width * 0.5, c.Width * 0.5, "Authorised signatory", f - 0.5, false, TextAlign.Right);
            c.Move(Lh(c, f));
        }

        c.Ensure(Lh(c, f - 1.5));
        c.Move(2);
        c.Text(0, c.Width, "This is a computer-generated document.", f - 1.5, false, narrow ? TextAlign.Center : TextAlign.Left);
        c.Move(Lh(c, f - 1.5));
    }

    private static double WrappedAt(PageComposer c, double x, double y, double width, string text, double size, bool bold = false, int maxLines = 3)
    {
        var lines = TextFit.Wrap(c.Measure, text, width, size, bold, maxLines);
        for (var i = 0; i < lines.Count; i++) c.Add(new TextItem(x, y + i * Lh(c, size), width, lines[i], size, bold));
        return lines.Count * Lh(c, size);
    }
}

/// <summary>
/// Tax invoice, cash bill, credit note, purchase bill, debit note, delivery note and purchase order, on A4, A5 or
/// thermal rolls. All amounts are the stored figures of the document; the tax summary groups the stored line taxes
/// by rate.
/// </summary>
public static class InvoiceTemplate
{
    private sealed record Column(string Header, double Fraction, TextAlign Align);

    public static string TitleOf(BillingDocumentView d, DocumentStyle style, bool companyRegistered) => style switch
    {
        DocumentStyle.DeliveryNote => "DELIVERY NOTE",
        DocumentStyle.PurchaseOrder => "PURCHASE ORDER",
        _ => d.Kind switch
        {
            BillingDocumentKind.SalesInvoice when d.Mode == SettlementMode.Cash => companyRegistered ? "TAX INVOICE · CASH BILL" : "CASH BILL",
            BillingDocumentKind.SalesInvoice => companyRegistered ? "TAX INVOICE" : "INVOICE",
            BillingDocumentKind.SalesReturn => "CREDIT NOTE",
            BillingDocumentKind.PurchaseBill => "PURCHASE BILL",
            BillingDocumentKind.PurchaseReturn => "DEBIT NOTE",
            _ => d.Kind.Label().ToUpperInvariant(),
        },
    };

    public static RenderedDocument Render(BillingDocumentView d, PrintCompany company, PrintParty party, DocumentStyle style, PrintMarks marks,
        PageSetup setup, ITextMeasurer measure)
    {
        if (style == DocumentStyle.DeliveryNote && d.Kind != BillingDocumentKind.SalesInvoice)
            throw new ArgumentException("A delivery note is printed from a sales invoice.");
        if (style == DocumentStyle.PurchaseOrder && (d.Kind != BillingDocumentKind.PurchaseBill || d.State != BillingStatus.Draft))
            throw new ArgumentException("A purchase order is printed from a draft purchase bill.");
        var title = TitleOf(d, style, company.Gstin.Length > 0);
        var number = d.Number ?? (style == DocumentStyle.PurchaseOrder ? "Not numbered" : "Draft");
        var c = new PageComposer(setup, measure);
        var details = new List<(string, string)>
        {
            ("Number", number),
            ("Date", Fmt.Date(d.Date)),
            (d.Kind.IsReturn() ? "Against" : string.Empty, d.OriginalNumber ?? string.Empty),
            (d.Kind.IsSales() ? "Your reference" : "Supplier bill no.", d.Reference),
            ("Place of supply", d.PlaceOfSupply.Length > 0 ? $"{d.PlaceOfSupply} {d.PlaceOfSupplyName}" : string.Empty),
            ("Payment", style == DocumentStyle.Standard ? (d.Mode == SettlementMode.Cash ? "Cash" : "Credit") : string.Empty),
        };
        var prices = style != DocumentStyle.DeliveryNote;
        if (setup.IsNarrow) Narrow(c, d, company, party, title, details, prices, marks);
        else Wide(c, d, company, party, title, number, details, prices, style, marks);
        return c.Finish($"{title} {number}");
    }

    private static void Wide(PageComposer c, BillingDocumentView d, PrintCompany company, PrintParty party, string title, string number,
        List<(string, string)> details, bool prices, DocumentStyle style, PrintMarks marks)
    {
        var f = c.FontSize;
        Blocks.WideHeader(c, company, title, details);
        Blocks.Banners(c, marks.Banners);

        // Party block.
        var partyLabel = d.Kind.IsSales() ? (style == DocumentStyle.DeliveryNote ? "Deliver to" : "Bill to") : "Supplier";
        c.Text(0, c.Width, partyLabel, f - 1, true);
        c.Move(Blocks.Lh(c, f - 1));
        c.Move(c.Wrapped(0, c.Width * 0.6, party.Name, f + 0.5, true, maxLines: 2));
        foreach (var line in party.AddressLines.Where(l => l.Length > 0)) c.Move(c.Wrapped(0, c.Width * 0.6, line, f - 0.5, maxLines: 2));
        var partyTax = string.Join(" · ", new[] { party.Gstin.Length > 0 ? "GSTIN " + party.Gstin : "Unregistered", party.StateName, party.Phone }.Where(s => s.Length > 0));
        c.Move(c.Wrapped(0, c.Width * 0.6, partyTax, f - 0.5));
        c.Move(8);

        var columns = prices
            ? new List<Column>
            {
                new("#", 0.045, TextAlign.Left), new("Description", 0.305, TextAlign.Left), new("HSN/SAC", 0.08, TextAlign.Left),
                new("Qty", 0.085, TextAlign.Right), new("Rate", 0.105, TextAlign.Right), new("Discount", 0.09, TextAlign.Right),
                new("Taxable", 0.115, TextAlign.Right), new("GST", 0.055, TextAlign.Right), new("Amount", 0.12, TextAlign.Right),
            }
            : [new("#", 0.06, TextAlign.Left), new("Description", 0.58, TextAlign.Left), new("HSN/SAC", 0.12, TextAlign.Left), new("Qty", 0.12, TextAlign.Right), new("Unit", 0.12, TextAlign.Right)];
        var positions = new List<(double X, double W)>();
        double x = 0;
        foreach (var column in columns)
        {
            positions.Add((x, column.Fraction * c.Width));
            x += column.Fraction * c.Width;
        }

        void TableHeader(PageComposer p)
        {
            p.Rule(0.8);
            p.Move(2);
            for (var i = 0; i < columns.Count; i++) p.Text(positions[i].X + 2, positions[i].W - 4, columns[i].Header, f - 0.5, true, columns[i].Align);
            p.Move(Blocks.Lh(p, f - 0.5) + 2);
            p.Rule(0.5);
            p.Move(3);
        }

        c.ContinuationHeader = p =>
        {
            p.Text(0, p.Width * 0.6, company.Name, f, true);
            p.Text(p.Width * 0.4, p.Width * 0.6, $"{title} {number} (continued)", f, false, TextAlign.Right);
            p.Move(Blocks.Lh(p, f) + 4);
            TableHeader(p);
        };
        TableHeader(c);

        foreach (var line in d.Lines)
        {
            var descriptionWidth = positions[1].W - 4;
            var extra = string.Join(" · ", new[] { line.Batch.Length > 0 ? "Batch " + line.Batch : string.Empty, line.ExpiresOn is { } e ? "Exp " + Fmt.Date(e) : string.Empty,
                line.SerialNumber is { Length: > 0 } s ? "S/N " + s : string.Empty }.Where(t => t.Length > 0));
            var descriptionLines = TextFit.Wrap(c.Measure, line.Description, descriptionWidth, f, false, 3);
            var extraLines = extra.Length > 0 ? TextFit.Wrap(c.Measure, extra, descriptionWidth, f - 1.5, false, 2) : [];
            var height = descriptionLines.Count * Blocks.Lh(c, f) + extraLines.Count * Blocks.Lh(c, f - 1.5) + 3;
            c.Ensure(height);
            var cells = prices
                ? new[]
                {
                    line.LineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), string.Empty, line.HsnCode, $"{Fmt.Quantity(line.Quantity)} {line.Unit}",
                    Fmt.Money(line.Rate), line.Discount == 0 ? string.Empty : Fmt.Money(line.Discount), Fmt.Money(line.Taxable), Fmt.Percent(line.TaxRatePercent),
                    Fmt.Money(line.Total),
                }
                : [line.LineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), string.Empty, line.HsnCode, Fmt.Quantity(line.Quantity), line.Unit];
            for (var i = 0; i < columns.Count; i++)
                if (i != 1) c.Text(positions[i].X + 2, positions[i].W - 4, cells[i], f, false, columns[i].Align);
            var y = c.Y;
            foreach (var text in descriptionLines)
            {
                c.Add(new TextItem(positions[1].X + 2, y, descriptionWidth, text, f));
                y += Blocks.Lh(c, f);
            }

            foreach (var text in extraLines)
            {
                c.Add(new TextItem(positions[1].X + 2, y, descriptionWidth, text, f - 1.5));
                y += Blocks.Lh(c, f - 1.5);
            }

            c.Move(height);
        }

        c.Rule(0.8);
        c.Move(6);
        c.ContinuationHeader = p =>
        {
            p.Text(0, p.Width * 0.6, company.Name, f, true);
            p.Text(p.Width * 0.4, p.Width * 0.6, $"{title} {number} (continued)", f, false, TextAlign.Right);
            p.Move(Blocks.Lh(p, f) + 4);
            p.Rule(0.5);
            p.Move(4);
        };

        if (!prices)
        {
            c.Text(0, c.Width, $"Total quantity: {Fmt.Quantity(d.Lines.Sum(l => l.Quantity))} (all units)", f, true);
            c.Move(Blocks.Lh(c, f) + 4);
            c.Text(0, c.Width, $"Against {(d.Number is null ? "draft invoice" : "invoice " + d.Number)}. Received the goods above in good condition.", f - 0.5);
            c.Move(Blocks.Lh(c, f) * 3);
            c.Text(0, c.Width * 0.45, "Receiver's signature", f - 0.5);
            Blocks.Footer(c, company, marks, signature: true, narrow: false);
            return;
        }

        // Tax summary (left) and totals (right), side by side, kept on one page with the amount in words and the
        // signature block, so a page never holds only the signature.
        var keep = (Math.Max(TaxGroups(d).Count + 2, TotalRows(d).Count) + 2) * Blocks.Lh(c, f + 1) + Blocks.Lh(c, f) * 8;
        c.Ensure(keep);
        var startY = c.Y;
        TaxSummary(c, d, c.Width * 0.55);
        var afterSummary = c.Y;
        var page = c.PageCount;
        c.Y = startY;
        Blocks.Totals(c, TotalRows(d), c.Width * 0.38);
        if (c.PageCount == page) c.Y = Math.Max(c.Y, afterSummary);
        c.Move(4);
        c.Ensure(Blocks.Lh(c, f) * 2);
        c.Move(c.Wrapped(0, c.Width, AmountInWords.Rupees(d.Total), f - 0.5, true));
        Blocks.Footer(c, company, marks, signature: true, narrow: false);
    }

    private static void Narrow(PageComposer c, BillingDocumentView d, PrintCompany company, PrintParty party, string title, List<(string, string)> details,
        bool prices, PrintMarks marks)
    {
        var f = c.FontSize;
        Blocks.NarrowHeader(c, company, title, details);
        Blocks.Banners(c, marks.Banners);
        if (party.Name.Length > 0)
        {
            c.Move(c.Wrapped(0, c.Width, party.Name, f, true, maxLines: 2));
            if (party.Gstin.Length > 0) c.Move(c.Wrapped(0, c.Width, "GSTIN " + party.Gstin, f - 0.5));
            Blocks.Dashes(c);
        }

        foreach (var line in d.Lines)
        {
            c.Move(c.Wrapped(0, c.Width, line.Description, f, false, maxLines: 3));
            if (line.SerialNumber is { Length: > 0 } serial) c.Move(c.Wrapped(0, c.Width, "S/N " + serial, f - 1));
            if (prices)
            {
                Blocks.Pair(c, $"{Fmt.Quantity(line.Quantity)} {line.Unit} × {Fmt.Money(line.Rate)}" + (line.Discount != 0 ? $" − {Fmt.Money(line.Discount)}" : string.Empty)
                    + $" · GST {Fmt.Percent(line.TaxRatePercent)}", Fmt.Money(line.Total), f - 0.5);
            }
            else
            {
                Blocks.Pair(c, "Quantity", $"{Fmt.Quantity(line.Quantity)} {line.Unit}", f - 0.5);
            }
        }

        Blocks.Dashes(c);
        if (prices)
        {
            foreach (var (label, amount, bold) in TotalRows(d)) Blocks.Pair(c, label, bold ? Fmt.Rupees(amount) : Fmt.Money(amount), bold ? f + 1 : f, bold);
            Blocks.Dashes(c);
            foreach (var (rate, taxable, cgst, sgst, igst) in TaxGroups(d))
            {
                var tax = d.IsInterState ? $"IGST {Fmt.Money(igst)}" : $"CGST {Fmt.Money(cgst)} SGST {Fmt.Money(sgst)}";
                c.Move(c.Wrapped(0, c.Width, $"GST {Fmt.Percent(rate)} on {Fmt.Money(taxable)}: {tax}", f - 1));
            }

            c.Move(2);
            c.Move(c.Wrapped(0, c.Width, AmountInWords.Rupees(d.Total), f - 1, false, TextAlign.Center));
        }

        Blocks.Footer(c, company, marks, signature: false, narrow: true);
    }

    private static List<(string, decimal, bool)> TotalRows(BillingDocumentView d)
    {
        var rows = new List<(string, decimal, bool)>();
        if (d.Discount != 0) rows.Add(("Discount given", d.Discount, false));
        rows.Add(("Taxable value", d.Taxable, false));
        if (d.Cgst != 0 || !d.IsInterState) rows.Add(("CGST", d.Cgst, false));
        if (d.Sgst != 0 || !d.IsInterState) rows.Add(("SGST", d.Sgst, false));
        if (d.Igst != 0 || d.IsInterState) rows.Add(("IGST", d.Igst, false));
        if (d.RoundOff != 0) rows.Add(("Round off", d.RoundOff, false));
        rows.Add(("Total", d.Total, true));
        return rows;
    }

    /// <summary>The stored line taxes grouped by rate (no tax is recomputed).</summary>
    public static IReadOnlyList<(decimal Rate, decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst)> TaxGroups(BillingDocumentView d) =>
        d.Lines.GroupBy(l => l.TaxRatePercent).OrderBy(g => g.Key)
            .Select(g => (g.Key, g.Sum(l => l.Taxable), g.Sum(l => l.Cgst), g.Sum(l => l.Sgst), g.Sum(l => l.Igst))).ToList();

    private static void TaxSummary(PageComposer c, BillingDocumentView d, double width)
    {
        var f = c.FontSize - 0.5;
        var headers = d.IsInterState ? new[] { "GST rate", "Taxable", "IGST" } : ["GST rate", "Taxable", "CGST", "SGST"];
        var w = width / headers.Length;
        var groups = TaxGroups(d);
        c.Ensure((groups.Count + 2) * c.LineHeight(f) + 4);
        for (var i = 0; i < headers.Length; i++) c.Text(i * w, w - 4, headers[i], f, true, i == 0 ? TextAlign.Left : TextAlign.Right);
        c.Move(c.LineHeight(f));
        c.Rule(0.4, 0, width);
        c.Move(2);
        foreach (var (rate, taxable, cgst, sgst, igst) in groups)
        {
            var cells = d.IsInterState ? new[] { Fmt.Percent(rate), Fmt.Money(taxable), Fmt.Money(igst) } : [Fmt.Percent(rate), Fmt.Money(taxable), Fmt.Money(cgst), Fmt.Money(sgst)];
            for (var i = 0; i < cells.Length; i++) c.Text(i * w, w - 4, cells[i], f, false, i == 0 ? TextAlign.Left : TextAlign.Right);
            c.Move(c.LineHeight(f));
        }
    }
}

/// <summary>Receipt, payment voucher or refund voucher for a settlement.</summary>
public static class ReceiptTemplate
{
    public static string TitleOf(SettlementKind kind) => kind switch
    {
        SettlementKind.CustomerReceipt => "RECEIPT",
        SettlementKind.SupplierPayment => "PAYMENT VOUCHER",
        SettlementKind.CustomerRefund => "REFUND VOUCHER",
        SettlementKind.SupplierRefund => "RECEIPT (SUPPLIER REFUND)",
        _ => kind.Label().ToUpperInvariant(),
    };

    public static RenderedDocument Render(SettlementView s, PrintCompany company, PrintMarks marks, PageSetup setup, ITextMeasurer measure)
    {
        var c = new PageComposer(setup, measure);
        var f = c.FontSize;
        var title = TitleOf(s.Kind);
        var details = new List<(string, string)> { ("Number", s.Number), ("Date", Fmt.Date(s.Date)) };
        var narrow = setup.IsNarrow;
        if (narrow) Blocks.NarrowHeader(c, company, title, details);
        else Blocks.WideHeader(c, company, title, details);
        var banners = s.IsReversed ? marks.Banners.Append("REVERSED").ToList() : marks.Banners;
        Blocks.Banners(c, banners);

        var received = s.Kind is SettlementKind.CustomerReceipt or SettlementKind.SupplierRefund;
        Blocks.Pair(c, received ? "Received from" : "Paid to", s.PartyName, f, true);
        if (s.PartyGstin.Length > 0) Blocks.Pair(c, "GSTIN", s.PartyGstin, f);
        Blocks.Pair(c, "Amount", Fmt.Rupees(s.Amount), f + 2, true);
        c.Move(c.Wrapped(0, c.Width, AmountInWords.Rupees(s.Amount), f - 0.5, true, narrow ? TextAlign.Center : TextAlign.Left));
        c.Move(4);
        Blocks.Pair(c, "Method", s.Method.ToString(), f);
        if (s.Reference.Length > 0) Blocks.Pair(c, "Reference", s.Reference, f);
        Blocks.Pair(c, received ? "Into" : "From", s.Account, f);
        if (s.Narration.Length > 0) c.Move(c.Wrapped(0, c.Width, s.Narration, f - 0.5));
        if (s.Allocations.Count > 0)
        {
            Blocks.Dashes(c);
            c.Text(0, c.Width, "Applied to", f, true);
            c.Move(c.LineHeight(f));
            foreach (var a in s.Allocations) Blocks.Pair(c, $"{a.OtherType} {a.OtherNumber}", Fmt.Money(a.Amount), f - 0.5);
        }

        if (s.Unapplied != 0) Blocks.Pair(c, "On account (advance)", Fmt.Money(s.Unapplied), f - 0.5);
        if (s.ReversalReason is { Length: > 0 } reason) c.Move(c.Wrapped(0, c.Width, "Reversed: " + reason, f - 0.5));
        Blocks.Footer(c, company, marks with { QrContent = null }, signature: true, narrow: narrow);
        return c.Finish($"{title} {s.Number}");
    }
}

/// <summary>Customer or supplier statement of account from the party ledger.</summary>
public static class StatementTemplate
{
    public static RenderedDocument Render(PartyStatement statement, PrintCompany company, PageSetup setup, ITextMeasurer measure)
    {
        var c = new PageComposer(setup, measure);
        var f = c.FontSize;
        var party = statement.Party;
        var customer = party.Kind == PartyKind.Customer;
        var title = "STATEMENT OF ACCOUNT";
        var details = new List<(string, string)> { ("From", Fmt.Date(statement.From)), ("To", Fmt.Date(statement.To)) };
        if (setup.IsNarrow) Blocks.NarrowHeader(c, company, title, details);
        else Blocks.WideHeader(c, company, title, details);
        c.Move(c.Wrapped(0, c.Width, party.Name, f + 0.5, true));
        var address = string.Join(", ", new[] { party.Address, party.City, party.Pincode }.Where(s => s.Length > 0));
        if (address.Length > 0) c.Move(c.Wrapped(0, c.Width * 0.7, address, f - 0.5));
        if (party.Gstin.Length > 0) c.Move(c.Wrapped(0, c.Width, "GSTIN " + party.Gstin, f - 0.5));
        c.Move(6);

        string Balance(decimal amount) => amount == 0 ? "0.00" : $"{Fmt.Money(Math.Abs(amount))} {(amount > 0 == customer ? "Dr" : "Cr")}";

        if (setup.IsNarrow)
        {
            Blocks.Pair(c, "Opening balance", Balance(statement.Opening), f, true);
            foreach (var line in statement.Lines)
            {
                c.Move(c.Wrapped(0, c.Width, $"{Fmt.Date(line.Date)} {line.Voucher} {line.Particulars}", f - 0.5, false, maxLines: 2));
                Blocks.Pair(c, line.Debit != 0 ? "Debit " + Fmt.Money(line.Debit) : "Credit " + Fmt.Money(line.Credit), Balance(line.Balance), f - 0.5);
            }

            Blocks.Dashes(c);
            Blocks.Pair(c, "Closing balance", Balance(statement.Closing), f + 1, true);
            Blocks.Footer(c, company, PrintMarks.None, signature: false, narrow: true);
            return c.Finish($"Statement {party.Name}");
        }

        double[] widths = [0.11, 0.17, 0.36, 0.12, 0.12, 0.12];
        string[] headers = ["Date", "Voucher", "Particulars", "Debit", "Credit", "Balance"];
        void Header(PageComposer p)
        {
            p.Rule(0.8);
            p.Move(2);
            double x = 0;
            for (var i = 0; i < headers.Length; i++)
            {
                p.Text(x + 2, widths[i] * p.Width - 4, headers[i], f - 0.5, true, i >= 3 ? TextAlign.Right : TextAlign.Left);
                x += widths[i] * p.Width;
            }

            p.Move(p.LineHeight(f - 0.5) + 2);
            p.Rule(0.5);
            p.Move(3);
        }

        c.ContinuationHeader = p =>
        {
            p.Text(0, p.Width, $"{party.Name}: statement {Fmt.Date(statement.From)} to {Fmt.Date(statement.To)} (continued)", f, true);
            p.Move(p.LineHeight(f) + 4);
            Header(p);
        };
        Header(c);
        void Row(string[] cells, bool bold)
        {
            var particularsWidth = widths[2] * c.Width - 4;
            var lines = TextFit.Wrap(c.Measure, cells[2], particularsWidth, f - 0.5, bold, 2);
            var height = lines.Count * c.LineHeight(f - 0.5) + 2;
            c.Ensure(height);
            double x = 0;
            for (var i = 0; i < cells.Length; i++)
            {
                if (i == 2)
                {
                    for (var k = 0; k < lines.Count; k++) c.Add(new TextItem(x + 2, c.Y + k * c.LineHeight(f - 0.5), particularsWidth, lines[k], f - 0.5, bold));
                }
                else
                {
                    c.Text(x + 2, widths[i] * c.Width - 4, cells[i], f - 0.5, bold, i >= 3 ? TextAlign.Right : TextAlign.Left);
                }

                x += widths[i] * c.Width;
            }

            c.Move(height);
        }

        Row([Fmt.Date(statement.From), string.Empty, "Opening balance", string.Empty, string.Empty, Balance(statement.Opening)], true);
        foreach (var line in statement.Lines)
            Row([Fmt.Date(line.Date), line.Voucher, line.Particulars, line.Debit == 0 ? string.Empty : Fmt.Money(line.Debit),
                line.Credit == 0 ? string.Empty : Fmt.Money(line.Credit), Balance(line.Balance)], false);
        c.Rule(0.5);
        c.Move(3);
        Row([Fmt.Date(statement.To), string.Empty, "Closing balance", Fmt.Money(statement.Lines.Sum(l => l.Debit)), Fmt.Money(statement.Lines.Sum(l => l.Credit)),
            Balance(statement.Closing)], true);

        if (statement.OpenItems.Count > 0)
        {
            c.Move(8);
            c.Ensure(c.LineHeight(f) * 3);
            c.Text(0, c.Width, customer ? "Open documents" : "Open bills and payments", f, true);
            c.Move(c.LineHeight(f) + 2);
            foreach (var item in statement.OpenItems)
            {
                c.Ensure(c.LineHeight(f - 0.5));
                c.Text(0, c.Width * 0.6, $"{Fmt.Date(item.Date)}  {item.Type} {item.Number}", f - 0.5);
                c.Text(c.Width * 0.6, c.Width * 0.2, Fmt.Money(item.Amount), f - 0.5, false, TextAlign.Right);
                c.Text(c.Width * 0.8, c.Width * 0.2, (item.IsCharge ? string.Empty : "− ") + Fmt.Money(item.Open), f - 0.5, true, TextAlign.Right);
                c.Move(c.LineHeight(f - 0.5));
            }

            c.Move(2);
            Blocks.Pair(c, customer ? "Outstanding" : "Payable", Fmt.Rupees(party.Outstanding - party.Unapplied), f, true);
        }

        Blocks.Footer(c, company, PrintMarks.None, signature: false, narrow: false);
        return c.Finish($"Statement {party.Name}");
    }
}

/// <summary>
/// The printer test page: a frame on the margins, millimetre scales, text sizes, the ₹ sign, a table with aligned
/// amounts, a barcode and a QR code. The person printing it checks the paper and confirms in QIK Suite.
/// </summary>
public static class TestPageTemplate
{
    public static RenderedDocument Render(string printerName, string testCode, DateTime localTime, PageSetup setup, ITextMeasurer measure)
    {
        var c = new PageComposer(setup, measure, pageNumbers: false);
        var f = c.FontSize;
        var mm = PageSetup.PointsPerMm;
        // The frame marks the printable area; on rolls it is drawn at the end, around the content.
        if (!setup.IsRoll) c.Add(new RectItem(0, 0, c.Width, c.Setup.ContentHeight, false, 0.8));
        c.Move(6);
        c.Move(c.Wrapped(4, c.Width - 8, "QIK Suite test page", f + 4, true, TextAlign.Center));
        c.Move(c.Wrapped(4, c.Width - 8, $"Printer: {(printerName.Length > 0 ? printerName : "Windows default")}", f, false, TextAlign.Center));
        c.Move(c.Wrapped(4, c.Width - 8, $"Paper: {setup.Paper} · {setup.Orientation} · scale {setup.ScalePercent:0}%", f, false, TextAlign.Center));
        c.Move(c.Wrapped(4, c.Width - 8, $"Test {testCode} · {localTime:dd-MM-yyyy HH:mm}", f, false, TextAlign.Center));
        c.Move(6);

        // Millimetre scale: a tick every 5 mm, numbered every 10 mm. Measure it with a ruler to check the scale.
        var scaleWidth = Math.Floor((c.Width - 8) / (10 * mm)) * 10 * mm;
        c.Add(new LineItem(4, c.Y + 8, 4 + scaleWidth, c.Y + 8, 0.6));
        for (var tick = 0; tick * 5 * mm <= scaleWidth + 0.01; tick++)
        {
            var x = 4 + tick * 5 * mm;
            c.Add(new LineItem(x, c.Y + (tick % 2 == 0 ? 2 : 5), x, c.Y + 8, 0.5));
            if (tick % 2 == 0) c.Add(new TextItem(x - 6, c.Y + 9, 12, (tick * 5).ToString(System.Globalization.CultureInfo.InvariantCulture), 5, false, TextAlign.Center));
        }

        c.Move(18);
        c.Move(c.Wrapped(4, c.Width - 8, $"The scale above is {scaleWidth / mm:0} mm long.", f - 1, false, TextAlign.Center));
        c.Move(6);
        foreach (var size in new[] { 6.0, 8, 10, 12, 16 }.Where(s => s <= f * 2))
        {
            c.Ensure(c.LineHeight(size));
            c.Text(4, c.Width - 8, $"{size:0} pt: Rupee sign ₹ 1,23,456.78 — ABC abc 0123456789", size);
            c.Move(c.LineHeight(size));
        }

        c.Move(4);
        c.Text(4, c.Width - 8, "Bold text for headings", f, true);
        c.Move(c.LineHeight(f) + 4);
        foreach (var (label, amount) in new[] { ("Taxable value", 1_000.00m), ("CGST 9%", 90.00m), ("SGST 9%", 90.00m), ("Total", 1_180.00m) })
        {
            var bold = label == "Total";
            c.Text(8, c.Width * 0.6, label, f, bold);
            c.Text(c.Width * 0.6, c.Width * 0.4 - 8, Fmt.Money(amount), f, bold, TextAlign.Right);
            c.Move(c.LineHeight(f));
        }
        c.Move(6);
        var barcode = Code128.Encode(testCode);
        var barWidth = Math.Min(c.Width - 8, barcode.Modules.Length * 1.2);
        c.Ensure(48);
        c.Add(new BarcodeItem((c.Width - barWidth) / 2, c.Y, barWidth, 30, barcode.Modules));
        c.Move(32);
        c.Text(0, c.Width, testCode, f - 1, false, TextAlign.Center);
        c.Move(c.LineHeight(f - 1) + 4);
        var qr = QrCodes.Encode("QIK Suite test " + testCode);
        var side = Math.Min(72, c.Width * 0.4);
        c.Ensure(side + 6);
        c.Add(new MatrixItem((c.Width - side) / 2, c.Y, side, qr.Modules));
        c.Move(side + 8);
        c.Move(c.Wrapped(4, c.Width - 8, "Check: every side of the frame is visible, the scale measures correctly, all text is sharp and complete, and the codes scan. Then record the result in QIK Suite (Print centre > Printers and test pages).",
            f - 1, false, TextAlign.Center));
        c.Move(6);
        if (setup.IsRoll) c.Add(new RectItem(0, 0, c.Width, c.Y, false, 0.8));
        return c.Finish($"Test page {testCode}");
    }
}
