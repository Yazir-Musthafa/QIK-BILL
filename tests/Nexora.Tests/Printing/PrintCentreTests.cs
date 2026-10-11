using Microsoft.Data.Sqlite;
using Nexora.Application;
using Nexora.Application.Billing;
using Nexora.Application.Companies;
using Nexora.Application.Security;
using Nexora.Domain.Billing;
using Nexora.Domain.Printing;
using Nexora.Printing;
using Nexora.Printing.Layout;
using Nexora.Printing.Templates;
using Nexora.Tests.Billing;

namespace Nexora.Tests.Printing;

/// <summary>Printing posted documents through the print centre with a fake printer layer.</summary>
public abstract class PrintTestBase : BillingTestBase
{
    protected PrintingService Printing => Db.Get<PrintingService>();

    protected FakePrinterGateway Printer => Db.Printers;

    protected async Task UseProfileAsync(PrintPurpose purpose, PaperKind paper, string printer = "", bool escPos = false, decimal font = 9)
    {
        var thermal = paper is PaperKind.Thermal58 or PaperKind.Thermal80;
        var margin = thermal ? 3m : 12m;
        await Printing.SaveProfileAsync(new PrinterProfileRow(purpose, printer, paper, PageOrientation.Portrait, margin, margin, margin, margin, 100, font, 1, escPos));
    }
}

public sealed class DocumentPrintTests : PrintTestBase
{
    [Fact]
    public async Task Invoice_prints_the_posted_figures_and_company_details()
    {
        await Db.Get<CompanyService>().UpdateProfileAsync(new Nexora.Domain.Companies.CompanyProfile("Acme Traders", "Acme Traders Pvt Ltd", "27", "12 MG Road", "",
            "Pune", "411001", "9820000000", "accounts@acme.example", "AAGCB7383J", Gstin("27", "AAGCB7383J")));
        var (id, number) = await SellAsync(Local, new BillingLineInput(Led, 3, DiscountPercent: 10));
        var posted = await Billing.GetAsync(id);

        var page = await Printing.PreviewAsync(new BillingSubject(id));
        var text = page.AllText;

        Assert.Contains("TAX INVOICE", text, StringComparison.Ordinal);
        Assert.Contains(number, text, StringComparison.Ordinal);
        Assert.Contains("Acme Traders Pvt Ltd", text, StringComparison.Ordinal);
        Assert.Contains("GSTIN 27AAGCB7383J1Z", text, StringComparison.Ordinal);
        Assert.Contains("Meera Electricals", text, StringComparison.Ordinal);
        Assert.Contains("₹ " + Nexora.Application.Formatting.IndianNumberFormat.Number(posted.Total), page.Texts.Select(t => t.Text));
        foreach (var amount in new[] { posted.Taxable, posted.Cgst, posted.Sgst, posted.RoundOff })
            Assert.Contains(Nexora.Application.Formatting.IndianNumberFormat.Number(amount), page.Texts.Select(t => t.Text));
        Assert.Contains(AmountInWords.Rupees(posted.Total), text, StringComparison.Ordinal);
        Assert.Contains("Authorised signatory", text, StringComparison.Ordinal);
        Assert.Empty(Printer.Jobs);                                   // a preview prints nothing
        Assert.Empty(await Printing.HistoryAsync("billing", id));     // and records nothing
    }

    [Fact]
    public async Task Tax_summary_groups_stored_line_taxes_and_matches_the_header()
    {
        var plain = await TaxedProductAsync("PLAIN", (await Catalogue.ListTaxCategoriesAsync()).Single(t => t.ReferenceRatePercent == 5).Id, selling: 99.99m);
        await ReceiveAsync(plain, 10, 50);
        var (id, _) = await SellAsync(Local, new BillingLineInput(Led, 2), new BillingLineInput(plain, 3), new BillingLineInput(Led, 1, Rate: 149.99m));
        var d = await Billing.GetAsync(id);

        var groups = InvoiceTemplate.TaxGroups(d);

        Assert.Equal([5m, 18m], groups.Select(g => g.Rate));
        Assert.Equal(d.Taxable, groups.Sum(g => g.Taxable));
        Assert.Equal(d.Cgst, groups.Sum(g => g.Cgst));
        Assert.Equal(d.Sgst, groups.Sum(g => g.Sgst));
    }

    [Fact]
    public async Task Long_invoices_paginate_with_repeated_headers_and_totals_on_the_last_page()
    {
        await UseProfileAsync(PrintPurpose.Invoices, PaperKind.A5, font: 8);
        await ReceiveAsync(Led, 200, 100);
        var lines = Enumerable.Range(0, 60).Select(_ => new BillingLineInput(Led, 1)).ToArray();
        var (id, number) = await SellAsync(Local, lines);
        var d = await Billing.GetAsync(id);

        var document = await Printing.PreviewAsync(new BillingSubject(id));

        Assert.True(document.Pages.Count >= 3, $"{document.Pages.Count} pages");
        var bottom = document.Pages[0].Height - 12 * PageSetup.PointsPerMm;
        for (var p = 0; p < document.Pages.Count; p++)
        {
            var texts = document.Pages[p].Items.OfType<TextItem>().ToList();
            Assert.All(texts, t => Assert.True(t.Y + t.Size * 1.25 <= bottom + 0.01, $"page {p + 1}: \"{t.Text}\" is below the margin"));
            Assert.Contains(texts, t => t.Text == $"Page {p + 1} of {document.Pages.Count}");
            if (p > 0) Assert.Contains(texts, t => t.Text.Contains($"{number} (continued)", StringComparison.Ordinal));
        }

        var rows = document.Texts.Count(t => t.Text == Nexora.Application.Formatting.IndianNumberFormat.Number(177m));
        Assert.Equal(60, rows);                                                  // every line printed once
        var total = "₹ " + Nexora.Application.Formatting.IndianNumberFormat.Number(d.Total);
        Assert.Contains(document.Pages[^1].Items.OfType<TextItem>(), t => t.Text == total);
        Assert.DoesNotContain(document.Pages[0].Items.OfType<TextItem>(), t => t.Text == total);
    }

    [Fact]
    public async Task Cash_sales_print_on_an_80_mm_roll_as_one_long_page()
    {
        await UseProfileAsync(PrintPurpose.Receipts, PaperKind.Thermal80);
        var (id, _) = await CashSaleAsync(new BillingLineInput(Led, 2));

        var document = await Printing.PreviewAsync(new BillingSubject(id), PrintPurpose.Receipts);

        var page = Assert.Single(document.Pages);
        Assert.Equal(80 * PageSetup.PointsPerMm, page.Width, 2);
        Assert.Contains("CASH BILL", document.AllText, StringComparison.Ordinal);
        Assert.Contains("₹ 354.00", document.Texts.Select(t => t.Text));
        Assert.All(document.Texts, t => Assert.True(t.X >= 0 && t.X + t.Width <= page.Width + 0.01));
    }

    [Fact]
    public async Task Delivery_notes_show_goods_without_prices()
    {
        var (id, number) = await SellAsync(Local, new BillingLineInput(Led, 3));

        var text = (await Printing.PreviewAsync(new BillingSubject(id, DocumentStyle.DeliveryNote))).AllText;

        Assert.Contains("DELIVERY NOTE", text, StringComparison.Ordinal);
        Assert.Contains("invoice " + number, text, StringComparison.Ordinal);
        Assert.DoesNotContain("531", text, StringComparison.Ordinal);
        Assert.DoesNotContain("450.00", text, StringComparison.Ordinal);
        Assert.Contains("Receiver's signature", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Purchase_orders_print_only_from_draft_purchase_bills()
    {
        var draft = await Billing.SaveDraftAsync(new BillingDraftInput(null, BillingDocumentKind.PurchaseBill, October, Vendor, Main, SettlementMode.Credit,
            [new BillingLineInput(Led, 10, Rate: 95)]));

        var text = (await Printing.PreviewAsync(new BillingSubject(draft, DocumentStyle.PurchaseOrder))).AllText;

        Assert.Contains("PURCHASE ORDER", text, StringComparison.Ordinal);
        Assert.Contains("Pune Wholesale", text, StringComparison.Ordinal);
        Assert.DoesNotContain("DRAFT", text, StringComparison.Ordinal);
        await Billing.PostAsync(draft);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Printing.PreviewAsync(new BillingSubject(draft, DocumentStyle.PurchaseOrder)));
    }

    [Fact]
    public async Task Drafts_and_cancelled_documents_carry_a_banner()
    {
        var draft = await DraftAsync(BillingDocumentKind.SalesInvoice, Local, new BillingLineInput(Led, 1));
        Assert.Contains("DRAFT · NOT A VALID DOCUMENT", (await Printing.PreviewAsync(new BillingSubject(draft))).AllText, StringComparison.Ordinal);

        await Billing.PostAsync(draft);
        await Billing.CancelAsync(draft, October, "Wrong customer");
        Assert.Contains("CANCELLED", (await Printing.PreviewAsync(new BillingSubject(draft))).AllText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Credit_notes_debit_notes_and_bills_have_their_own_titles()
    {
        var (invoice, _) = await SellAsync(Local, new BillingLineInput(Led, 2));
        var (credit, _) = await ReturnAsync(invoice, (1, 1));
        var (bill, _) = await BuyAsync("PW/1", new BillingLineInput(Led, 5, Rate: 100));
        var (debit, _) = await ReturnAsync(bill, (1, 1));

        Assert.Contains("CREDIT NOTE", (await Printing.PreviewAsync(new BillingSubject(credit))).AllText, StringComparison.Ordinal);
        Assert.Contains("PURCHASE BILL", (await Printing.PreviewAsync(new BillingSubject(bill))).AllText, StringComparison.Ordinal);
        var debitText = (await Printing.PreviewAsync(new BillingSubject(debit))).AllText;
        Assert.Contains("DEBIT NOTE", debitText, StringComparison.Ordinal);
        Assert.Contains("PW/1", (await Printing.PreviewAsync(new BillingSubject(bill))).AllText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Receipts_print_the_amount_allocations_and_words()
    {
        var (invoice, number) = await SellAsync(Local, new BillingLineInput(Led, 1));
        var (receipt, receiptNumber) = await Settlements.PostAsync(new SettlementInput(SettlementKind.CustomerReceipt, October, Local, CashAccount, 500,
            PaymentMethod.Upi, [new AllocationInput(invoice, 177)], Reference: "UPI 4471"));

        var text = (await Printing.PreviewAsync(new SettlementSubject(receipt))).AllText;

        Assert.Contains("RECEIPT", text, StringComparison.Ordinal);
        Assert.Contains(receiptNumber, text, StringComparison.Ordinal);
        Assert.Contains("₹ 500.00", text, StringComparison.Ordinal);
        Assert.Contains("Rupees Five Hundred Only", text, StringComparison.Ordinal);
        Assert.Contains("Sales invoice " + number, text, StringComparison.Ordinal);
        Assert.Contains("323.00", text, StringComparison.Ordinal);     // on account
        Assert.Contains("UPI 4471", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Statements_show_opening_lines_closing_and_open_documents()
    {
        var (invoice, number) = await SellAsync(Local, new BillingLineInput(Led, 2));          // 354
        await Settlements.PostAsync(new SettlementInput(SettlementKind.CustomerReceipt, October, Local, CashAccount, 100, PaymentMethod.Cash, [new AllocationInput(invoice, 100)]));

        var statement = await Parties.StatementAsync(Local, October.AddDays(-9), October);
        var text = (await Printing.PreviewAsync(new StatementSubject(Local, October.AddDays(-9), October))).AllText;

        Assert.Equal((0m, 254m), (statement.Opening, statement.Closing));
        Assert.Equal([354m, 0m], statement.Lines.Select(l => l.Debit));
        Assert.Contains("STATEMENT OF ACCOUNT", text, StringComparison.Ordinal);
        Assert.Contains("254.00 Dr", text, StringComparison.Ordinal);
        Assert.Contains(number, text, StringComparison.Ordinal);
        Assert.Contains("₹ 254.00", text, StringComparison.Ordinal);
        var later = await Parties.StatementAsync(Local, October.AddDays(1), October.AddDays(5));
        Assert.Equal((254m, 254m, 0), (later.Opening, later.Closing, later.Lines.Count));
    }

    [Fact]
    public async Task Invoice_reference_qr_follows_the_configured_format_and_is_labelled_as_not_an_e_invoice()
    {
        await Printing.SaveSettingsAsync(new PrintSettingsRow(true, "{sku}", "INV {number} {date} {total}", "Bank: HDFC 1234"));
        var (id, number) = await SellAsync(Local, new BillingLineInput(Led, 1));

        var document = await Printing.PreviewAsync(new BillingSubject(id));

        var qr = Assert.Single(document.Pages.SelectMany(p => p.Items).OfType<MatrixItem>());
        Assert.Equal($"INV {number} 10-10-2026 177.00", BarcodeTests.DecodeQr(new Nexora.Printing.Barcodes.QrMatrix("", qr.Modules)));
        Assert.Contains("Not an e-invoice QR", document.AllText, StringComparison.Ordinal);
        Assert.Contains("Bank: HDFC 1234", document.AllText, StringComparison.Ordinal);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Printing.SaveSettingsAsync(new PrintSettingsRow(true, "{colour}", string.Empty, string.Empty)));
    }

    [Fact]
    public async Task Pdf_export_uses_the_same_pages_embeds_the_font_and_counts_as_a_print()
    {
        // The test company has no GSTIN, so its invoices are titled INVOICE, not TAX INVOICE.
        var (id, number) = await SellAsync(Local, new BillingLineInput(Led, 1));

        var pdf = await Printing.ExportPdfAsync(new BillingSubject(id));

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf.Content, 0, 4));
        var raw = System.Text.Encoding.Latin1.GetString(pdf.Content);
        Assert.Contains("+Noto#20Sans", raw, StringComparison.Ordinal);      // a subset of the bundled font
        Assert.Contains("/FontFile2", raw, StringComparison.Ordinal);        // embedded, not referenced
        Assert.Equal($"INVOICE {number}.pdf".Replace('/', '-'), pdf.FileName);
        var record = Assert.Single(await Printing.HistoryAsync("billing", id));
        Assert.Equal((PrintOutput.Pdf, 1), (record.Output, record.PrintNumber));
    }
}

public sealed class ReprintTests : PrintTestBase
{
    [Fact]
    public async Task Second_print_is_counted_and_marked_duplicate()
    {
        var (id, _) = await SellAsync(Local, new BillingLineInput(Led, 1));

        var first = await Printing.PrintAsync(new BillingSubject(id));
        var second = await Printing.PrintAsync(new BillingSubject(id), copies: 2);

        Assert.True(first.Succeeded && second.Succeeded);
        Assert.Equal((1, false, 2, true), (first.PrintNumber, first.MarkedDuplicate, second.PrintNumber, second.MarkedDuplicate));
        Assert.DoesNotContain("DUPLICATE", Printer.Jobs[0].Document.AllText, StringComparison.Ordinal);
        Assert.Contains("DUPLICATE", Printer.Jobs[1].Document.AllText, StringComparison.Ordinal);
        Assert.Equal(2, Printer.Jobs[1].Copies);
        var history = await Printing.HistoryAsync("billing", id);
        Assert.Equal([2, 1], history.Select(h => h.PrintNumber));
        Assert.Equal("Asha Rao", history[0].PrintedBy);
        Assert.True(history[0].IsReprint);
        Assert.Contains("DUPLICATE", (await Printing.PreviewAsync(new BillingSubject(id))).AllText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Duplicate_marking_can_be_turned_off_but_reprints_are_still_counted()
    {
        await Printing.SaveSettingsAsync(new PrintSettingsRow(false, "{sku}", string.Empty, string.Empty));
        var (id, _) = await SellAsync(Local, new BillingLineInput(Led, 1));

        await Printing.PrintAsync(new BillingSubject(id));
        var again = await Printing.PrintAsync(new BillingSubject(id));

        Assert.Equal((2, false), (again.PrintNumber, again.MarkedDuplicate));
        Assert.DoesNotContain("DUPLICATE", Printer.Jobs[1].Document.AllText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_print_is_not_recorded_and_explains_how_to_recover()
    {
        await UseProfileAsync(PrintPurpose.Invoices, PaperKind.A4, "Label Printer");
        var (id, _) = await SellAsync(Local, new BillingLineInput(Led, 1));
        Printer.NextFailure = new PrintOutcome(false, PrintFailure.Offline, "The printer is offline.");

        var result = await Printing.PrintAsync(new BillingSubject(id));

        Assert.False(result.Succeeded);
        Assert.Contains("Could not print to Label Printer", result.Message, StringComparison.Ordinal);
        Assert.Contains("Use printer offline", result.Guidance, StringComparison.Ordinal);
        Assert.Empty(await Printing.HistoryAsync("billing", id));
        Assert.Equal(1, (await Printing.PrintAsync(new BillingSubject(id))).PrintNumber);
    }

    [Fact]
    public async Task Jobs_go_to_the_profile_printer_with_its_paper_and_copies()
    {
        await Printing.SaveProfileAsync(new PrinterProfileRow(PrintPurpose.Invoices, "Office LaserJet", PaperKind.A5, PageOrientation.Landscape, 10, 10, 10, 10, 90, 8, 3, false));
        var (id, _) = await SellAsync(Local, new BillingLineInput(Led, 1));

        await Printing.PrintAsync(new BillingSubject(id));

        var job = Assert.Single(Printer.Jobs);
        Assert.Equal(("Office LaserJet", 3, PaperKind.A5, PageOrientation.Landscape), (job.PrinterName, job.Copies, job.Paper, job.Orientation));
        Assert.Equal(210 * PageSetup.PointsPerMm, job.Document.Pages[0].Width, 2);
        var profile = (await Printing.ProfilesAsync()).Single(p => p.Purpose == PrintPurpose.Invoices);
        Assert.Equal((90m, 8m), (profile.ScalePercent, profile.FontSize));
    }

    [Fact]
    public async Task Experimental_esc_pos_sends_raw_receipt_bytes_only_when_enabled()
    {
        await UseProfileAsync(PrintPurpose.Receipts, PaperKind.Thermal58, "POS-80 Thermal", escPos: true);
        var (id, _) = await CashSaleAsync(new BillingLineInput(Led, 1));

        var result = await Printing.PrintAsync(new BillingSubject(id), PrintPurpose.Receipts);

        Assert.True(result.Succeeded);
        Assert.Empty(Printer.Jobs);
        var (printer, data) = Assert.Single(Printer.RawJobs);
        Assert.Equal("POS-80 Thermal", printer);
        Assert.Contains("TOTAL Rs.", System.Text.Encoding.ASCII.GetString(data), StringComparison.Ordinal);
        Assert.Equal(PrintOutput.EscPos, Assert.Single(await Printing.HistoryAsync("billing", id)).Output);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => UseProfileAsync(PrintPurpose.Invoices, PaperKind.A4, "Office LaserJet", escPos: true));
    }
}

public sealed class TestPrintTests : PrintTestBase
{
    [Fact]
    public async Task A_test_page_passes_only_when_someone_confirms_it()
    {
        var started = await Printing.StartTestAsync("Office LaserJet", PaperKind.A4);

        Assert.Equal(TestPrintOutcome.Sent, started.Test.Outcome);
        var page = Assert.Single(Assert.Single(Printer.Jobs).Document.Pages);
        Assert.Contains(page.Items.OfType<TextItem>(), t => t.Text.Contains("Printer: Office LaserJet", StringComparison.Ordinal));
        Assert.Single(page.Items.OfType<BarcodeItem>());
        Assert.Null((await Printing.ListPrintersAsync()).Single(p => p.Printer.Name == "Office LaserJet").LastTest);

        await Printing.ConfirmTestAsync(started.Test.Id, true, string.Empty);

        var row = (await Printing.ListPrintersAsync()).Single(p => p.Printer.Name == "Office LaserJet");
        Assert.Equal(TestPrintOutcome.ConfirmedGood, row.LastTest);
        Assert.Equal("Asha Rao", (await Printing.TestsAsync()).Single().ConfirmedBy);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Printing.ConfirmTestAsync(started.Test.Id, true, string.Empty));
    }

    [Fact]
    public async Task Problems_need_a_description_and_failed_sends_cannot_be_confirmed()
    {
        var started = await Printing.StartTestAsync("POS-80 Thermal", PaperKind.Thermal80);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Printing.ConfirmTestAsync(started.Test.Id, false, " "));
        await Printing.ConfirmTestAsync(started.Test.Id, false, "Right edge cut off");
        Assert.Equal(TestPrintOutcome.ReportedProblem, (await Printing.ListPrintersAsync()).Single(p => p.Printer.Name == "POS-80 Thermal").LastTest);

        Printer.NextFailure = new PrintOutcome(false, PrintFailure.NotFound, "No such printer.");
        var failed = await Printing.StartTestAsync("Gone Printer", PaperKind.A4);

        Assert.Equal(TestPrintOutcome.SendFailed, failed.Test.Outcome);
        Assert.Contains("Printers & scanners", failed.Guidance, StringComparison.Ordinal);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Printing.ConfirmTestAsync(failed.Test.Id, true, string.Empty));
    }

    [Fact]
    public async Task Printer_list_keeps_what_the_driver_reports_without_inventing_capabilities()
    {
        var printers = await Printing.ListPrintersAsync();

        Assert.Equal("Office LaserJet", printers[0].Printer.Name);         // default first
        Assert.True(printers[0].Printer.CapabilitiesKnown);
        var thermal = printers.Single(p => p.Printer.Name == "POS-80 Thermal").Printer;
        Assert.False(thermal.CapabilitiesKnown);
        Assert.Empty(thermal.PaperSizes);
        Assert.Equal(PrinterState.Offline, printers.Single(p => p.Printer.Name == "Label Printer").Printer.State);
    }
}

public sealed class LabelPrintTests : PrintTestBase
{
    [Fact]
    public async Task Labels_print_products_with_the_seeded_sheet_layout()
    {
        await Catalogue.SaveProductAsync(new Nexora.Application.Inventory.ProductInput(Led, new Nexora.Domain.Inventory.ProductDetails("LED-18W", "LED-18W item",
            Barcode: "4006381333931", HsnCode: "9405", TaxCategoryId: Gst18, PurchasePrice: 90, SellingPrice: 150, Mrp: 160), Units["PCS"], Nexora.Domain.Inventory.TrackingMode.None, false));
        var sheet = (await Printing.LabelLayoutsAsync()).Single(l => l.Name.StartsWith("A4 sheet", StringComparison.Ordinal));

        var result = await Printing.PrintAsync(new LabelSubject(sheet.Id, [new LabelRequest(Led, 30)]));

        Assert.True(result.Succeeded);
        var job = Assert.Single(Printer.Jobs);
        Assert.Equal(2, job.Document.Pages.Count);
        Assert.Equal(30, job.Document.Texts.Count(t => t.Text == "₹ 150.00"));
        Assert.Equal(30, job.Document.Texts.Count(t => t.Text == "MRP ₹ 160.00 (incl. of all taxes)"));
        Assert.Equal(30, job.Document.Pages.Sum(p => p.Items.OfType<BarcodeItem>().Count()));
    }

    [Fact]
    public async Task Label_layouts_are_validated_and_qr_labels_use_the_company_format()
    {
        await Printing.SaveSettingsAsync(new PrintSettingsRow(true, "{sku}|{price}", string.Empty, string.Empty));
        var id = await Printing.SaveLabelLayoutAsync(new LabelLayoutInput(null, "QR roll 38 × 25", 38, 25, 38, 25, 1, 1, 0, 0, 0, 0, true, false, true, false, LabelCode.Qr));

        var document = await Printing.PreviewAsync(new LabelSubject(id, [new LabelRequest(Led, 2)]));

        Assert.Equal(2, document.Pages.Count);
        Assert.Equal("LED-18W|150.00", BarcodeTests.DecodeQr(new Nexora.Printing.Barcodes.QrMatrix("", document.Pages[0].Items.OfType<MatrixItem>().Single().Modules)));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Printing.SaveLabelLayoutAsync(new LabelLayoutInput(null, "Too wide", 50, 25, 60, 25, 1, 1, 0, 0, 0, 0, true, false, true, false, LabelCode.None)));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Printing.SaveLabelLayoutAsync(new LabelLayoutInput(null, "QR roll 38 × 25", 38, 25, 38, 25, 1, 1, 0, 0, 0, 0, true, false, true, false, LabelCode.Qr)));
        await Printing.SetLabelLayoutActiveAsync(id, false);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Printing.PreviewAsync(new LabelSubject(id, [new LabelRequest(Led, 1)])));
    }

    [Fact]
    public async Task Scanned_codes_find_products_by_barcode_qr_identifier_or_sku()
    {
        await Catalogue.SaveProductAsync(new Nexora.Application.Inventory.ProductInput(Led, new Nexora.Domain.Inventory.ProductDetails("LED-18W", "LED-18W item",
            Barcode: "4006381333931", QrCode: "QIK:LED18", TaxCategoryId: Gst18, PurchasePrice: 90, SellingPrice: 150), Units["PCS"], Nexora.Domain.Inventory.TrackingMode.None, false));

        Assert.Equal(Led, (await Catalogue.FindByCodeAsync("4006381333931"))!.Id);
        Assert.Equal(Led, (await Catalogue.FindByCodeAsync("QIK:LED18"))!.Id);
        Assert.Equal(Led, (await Catalogue.FindByCodeAsync(" led-18w "))!.Id);
        Assert.Null(await Catalogue.FindByCodeAsync("4006381333932"));
    }
}

public sealed class PrintAccessTests : PrintTestBase
{
    [Fact]
    public async Task Billing_operator_prints_sales_documents_but_cannot_change_printers_or_print_purchases()
    {
        var (invoice, _) = await SellAsync(Local, new BillingLineInput(Led, 1));
        var (bill, _) = await BuyAsync("PW/7", new BillingLineInput(Led, 1));
        await SignInAsAsync("bina", "Billing Operator");
        var profile = (await Printing.ProfilesAsync())[0];

        Assert.True((await Printing.PrintAsync(new BillingSubject(invoice))).Succeeded);
        await Assert.ThrowsAsync<AccessDeniedException>(() => Printing.PreviewAsync(new BillingSubject(bill)));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Printing.SaveProfileAsync(profile));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Printing.StartTestAsync("Office LaserJet", PaperKind.A4));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Printing.SaveSettingsAsync(new PrintSettingsRow(false, "{sku}", string.Empty, string.Empty)));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Printing.HistoryAsync());
        Assert.Single(await Printing.HistoryAsync("billing", invoice));
    }

    [Fact]
    public async Task Inventory_manager_prints_labels_and_bills_but_not_sales_invoices()
    {
        var (invoice, _) = await SellAsync(Local, new BillingLineInput(Led, 1));
        var layout = (await Printing.LabelLayoutsAsync())[0];
        await SignInAsAsync("ivan", "Inventory Manager");

        Assert.True((await Printing.PrintAsync(new LabelSubject(layout.Id, [new LabelRequest(Led, 1)]))).Succeeded);
        await Assert.ThrowsAsync<AccessDeniedException>(() => Printing.PrintAsync(new BillingSubject(invoice)));
    }

    [Fact]
    public async Task Another_companys_documents_and_print_history_are_out_of_reach()
    {
        var (invoice, _) = await SellAsync(Local, new BillingLineInput(Led, 1));
        await Printing.PrintAsync(new BillingSubject(invoice));
        var demo = (await Db.Workspace.GetCompaniesAsync()).Single(c => c.IsDemo);
        await Db.Workspace.SwitchCompanyAsync(demo.Id);

        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Printing.PreviewAsync(new BillingSubject(invoice)));
        Assert.Empty(await Printing.HistoryAsync());
        Assert.Empty(await Printing.HistoryAsync("billing", invoice));
        Assert.Equal(4, (await Printing.ProfilesAsync()).Count);
        Assert.Equal(2, (await Printing.LabelLayoutsAsync()).Count);
    }

    [Fact]
    public async Task Print_history_cannot_be_edited_in_the_database()
    {
        var (invoice, _) = await SellAsync(Local, new BillingLineInput(Led, 1));
        await Printing.PrintAsync(new BillingSubject(invoice));

        using var connection = Db.OpenRawConnection();
        foreach (var sql in new[] { "UPDATE PrintRecords SET Copies = 9", "DELETE FROM PrintRecords" })
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
        }
    }

    [Fact]
    public async Task New_and_upgraded_companies_get_print_settings_once()
    {
        Assert.Equal([PrintPurpose.Invoices, PrintPurpose.Receipts, PrintPurpose.Reports, PrintPurpose.Labels], (await Printing.ProfilesAsync()).Select(p => p.Purpose));
        Assert.Equal(new PrintSettingsRow(true, "{sku}", string.Empty, string.Empty), await Printing.SettingsAsync());

        using (var connection = Db.OpenRawConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM PrintSettings; DELETE FROM PrinterProfiles; DELETE FROM LabelLayouts;";
            command.ExecuteNonQuery();
        }

        await Db.Workspace.InitializeAsync();
        await Db.Workspace.InitializeAsync();
        await SignInAsAdministratorAsync();
        Assert.Equal(4, (await Printing.ProfilesAsync()).Count);
        Assert.Equal(2, (await Printing.LabelLayoutsAsync()).Count);
    }
}
