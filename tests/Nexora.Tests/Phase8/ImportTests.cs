using System.Text;
using Nexora.Accounting;
using Nexora.Application;
using Nexora.Application.Security;
using Nexora.Domain.Billing;
using Nexora.Reporting.Export;
using Nexora.Reporting.Import;
using Nexora.Tests.Billing;

namespace Nexora.Tests.Phase8;

public sealed class ImportTests : BillingTestBase
{
    private DataTransferService Transfer => Db.Get<DataTransferService>();

    private static byte[] Csv(params string[] lines) => Encoding.UTF8.GetBytes(string.Join("\r\n", lines));

    [Fact]
    public async Task Products_import_after_a_clean_preview_and_appear_in_the_catalogue()
    {
        var file = Csv("SKU,Name,Unit,Tax category,Selling price,Purchase price,MRP,Tracking,Track expiry",
            "fan-1200,Ceiling fan,PCS,GST 18%,\"2,450.00\",1800,2999,None,No",
            "SYRUP-1,Cough syrup,PCS,,60,40,75,Batch,Yes");

        var preview = await Transfer.PreviewAsync(ImportKind.Products, file, "products.csv");
        Assert.True(preview.CanImport);
        Assert.Equal(2, preview.ValidRows);
        Assert.Empty(await NewProductsAsync());   // the preview saved nothing

        var result = await Transfer.ImportAsync(ImportKind.Products, file, "products.csv");
        Assert.Equal(2, result.Created);
        var fan = (await Catalogue.ListProductsAsync()).Single(p => p.Sku == "FAN-1200");
        Assert.Equal((2_450m, 1_800m, Gst18), (fan.SellingPrice, fan.PurchasePrice!.Value, fan.TaxCategoryId!.Value));
        Assert.True((await Catalogue.ListProductsAsync()).Single(p => p.Sku == "SYRUP-1").TracksExpiry);
    }

    [Fact]
    public async Task One_bad_row_stops_the_whole_import_and_every_error_is_reported()
    {
        var file = Csv("SKU,Name,Unit,Selling price,MRP",
            "OK-1,Good row,PCS,100,120",
            "LED-18W,Duplicate of the fixture,PCS,1,2",
            "OK-1,Duplicate in the file,PCS,1,2",
            "BAD-UNIT,No such unit,BOX9,1,2",
            ",No SKU,PCS,abc,2",
            "PRICE,Selling above MRP,PCS,500,100");

        var preview = await Transfer.PreviewAsync(ImportKind.Products, file, "bad.csv");
        Assert.False(preview.CanImport);
        Assert.Equal(1, preview.ValidRows);
        Assert.Equal([2, 3, 4, 5, 6, 7], preview.Rows.Select(r => r.RowNumber));
        Assert.Contains(preview.Rows[1].Errors, e => e.Contains("already exists", StringComparison.Ordinal));
        Assert.Contains(preview.Rows[2].Errors, e => e.Contains("already exists", StringComparison.Ordinal));
        Assert.Contains(preview.Rows[3].Errors, e => e.Contains("BOX9", StringComparison.Ordinal));
        Assert.Equal(2, preview.Rows[4].Errors.Count);                                  // SKU required, price not a number
        Assert.Contains(preview.Rows[5].Errors, e => e.Contains("MRP", StringComparison.Ordinal));   // the domain rule

        var refused = await Assert.ThrowsAsync<ImportRejectedException>(() => Transfer.ImportAsync(ImportKind.Products, file, "bad.csv"));
        Assert.Equal(5, refused.Preview.ErrorRows);
        Assert.Empty(await NewProductsAsync());
    }

    [Fact]
    public async Task Unknown_or_missing_columns_are_refused_before_any_row()
    {
        var preview = await Transfer.PreviewAsync(ImportKind.Customers, Csv("Customer name,Phone", "X,1"), "c.csv");
        Assert.False(preview.CanImport);
        Assert.Contains(preview.FileErrors, e => e.Contains("Unknown column", StringComparison.Ordinal));
        Assert.Contains(preview.FileErrors, e => e.Contains("\"Name\" is missing", StringComparison.Ordinal));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Transfer.PreviewAsync(ImportKind.Customers, Csv("Name"), "c.pdf"));
    }

    [Fact]
    public async Task Many_customers_get_distinct_ledgers_in_one_transaction()
    {
        var lines = new List<string> { "Name,Phone,GSTIN,City,Credit limit" };
        for (var i = 1; i <= 25; i++) lines.Add($"Customer {i:00},98200{i:00000},,Pune,{i * 1000}");
        lines.Add($"Gst Customer,,{Gstin("29", "AAACG1234G")},,");

        var result = await Transfer.ImportAsync(ImportKind.Customers, Csv([.. lines]), "customers.csv");
        Assert.Equal(26, result.Created);
        var customers = await Parties.ListAsync(PartyKind.Customer);
        Assert.Equal(26 + 2, customers.Count);
        Assert.Equal(customers.Count, customers.Select(c => c.AccountId).Distinct().Count());
        Assert.Equal("29", customers.Single(c => c.Name == "Gst Customer").StateCode);
        var chart = await Db.Get<ChartOfAccountsService>().ListAsync();
        Assert.Equal(chart.Count, chart.Select(a => a.Code).Distinct().Count());
    }

    [Fact]
    public async Task Invalid_gstin_is_reported_on_its_row()
    {
        var preview = await Transfer.PreviewAsync(ImportKind.Suppliers, Csv("Name,GSTIN", "Good Supplier,", "Bad Supplier,27AAACB1234B1ZZ"), "s.csv");
        Assert.True(preview.Rows[0].IsValid);
        Assert.False(preview.Rows[1].IsValid);
    }

    [Fact]
    public async Task Opening_stock_posts_with_its_voucher_and_reconciles()
    {
        var file = Csv("Date,Warehouse,SKU,Quantity,Unit cost", "10-10-2026,MAIN,LED-18W,5,110");
        var before = await LedgerAsync(SystemAccounts.Inventory, October);

        var preview = await Transfer.PreviewAsync(ImportKind.OpeningStock, file, "stock.csv");
        Assert.True(preview.CanImport, string.Join("; ", preview.FileErrors.Concat(preview.Rows.SelectMany(r => r.Errors))));
        Assert.Equal(before, await LedgerAsync(SystemAccounts.Inventory, October));          // the dry run rolled back

        await Transfer.ImportAsync(ImportKind.OpeningStock, file, "stock.csv");
        Assert.Equal(before + 550m, await LedgerAsync(SystemAccounts.Inventory, October));
        Assert.True((await Stock.ReconcileAsync(October)).IsReconciled);
        await AssertBooksBalanceAsync();
    }

    [Fact]
    public async Task Opening_stock_rules_of_the_stock_engine_show_in_the_preview()
    {
        // Before the fixture's stock date: only the stock engine knows this rule, and the preview's dry run reports it.
        var early = await Transfer.PreviewAsync(ImportKind.OpeningStock, Csv("Date,Warehouse,SKU,Quantity,Unit cost", "01-10-2026,MAIN,LED-18W,5,110"), "s.csv");
        Assert.False(early.CanImport);
        Assert.NotEmpty(early.FileErrors);

        var bad = await Transfer.PreviewAsync(ImportKind.OpeningStock, Csv("Date,Warehouse,SKU,Quantity,Unit cost", "10-10-2026,NOWHERE,NOPE,0,"), "s.csv");
        Assert.Equal(4, bad.Rows.Single().Errors.Count);
    }

    [Fact]
    public async Task Chart_of_accounts_imports_groups_before_their_ledgers()
    {
        var file = Csv("Code,Name,Parent code,Type", "5400,Marketing,5000,Group", "5401,Online ads,5400,General", "1122,Second bank,1120,Bank", "9999,Orphan,8888,General");
        var preview = await Transfer.PreviewAsync(ImportKind.ChartOfAccounts, file, "coa.csv");
        Assert.Equal(3, preview.ValidRows);
        Assert.Contains(preview.Rows[3].Errors, e => e.Contains("8888", StringComparison.Ordinal));

        await Transfer.ImportAsync(ImportKind.ChartOfAccounts, Csv("Code,Name,Parent code,Type", "5400,Marketing,5000,Group", "5401,Online ads,5400,General"), "coa.csv");
        var chart = await Db.Get<ChartOfAccountsService>().ListAsync();
        Assert.Equal(chart.Single(a => a.Code == "5400").Id, chart.Single(a => a.Code == "5401").ParentId);
        Assert.Equal(Nexora.Domain.Accounting.AccountNature.Expense, chart.Single(a => a.Code == "5401").Nature);
    }

    [Fact]
    public async Task Exports_import_back_and_xlsx_round_trips()
    {
        var excel = await Transfer.ExportAsync(ImportKind.Products, ExportFormat.Excel);
        var csv = await Transfer.ExportAsync(ImportKind.Products, ExportFormat.Csv);
        foreach (var file in new[] { excel, csv })
        {
            var preview = await Transfer.PreviewAsync(ImportKind.Products, file.Content, file.FileName);
            Assert.Empty(preview.FileErrors);                                   // the export's headers are the import's
            Assert.Contains(preview.Rows.Single(r => r.Summary.StartsWith("LED-18W", StringComparison.Ordinal)).Errors, e => e.Contains("already exists", StringComparison.Ordinal));
        }

        var stock = await Transfer.ExportAsync(ImportKind.OpeningStock, ExportFormat.Excel);
        var data = TabularReader.Read(stock.Content, stock.FileName);
        Assert.Equal("2026-10-10", data.Rows.Single().Cells[0]);               // a real date cell reads back as a date
        Assert.Equal("20", data.Rows.Single().Cells[3]);
    }

    [Fact]
    public async Task Exported_names_cannot_run_as_formulas()
    {
        await Parties.SaveAsync(new Application.Billing.PartyInput(null, PartyKind.Customer, new PartyDetails("=cmd|'/c calc'!A1", StateCode: "27")));
        var text = Encoding.UTF8.GetString((await Transfer.ExportAsync(ImportKind.Customers, ExportFormat.Csv)).Content);
        Assert.Contains("'=cmd", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\n=cmd", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Imports_and_exports_check_permissions()
    {
        await SignInAsAsync("bina", "Billing Operator");
        await Assert.ThrowsAsync<AccessDeniedException>(() => Transfer.PreviewAsync(ImportKind.Products, Csv("SKU,Name,Unit", "A,B,PCS"), "p.csv"));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Transfer.ExportAsync(ImportKind.Customers, ExportFormat.Csv));   // no Data: export
        Assert.True((await Transfer.PreviewAsync(ImportKind.Customers, Csv("Name", "New One"), "c.csv")).CanImport);

        await SignInAsAdministratorAsync();
        await Db.Get<Application.Users.RoleService>().SaveAsync(new Application.Users.RoleInput(null, "Catalogue clerk", "Products without costs",
            [Nexora.Domain.Security.Permissions.InventoryView, Nexora.Domain.Security.Permissions.InventoryManage], 0));
        await SignInAsAsync("cara", "Catalogue clerk");
        var noCost = await Transfer.PreviewAsync(ImportKind.Products, Csv("SKU,Name,Unit,Purchase price", "Z-1,Z,PCS,10"), "p.csv");
        Assert.Contains(noCost.Rows.Single().Errors, e => e.Contains("View cost", StringComparison.Ordinal));
    }

    private async Task<List<string>> NewProductsAsync() => (await Catalogue.ListProductsAsync()).Where(p => p.Sku != "LED-18W").Select(p => p.Sku).ToList();
}
