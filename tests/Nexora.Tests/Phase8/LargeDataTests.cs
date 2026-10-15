using System.Diagnostics;
using Nexora.Accounting;
using Nexora.Application.Billing;
using Nexora.Domain.Billing;
using Nexora.Reporting.Export;
using Nexora.Reporting.Reports;
using Xunit.Abstractions;

namespace Nexora.Tests.Phase8;

/// <summary>
/// 50,000 posted invoice lines (500 invoices × 100 lines). The lines are service lines, so seeding goes through the real
/// invoice, ledger and GST posting without stock costing; the reports read them with database queries and paging.
/// Timings are written to the test output; the limits are generous so slow machines do not fail.
/// </summary>
[Trait("Category", "Scale")]
public sealed class LargeDataTests(ITestOutputHelper output) : AccountingAdditionsBase
{
    private static int Invoices => int.TryParse(Environment.GetEnvironmentVariable("QIK_SCALE_INVOICES"), out var n) ? n : 500;

    [Fact]
    public async Task Fifty_thousand_invoice_lines_report_with_database_paging_and_reconcile()
    {
        var seeding = Stopwatch.StartNew();
        for (var i = 0; i < Invoices; i++)
        {
            var lines = Enumerable.Range(1, 100).Select(n => new BillingLineInput(Guid.Empty, 1, Rate: 100 + n, Description: $"Service {n:000}",
                LineType: BillingLineType.Service, TaxCategoryId: Gst18, HsnCode: "9987")).ToArray();
            var id = await Billing.SaveDraftAsync(new BillingDraftInput(null, BillingDocumentKind.SalesInvoice, October, i % 2 == 0 ? Local : Bengaluru, Main,
                SettlementMode.Credit, lines));
            await Billing.PostAsync(id);
        }

        seeding.Stop();
        var reports = Db.Get<ReportService>();
        var range = new ReportRequest { From = new DateOnly(2026, 10, 1), To = new DateOnly(2026, 10, 31), AsOf = new DateOnly(2026, 10, 31), PageSize = 100 };
        var timings = new List<string> { $"seeding {Invoices * 100:N0} lines: {seeding.Elapsed.TotalSeconds:0.0} s" };

        async Task<T> Time<T>(string name, Func<Task<T>> run)
        {
            var watch = Stopwatch.StartNew();
            var result = await run();
            timings.Add($"{name}: {watch.ElapsedMilliseconds} ms");
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(60), $"{name} took {watch.Elapsed.TotalSeconds:0.0} s");
            return result;
        }

        var page1 = await Time("sales register page 1", () => reports.RunAsync("sales.register", range));
        var page5 = await Time("sales register page 5", () => reports.RunAsync("sales.register", range with { Page = 5 }));
        var products = await Time("product-wise sales", () => reports.RunAsync("sales.products", range with { PageSize = 0 }));
        var gst = await Time("GST summary", () => reports.RunAsync("gst.summary", range with { PageSize = 0 }));
        var hsn = await Time("HSN/SAC summary", () => reports.RunAsync("gst.hsn", range with { PageSize = 0 }));
        var tb = await Time("trial balance", () => reports.RunAsync("accounts.trialbalance", range with { PageSize = 0 }));
        var ledger = await Time("sales ledger last page", () => reports.RunAsync("accounts.ledger", range with { Page = (Invoices + 99) / 100, AccountId = Accounts[SystemAccounts.Sales] }));
        var dashboard = await Time("dashboard", () => Db.Workspace.GetDashboardAsync());
        var export = await Time("sales register CSV (all rows)", () => Db.Get<ReportExportService>().ExportAsync("sales.register", range, ExportFormat.Csv));

        Assert.Equal(Math.Min(100, Invoices), page1.Rows.Count);
        Assert.Equal(Invoices, page1.TotalRows);                                   // the register lists documents
        Assert.Equal(100, products.Rows.Count);                                    // 50,000 lines grouped into 100 services
        Assert.Equal(page1.TotalRows, page5.TotalRows);
        var sales = -(await LedgerAsync(SystemAccounts.Sales, new DateOnly(2026, 10, 31)));
        Assert.Equal(sales, (decimal)page1.Totals![6]!);
        Assert.Equal(sales, (decimal)page5.Totals![6]!);
        Assert.Equal(sales, products.Rows.Sum(r => (decimal)r.Cells[3]!));
        Assert.Equal(sales, (decimal)gst.Rows.Single(r => ((string)r.Cells[0]!).StartsWith("Total output", StringComparison.Ordinal)).Cells[1]!);
        Assert.Equal((decimal)tb.Totals![3]!, (decimal)tb.Totals[4]!);
        Assert.Equal(Invoices - ((Invoices + 99) / 100 - 1) * 100 + 1, ledger.Rows.Count);   // brought forward + the last page
        Assert.Equal(sales, hsn.Rows.Where(r => (string)r.Cells[0]! == "Outward").Sum(r => (decimal)r.Cells[5]!));
        Assert.Equal(sales, dashboard.Metrics.Single(m => m.Label == "Sales").Value);
        Assert.True(export.Content.Length > Invoices * 50);
        output.WriteLine(string.Join(Environment.NewLine, timings));
    }
}
