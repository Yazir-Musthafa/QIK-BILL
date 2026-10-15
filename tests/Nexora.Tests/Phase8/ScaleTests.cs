using System.Diagnostics;
using System.Text;
using Nexora.Accounting;
using Nexora.Domain.Accounting;
using Nexora.Reporting.Export;
using Nexora.Reporting.Import;
using Nexora.Reporting.Reports;
using Xunit.Abstractions;

namespace Nexora.Tests.Phase8;

public sealed class ScaleTests(ITestOutputHelper output) : AccountingAdditionsBase
{
    [Fact]
    public async Task Two_thousand_posted_entries_page_and_export_with_reconciled_balances()
    {
        var bank = await AccountAsync("Primary Bank Account");
        var capital = await AccountAsync("Owner's Capital");
        decimal expected = 0;
        for (var batch = 0; batch < 8; batch++)
        {
            var lines = Enumerable.Range(1, 249).Select(i => new VoucherLineInput(bank, (batch * 249 + i) / 100m, 0)).ToList();
            var total = lines.Sum(l => l.Debit);
            lines.Add(new VoucherLineInput(capital, 0, total));
            await PostAsync(VoucherType.Receipt, October, [.. lines]);
            expected += total;
        }
        var reports = Db.Get<ReportService>();
        var request = new ReportRequest { From = October, To = October, AccountId = bank, PageSize = 100 };
        var watch = Stopwatch.StartNew();
        var all = await reports.RunAsync("accounts.ledger", request with { PageSize = 0 });
        Assert.Equal(1992, all.TotalRows);
        Assert.Equal(expected, all.Totals![6]);
        for (var page = 1; page <= 20; page++)
        {
            var part = await reports.RunAsync("accounts.ledger", request with { Page = page });
            Assert.Equal(expected, part.Totals![6]);
            var offset = (page - 1) * 100;
            Assert.Equal(offset == 0 ? 0m : all.Rows[offset].Cells[6], part.Rows[0].Cells[6]);
            Assert.Equal(all.Rows.Skip(offset + 1).Take(100).Select(r => r.Cells[6]), part.Rows.Skip(1).Select(r => r.Cells[6]));
        }
        var csv = await Db.Get<ReportExportService>().ExportAsync("accounts.ledger", request with { Page = 2 }, ExportFormat.Csv);
        var parsed = TabularReader.Read(csv.Content, csv.FileName);
        Assert.Equal(1992, parsed.Rows.Count(r => r.Cells.Count > 3 && r.Cells[2] == "Receipt"));
        var tb = await Db.Get<LedgerService>().TrialBalanceAsync(October);
        Assert.True(tb.IsBalanced);
        Assert.Equal(expected, tb.Rows.Single(r => r.AccountId == bank).Net);
        output.WriteLine($"2,000 posted entries; ledger paging, full CSV and trial balance checked in {watch.ElapsedMilliseconds} ms (after seeding).");
    }

    [Fact]
    public async Task Thousand_products_preview_without_writes_then_import_and_round_trip()
    {
        var transfer = Db.Get<DataTransferService>();
        var content = Encoding.UTF8.GetBytes("SKU,Name,Unit,Selling price\n" + string.Join('\n',
            Enumerable.Range(1, 1000).Select(i => $"SCALE-{i:0000},Scale item {i},PCS,{i}")));
        var watch = Stopwatch.StartNew();
        var preview = await transfer.PreviewAsync(ImportKind.Products, content, "scale.csv");
        Assert.True(preview.CanImport);
        Assert.Equal(1000, preview.ValidRows);
        Assert.DoesNotContain(await Catalogue.ListProductsAsync(), p => p.Sku.StartsWith("SCALE-", StringComparison.Ordinal));
        Assert.Equal(1000, (await transfer.ImportAsync(ImportKind.Products, content, "scale.csv")).Created);
        Assert.Equal(1000, (await Catalogue.ListProductsAsync()).Count(p => p.Sku.StartsWith("SCALE-", StringComparison.Ordinal)));
        var excel = await transfer.ExportAsync(ImportKind.Products, ExportFormat.Excel);
        Assert.Equal(1000, TabularReader.Read(excel.Content, excel.FileName).Rows.Count(r => r.Cells[0].StartsWith("SCALE-", StringComparison.Ordinal)));
        output.WriteLine($"1,000 products previewed, imported and exported to Excel in {watch.ElapsedMilliseconds} ms.");
    }
}
