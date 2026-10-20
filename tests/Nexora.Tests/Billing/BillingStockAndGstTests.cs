using System.Text.Json;
using Nexora.Application;
using Nexora.Application.Billing;
using Nexora.Application.Companies;
using Nexora.Application.Security;
using Nexora.Application.Users;
using Nexora.Domain.Billing;
using Nexora.Domain.Companies;
using Nexora.Domain.Security;
using Nexora.Reporting.Gst;
using Nexora.Reporting.Reports;

namespace Nexora.Tests.Billing;

public sealed class BillingStockAndGstTests : BillingTestBase
{
    private static readonly DateOnly From = new(2026, 10, 1), To = new(2026, 10, 31);
    private async Task RegisterCompanyAsync()
    {
        var service = Db.Get<CompanyService>();
        var details = await service.GetDetailsAsync();
        await service.UpdateProfileAsync(details.Profile with { Gstin = Gstin("27", "AAPFU0939F"), Pan = "AAPFU0939F" });
    }

    [Fact]
    public async Task Billing_choices_show_only_posted_purchases_in_the_chosen_warehouse_and_update_after_returns()
    {
        var draft = await DraftAsync(BillingDocumentKind.PurchaseBill, Vendor, new BillingLineInput(Led, 5, Rate: 100));
        Assert.Equal(20, (await Catalogue.ListBillingProductsAsync(Main)).Single(p => p.Id == Led).WarehouseStock);
        await Billing.PostAsync(draft);
        Assert.Equal(25, (await Catalogue.ListBillingProductsAsync(Main)).Single(p => p.Id == Led).WarehouseStock);
        Assert.Equal(0, (await Catalogue.ListBillingProductsAsync(Second)).Single(p => p.Id == Led).WarehouseStock);
        await ReturnAsync(draft, (1, 2));
        Assert.Equal(23, (await Catalogue.ListBillingProductsAsync(Main)).Single(p => p.Id == Led).WarehouseStock);
        await SellAsync(Local, new BillingLineInput(Led, 3));
        Assert.Equal(20, (await Catalogue.ListBillingProductsAsync(Main)).Single(p => p.Id == Led).WarehouseStock);
        await AssertBooksBalanceAsync();
    }

    [Fact]
    public async Task Billing_stock_choices_are_company_isolated_and_do_not_reveal_cost_to_billing_operators()
    {
        await SignInAsAsync("operator", "Billing Operator");
        var row = (await Catalogue.ListBillingProductsAsync(Main)).Single(p => p.Id == Led);
        Assert.Null(row.PurchasePrice);
        Assert.Equal(20, row.WarehouseStock);
        await SignInAsAdministratorAsync();
        await Db.Workspace.SwitchCompanyAsync((await Db.Workspace.GetCompaniesAsync()).Single(c => c.IsDemo).Id);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Catalogue.ListBillingProductsAsync(Main));
        Assert.DoesNotContain(await Catalogue.ListProductsAsync(), p => p.Id == Led);
    }

    [Fact]
    public async Task Walk_in_interstate_sale_keeps_place_of_supply_when_posted_and_returned()
    {
        var id = await Billing.SaveDraftAsync(new BillingDraftInput(null, BillingDocumentKind.SalesInvoice, October, null, Main,
            SettlementMode.Cash, [new(Led, 2)], CashAccountId: CashAccount, PlaceOfSupply: "29"));
        await Billing.PostAsync(id);
        var sale = await Billing.GetAsync(id);
        Assert.Equal("29", sale.PlaceOfSupply);
        Assert.True(sale.IsInterState);
        Assert.Equal(54, sale.Igst);
        Assert.Equal(0, sale.Cgst);
        var (returned, _) = await ReturnAsync(id, (1, 1));
        Assert.Equal("29", (await Billing.GetAsync(returned)).PlaceOfSupply);
        await AssertBooksBalanceAsync();
    }

    [Fact]
    public async Task Invalid_place_of_supply_is_rejected_and_returns_keep_original_registration_snapshot()
    {
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Billing.SaveDraftAsync(new BillingDraftInput(null,
            BillingDocumentKind.SalesInvoice, October, Local, Main, SettlementMode.Credit, [new(Led, 1)], PlaceOfSupply: "XX")));
        var (id, _) = await SellAsync(Bengaluru, new BillingLineInput(Led, 2));
        var before = await Billing.GetAsync(id);
        await Parties.SaveAsync(new PartyInput(Bengaluru, PartyKind.Customer, new PartyDetails("Renamed", StateCode: "27")));
        var (credit, _) = await ReturnAsync(id, (1, 1));
        var after = await Billing.GetAsync(credit);
        Assert.Equal(before.PartyGstin, after.PartyGstin);
        Assert.Equal(before.PartyName, after.PartyName);
        Assert.Equal(before.PlaceOfSupply, after.PlaceOfSupply);
    }

    [Fact]
    public async Task Gst_export_uses_posted_snapshots_and_reconciles_with_the_sales_register()
    {
        await RegisterCompanyAsync();
        var (b2b, _) = await SellAsync(Bengaluru, new BillingLineInput(Led, 2));
        await CashSaleAsync(new BillingLineInput(Led, 3));
        await ReturnAsync(b2b, (1, 1));
        await Parties.SaveAsync(new PartyInput(Bengaluru, PartyKind.Customer, new PartyDetails("Changed", StateCode: "27")));
        using var json = JsonDocument.Parse((await Db.Get<Gstr1ExportService>().ExportAsync(From, To)).Content);
        var root = json.RootElement;
        Assert.Equal("102026", root.GetProperty("fp").GetString());
        Assert.Equal(Gstin("29", "AAACK1234A"), root.GetProperty("b2b")[0].GetProperty("ctin").GetString());
        Assert.Single(root.GetProperty("cdnr").EnumerateArray());
        var hsn = root.GetProperty("hsn");
        var taxable = hsn.GetProperty("hsn_b2b").EnumerateArray().Concat(hsn.GetProperty("hsn_b2c").EnumerateArray()).Sum(r => r.GetProperty("txval").GetDecimal());
        var register = await Db.Get<ReportService>().RunAsync("sales.register", new ReportRequest { From = From, To = To, PageSize = 0 });
        var taxIndex = register.Columns.ToList().FindIndex(c => c.Key == "taxable");
        Assert.Equal((decimal)register.Totals![taxIndex]!, taxable);
        Assert.Contains(register.Rows, r => Equals(r.Cells[^1], "B2B"));
        Assert.Contains(register.Rows, r => Equals(r.Cells[^1], "B2C"));
    }

    [Fact]
    public async Task Gst_export_requires_permissions_and_does_not_cross_companies()
    {
        await RegisterCompanyAsync();
        await SellAsync(Bengaluru, new BillingLineInput(Led, 1));
        await SignInAsAsync("reader", "Read-only");
        await Assert.ThrowsAsync<AccessDeniedException>(() => Db.Get<Gstr1ExportService>().ExportAsync(From, To));
        await SignInAsAdministratorAsync();
        await Db.Workspace.SwitchCompanyAsync((await Db.Workspace.GetCompaniesAsync()).Single(c => c.IsDemo).Id);
        await RegisterCompanyAsync();
        var error = await Assert.ThrowsAsync<WorkspaceValidationException>(() => Db.Get<Gstr1ExportService>().ExportAsync(From, To));
        Assert.Contains("No posted sales", error.Message);
    }

    [Theory]
    [InlineData(Permissions.ReportsView)]
    [InlineData(Permissions.SalesView)]
    [InlineData(Permissions.DataExport)]
    public async Task Each_export_permission_is_required_independently(string missing)
    {
        await RegisterCompanyAsync();
        await SellAsync(Local, new BillingLineInput(Led, 1));
        var permissions = new[] { Permissions.ReportsView, Permissions.SalesView, Permissions.DataExport };
        await Db.Get<RoleService>().SaveAsync(new RoleInput(null, "Limited GST", "",
            permissions.Where(p => p != missing).ToArray(), 0));
        await SignInAsAsync("limited", "Limited GST");

        await Assert.ThrowsAsync<AccessDeniedException>(() => Db.Get<Gstr1ExportService>().ExportAsync(From, To));
    }

    [Fact]
    public async Task Same_period_cancellation_is_counted_in_documents_but_not_in_tax()
    {
        await RegisterCompanyAsync();
        var (sale, _) = await SellAsync(Local, new BillingLineInput(Led, 1));
        await Billing.CancelAsync(sale, October, "Wrong entry");
        using var json = JsonDocument.Parse((await Db.Get<Gstr1ExportService>().ExportAsync(From, To)).Content);
        Assert.False(json.RootElement.TryGetProperty("b2cs", out _));
        var count = json.RootElement.GetProperty("doc_issue").GetProperty("doc_det")[0].GetProperty("docs")[0];
        Assert.Equal(1, count.GetProperty("cancel").GetInt32());
        Assert.Equal(0, count.GetProperty("net_issue").GetInt32());
    }

    [Fact]
    public async Task Later_cancellation_preserves_the_original_period_but_requires_amendment_review_in_its_own_period()
    {
        await RegisterCompanyAsync();
        var (sale, _) = await SellAsync(Local, new BillingLineInput(Led, 1));
        var november = new DateOnly(2026, 11, 1);
        await Billing.CancelAsync(sale, november, "Wrong entry discovered later");

        using var october = JsonDocument.Parse((await Db.Get<Gstr1ExportService>().ExportAsync(From, To)).Content);
        Assert.Equal(150, october.RootElement.GetProperty("b2cs")[0].GetProperty("txval").GetDecimal());
        Assert.Equal(0, october.RootElement.GetProperty("doc_issue").GetProperty("doc_det")[0]
            .GetProperty("docs")[0].GetProperty("cancel").GetInt32());
        var error = await Assert.ThrowsAsync<WorkspaceValidationException>(() =>
            Db.Get<Gstr1ExportService>().ExportAsync(november, november.AddMonths(1).AddDays(-1)));
        Assert.Contains("prior-period", error.Message);
    }

    [Fact]
    public async Task Export_excludes_drafts_and_purchases_and_keeps_the_posted_product_snapshot()
    {
        await RegisterCompanyAsync();
        await SellAsync(Local, new BillingLineInput(Led, 1));
        await DraftAsync(BillingDocumentKind.SalesInvoice, Bengaluru, new BillingLineInput(Led, 5));
        await BuyAsync("SUP-1", new BillingLineInput(Led, 2));
        var product = (await Catalogue.ListProductsAsync()).Single(p => p.Id == Led);
        await Catalogue.SaveProductAsync(new Nexora.Application.Inventory.ProductInput(Led,
            new Nexora.Domain.Inventory.ProductDetails(product.Sku, product.Name, HsnCode: "123456",
                SellingPrice: 999, PurchasePrice: 100), product.UnitId, product.Tracking, product.TracksExpiry));

        using var json = JsonDocument.Parse((await Db.Get<Gstr1ExportService>().ExportAsync(From, To)).Content);
        var root = json.RootElement;
        Assert.False(root.TryGetProperty("b2b", out _));
        Assert.Equal(150, root.GetProperty("b2cs")[0].GetProperty("txval").GetDecimal());
        Assert.Equal("9405", root.GetProperty("hsn").GetProperty("hsn_b2c")[0].GetProperty("hsn_sc").GetString());
        Assert.Single(root.GetProperty("doc_issue").GetProperty("doc_det")[0].GetProperty("docs").EnumerateArray());
    }
}

public sealed class Gstr1FormatTests
{
    private static readonly DateOnly From = new(2026, 10, 1), To = new(2026, 10, 31);
    private static string Gstin => BillingTestBase.Gstin("27", "AAPFU0939F");
    private static GstInvoice Invoice(string number = "INV/26/1", decimal total = 118m, bool inter = false) =>
        new(number, From, "", inter ? "29" : "27", inter, total, false, "B2CS", false,
            [new("9405", "PCS", 1, 18, total / 1.18m, inter ? 0 : total / 1.18m * .09m, inter ? 0 : total / 1.18m * .09m, inter ? total / 1.18m * .18m : 0)]);

    [Theory]
    [InlineData(2024, 7, 250000, "B2CS")]
    [InlineData(2024, 7, 250000.01, "B2CL")]
    [InlineData(2024, 8, 100000, "B2CS")]
    [InlineData(2024, 8, 100000.01, "B2CL")]
    public void B2cl_threshold_uses_invoice_value_and_effective_date(int year, int month, decimal value, string expected) =>
        Assert.Equal(expected, GstSupply.Classify("", true, value, new DateOnly(year, month, 1)));

    [Fact]
    public void Intrastate_consumer_is_small_and_registered_is_b2b_at_any_value()
    {
        Assert.Equal("B2CS", GstSupply.Classify("", false, 900000, From));
        Assert.Equal("B2B", GstSupply.Classify(Gstin, true, 1, From));
    }

    [Fact]
    public void B2cl_credit_note_uses_original_invoice_category_even_when_note_value_is_small()
    {
        var sale = Invoice(total: 118000, inter: true);
        var note = Invoice("CRN/26/1", inter: true) with { IsCreditNote = true, OriginalCategory = "B2CL" };
        using var json = JsonDocument.Parse(Gstr1ExportService.Build(Gstin, From, To, [sale, note]).Content);
        Assert.Single(json.RootElement.GetProperty("b2cl").EnumerateArray());
        Assert.Single(json.RootElement.GetProperty("cdnur").EnumerateArray());
        Assert.False(json.RootElement.TryGetProperty("b2cs", out _));
        Assert.Equal(99900, json.RootElement.GetProperty("hsn").GetProperty("hsn_b2c")[0].GetProperty("txval").GetDecimal());
    }

    [Fact]
    public void B2cs_returns_are_netted_by_state_and_rate_without_fake_credit_note_sections()
    {
        var sale = Invoice(total: 236);
        var note = Invoice("CRN/26/1") with { IsCreditNote = true };
        using var json = JsonDocument.Parse(Gstr1ExportService.Build(Gstin, From, To, [sale, note]).Content);
        var small = json.RootElement.GetProperty("b2cs")[0];
        Assert.Equal(100, small.GetProperty("txval").GetDecimal());
        Assert.Equal(9, small.GetProperty("camt").GetDecimal());
        Assert.Equal(9, small.GetProperty("samt").GetDecimal());
        Assert.False(json.RootElement.TryGetProperty("cdnur", out _));
    }

    [Fact]
    public void Upload_envelope_and_service_hsn_follow_the_offline_tool_format()
    {
        var sale = Invoice();
        sale = sale with { Lines = [sale.Lines[0] with { Hsn = "998313", Unit = "NA" }] };
        using var json = JsonDocument.Parse(Gstr1ExportService.Build(Gstin, From, To, [sale], sixDigitHsn: true).Content);
        var root = json.RootElement;
        Assert.Equal("GST3.2.4", root.GetProperty("version").GetString());
        Assert.Equal("hash", root.GetProperty("hash").GetString());
        var hsn = root.GetProperty("hsn").GetProperty("hsn_b2c")[0];
        Assert.Equal("NA", hsn.GetProperty("uqc").GetString());
        Assert.Equal(0, hsn.GetProperty("qty").GetDecimal());
        Assert.Equal(100, hsn.GetProperty("txval").GetDecimal());
    }

    [Fact]
    public void Historical_hsn_uses_the_single_table_before_may_2025()
    {
        var from = new DateOnly(2025, 4, 1);
        using var json = JsonDocument.Parse(Gstr1ExportService.Build(Gstin, from, from.AddMonths(1).AddDays(-1),
            [Invoice() with { Date = from }]).Content);
        Assert.Single(json.RootElement.GetProperty("hsn").GetProperty("data").EnumerateArray());
        Assert.False(json.RootElement.GetProperty("hsn").TryGetProperty("hsn_b2c", out _));
    }

    [Fact]
    public void Invalid_registration_duplicate_numbers_and_mismatched_totals_are_rejected()
    {
        Assert.Throws<WorkspaceValidationException>(() => Gstr1ExportService.Build("INVALID", From, To, [Invoice()]));
        Assert.Throws<WorkspaceValidationException>(() => Gstr1ExportService.Build(Gstin, From, To, [Invoice() with { Gstin = Gstin }]));
        Assert.Throws<WorkspaceValidationException>(() => Gstr1ExportService.Build(Gstin, From, To, [Invoice(), Invoice("inv/26/1")]));
        Assert.Throws<WorkspaceValidationException>(() => Gstr1ExportService.Build(Gstin, From, To, [Invoice() with { Total = 120 }]));
    }

    [Fact]
    public void Tax_split_must_match_both_the_supplier_state_and_the_line_amounts()
    {
        var sale = Invoice();
        Assert.Throws<WorkspaceValidationException>(() => Gstr1ExportService.Build(Gstin, From, To,
            [sale with { PlaceOfSupply = "29" }]));
        Assert.Throws<WorkspaceValidationException>(() => Gstr1ExportService.Build(Gstin, From, To,
            [sale with { Lines = [sale.Lines[0] with { Cgst = 0, Sgst = 0, Igst = 18 }] }]));
    }

    [Theory]
    [InlineData("", "PCS", 18, false)]
    [InlineData("9405", "UNMAPPED", 18, false)]
    [InlineData("9405", "PCS", 0, false)]
    [InlineData("9405", "PCS", 18, true)]
    public void Missing_or_unsupported_tax_details_prevent_export(string hsn, string unit, decimal rate, bool sixDigits)
    {
        var sale = Invoice();
        sale = sale with { Lines = [sale.Lines[0] with { Hsn = hsn, Unit = unit, Rate = rate }] };
        Assert.Throws<WorkspaceValidationException>(() => Gstr1ExportService.Build(Gstin, From, To, [sale], sixDigits));
    }

    [Fact]
    public void Period_must_be_a_complete_month_or_quarter_and_document_number_must_fit_gst()
    {
        Assert.Throws<WorkspaceValidationException>(() => Gstr1ExportService.Build(Gstin, From, From.AddDays(2), [Invoice()]));
        Assert.Throws<WorkspaceValidationException>(() => Gstr1ExportService.Build(Gstin, From, To, [Invoice("TOO-LONG-INVOICE-NUMBER")]));
        Gstr1ExportService.ValidatePeriod(new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30));
        Assert.Throws<WorkspaceValidationException>(() => Gstr1ExportService.ValidatePeriod(new DateOnly(2026, 8, 1), new DateOnly(2026, 10, 31)));
    }
}
