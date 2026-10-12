using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Nexora.Application.Billing;
using Nexora.Application.Security;
using Nexora.Domain.Billing;
using Nexora.Reporting.Export;
using Nexora.Reporting.Reports;

namespace Nexora.Tests.Phase8;

public sealed class ReportExportTests : ReportTestBase
{
    private ReportExportService Exports => Db.Get<ReportExportService>();

    private static readonly ReportRequest October2026 = new() { From = new DateOnly(2026, 10, 1), To = new DateOnly(2026, 10, 31), AsOf = new DateOnly(2026, 10, 31) };

    private async Task<Guid> HostileCustomerAsync()
    {
        var id = await Parties.SaveAsync(new PartyInput(null, PartyKind.Customer, new PartyDetails("=HYPERLINK(\"http://x\",\"pay\")", StateCode: "27")));
        await SellAsync(id, new BillingLineInput(Led, 1));
        return id;
    }

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+91 98200", "'+91 98200")]
    [InlineData("-x", "'-x")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\tcmd", "'\tcmd")]
    [InlineData("Meera Electricals", "Meera Electricals")]
    [InlineData("", "")]
    public void Text_that_a_spreadsheet_would_run_is_neutralised(string text, string expected)
    {
        Assert.Equal(expected, SpreadsheetSafety.Sanitize(text));
    }

    [Fact]
    public async Task Csv_export_neutralises_formulas_and_keeps_numbers_plain()
    {
        await HostileCustomerAsync();
        var file = await Exports.ExportAsync("sales.register", October2026, ExportFormat.Csv);
        var text = Encoding.UTF8.GetString(file.Content);

        Assert.EndsWith(".csv", file.FileName, StringComparison.Ordinal);
        Assert.Equal(Encoding.UTF8.GetPreamble(), file.Content[..3]);
        Assert.Contains("\"'=HYPERLINK(\"\"http://x\"\",\"\"pay\"\")\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain(",=HYPERLINK", text, StringComparison.Ordinal);
        Assert.Contains(",-177.00", text, StringComparison.Ordinal);      // the cancelled invoice, a negative number, not text
        Assert.Contains("2026-10-10", text, StringComparison.Ordinal);
        Assert.DoesNotContain("1,", text.Split("\r\n").Skip(8).First(), StringComparison.Ordinal);   // no digit grouping in data rows
    }

    [Fact]
    public async Task Excel_export_opens_with_typed_cells_and_neutralised_text()
    {
        await HostileCustomerAsync();
        var file = await Exports.ExportAsync("sales.register", October2026, ExportFormat.Excel);
        using var stream = new MemoryStream(file.Content);
        using var document = SpreadsheetDocument.Open(stream, false);
        var cells = document.WorkbookPart!.WorksheetParts.Single().Worksheet!.Descendants<Cell>().ToList();

        var texts = cells.Where(c => c.DataType?.Value == CellValues.InlineString).Select(c => c.InnerText).ToList();
        Assert.Contains("'=HYPERLINK(\"http://x\",\"pay\")", texts);
        Assert.DoesNotContain(texts, t => t.StartsWith('='));
        Assert.Contains(cells, c => c.DataType?.Value == CellValues.Number && decimal.Parse(c.CellValue!.Text, System.Globalization.CultureInfo.InvariantCulture) == -177m);
        Assert.Contains(cells, c => c.StyleIndex?.Value == 4 && c.CellValue!.Text == new DateTime(2026, 10, 10).ToOADate().ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.DoesNotContain(cells, c => c.CellFormula is not null);
    }

    [Fact]
    public async Task Pdf_export_goes_through_the_print_centre_and_uses_indian_grouping()
    {
        var file = await Exports.ExportAsync("accounts.trialbalance", October2026, ExportFormat.Pdf);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(file.Content, 0, 4));
        Assert.Contains(await Db.Get<Nexora.Printing.PrintingService>().HistoryAsync("report"), r => r.Output == Nexora.Domain.Printing.PrintOutput.Pdf);
        Assert.Equal("1,23,45,678.50", ReportPdf.Format(12_345_678.5m, ColumnKind.Amount));

        var preview = await Db.Get<Nexora.Printing.PrintingService>().PreviewAsync(await Exports.PrintSubjectAsync("accounts.trialbalance", October2026));
        Assert.Contains("Trial balance", preview.AllText, StringComparison.Ordinal);
        Assert.Contains("Cash", preview.AllText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exporting_needs_data_export_but_printing_does_not()
    {
        await SignInAsAsync("ravi", "Read-only");
        await Assert.ThrowsAsync<AccessDeniedException>(() => Exports.ExportAsync("sales.register", October2026, ExportFormat.Csv));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Exports.ExportAsync("sales.register", October2026, ExportFormat.Excel));
        Assert.NotNull(await Exports.PrintSubjectAsync("sales.register", October2026));
    }
}
