using Microsoft.Data.Sqlite;
using Nexora.Application;
using Nexora.Application.Billing;
using Nexora.Application.Inventory;
using Nexora.Application.Lookups;
using Nexora.Application.Security;
using Nexora.Domain.Billing;
using Nexora.Domain.Inventory;

namespace Nexora.Tests.Billing;

public sealed class LookupTextTests
{
    [Theory]
    [InlineData("LED-18W · White ceiling light", "white led", true)]
    [InlineData("2110-0001 · Pune Wholesale", "wholesale 0001", true)]
    [InlineData("LED-18W · White ceiling light", "  CEILING   light ", true)]
    [InlineData("LED-18W · White ceiling light", "white red", false)]
    [InlineData("LED-18W · White ceiling light", "18w", true)]
    [InlineData(null, "", true)]
    [InlineData(null, "name", false)]
    public void Every_word_matches_code_or_name_without_case_sensitivity(string? label, string query, bool expected) =>
        Assert.Equal(expected, LookupText.Matches(label, query));
}

public sealed class SimplifiedPurchaseTests : BillingTestBase
{
    private ProductInput NewItem(string code = "NEW-ITEM") => new(null,
        new ProductDetails(code, "New purchase item", HsnCode: "9405", TaxCategoryId: Gst18, PurchasePrice: 50, SellingPrice: 80, Mrp: 100),
        Units["PCS"], TrackingMode.None, false);

    [Fact]
    public async Task New_item_is_persisted_then_purchase_posts_stock_and_balanced_accounts()
    {
        var id = await Catalogue.AddPurchaseItemAsync(NewItem());
        var item = Assert.Single(await Catalogue.ListProductsAsync(), p => p.Id == id);
        Assert.Equal("NEW-ITEM · New purchase item", item.LookupLabel);
        Assert.Equal(0m, item.OnHand);
        await BuyAsync("NEW/1", new BillingLineInput(id, 3));
        Assert.Equal(3m, await OnHandAsync(id));
        Assert.Equal(177m, await PartyLedgerAsync(Vendor));
        await AssertBooksBalanceAsync();
    }

    [Fact]
    public async Task Existing_code_is_reused_case_insensitively_without_overwriting_details()
    {
        var before = (await Catalogue.ListProductsAsync()).Single(p => p.Id == Led);
        var id = await Catalogue.AddPurchaseItemAsync(NewItem("  led-18w  "));
        Assert.Equal(Led, id);
        Assert.Equal(before, (await Catalogue.ListProductsAsync()).Single(p => p.Id == Led));
        Assert.Single(await Catalogue.ListProductsAsync(), p => p.Sku == "LED-18W");
    }

    [Fact]
    public async Task Concurrent_item_additions_reuse_one_code()
    {
        var ids = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Catalogue.AddPurchaseItemAsync(NewItem())));
        Assert.Single(ids.Distinct());
        Assert.Single(await Catalogue.ListProductsAsync(), p => p.Sku == "NEW-ITEM");
    }

    [Fact]
    public async Task Archived_code_cannot_silently_create_a_second_item()
    {
        var id = await Catalogue.AddPurchaseItemAsync(NewItem());
        await Catalogue.SetArchivedAsync(id, true);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Catalogue.AddPurchaseItemAsync(NewItem()));
        Assert.Single(await Catalogue.ListProductsAsync(true), p => p.Sku == "NEW-ITEM");
    }

    [Fact]
    public async Task Read_only_and_sales_operators_cannot_add_items_through_purchase_entry()
    {
        var input = NewItem();
        await SignInAsAsync("reader", "Read-only");
        await Assert.ThrowsAsync<AccessDeniedException>(() => Catalogue.AddPurchaseItemAsync(input));
        await SignInAsAdministratorAsync();
        await SignInAsAsync("seller", "Billing Operator");
        await Assert.ThrowsAsync<AccessDeniedException>(() => Catalogue.AddPurchaseItemAsync(input));
    }

    [Fact]
    public async Task Invalid_unit_or_another_companys_unit_never_leaves_a_new_item()
    {
        var input = NewItem();
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Catalogue.AddPurchaseItemAsync(input with { UnitId = Guid.NewGuid() }));
        Assert.DoesNotContain(await Catalogue.ListProductsAsync(), p => p.Sku == "NEW-ITEM");
        var demo = (await Db.Workspace.GetCompaniesAsync()).Single(c => c.IsDemo);
        await Db.Workspace.SwitchCompanyAsync(demo.Id);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Catalogue.AddPurchaseItemAsync(input));
        Assert.DoesNotContain(await Catalogue.ListProductsAsync(), p => p.Sku == "NEW-ITEM");
    }

    [Fact]
    public async Task Supplier_numbers_are_distinct_and_preserved_on_rename()
    {
        var first = await Parties.GetAsync(Vendor);
        var secondId = await Parties.SaveAsync(new PartyInput(null, PartyKind.Supplier, new PartyDetails("Second supplier", Phone: "9876543210", Address: "Market Road")));
        var second = await Parties.GetAsync(secondId);
        Assert.NotEmpty(first.Number);
        Assert.NotEqual(first.Number, second.Number);
        await Parties.SaveAsync(new PartyInput(Vendor, PartyKind.Supplier, new PartyDetails("Renamed supplier")));
        var renamed = await Parties.GetAsync(Vendor);
        Assert.Equal(first.Number, renamed.Number);
        Assert.Equal(first.AccountId, renamed.AccountId);
        Assert.Equal(renamed.Number, (await Parties.ListAsync(PartyKind.Supplier)).Single(p => p.Id == Vendor).Number);
        Assert.Contains("Renamed supplier", renamed.LookupLabel, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Supplier_journals_include_balanced_counterparts_and_exclude_other_parties()
    {
        var (bill, _) = await BuyAsync("J/1", new BillingLineInput(Led, 2, Rate: 100));
        await SellAsync(Local, new BillingLineInput(Led, 1));
        await Settlements.PostAsync(new SettlementInput(SettlementKind.SupplierPayment, October, Vendor, CashAccount, 100,
            PaymentMethod.Cash, [new AllocationInput(bill, 100)]));
        var journal = await Parties.JournalsAsync(Vendor, October, October);
        Assert.Equal(2, journal.Select(l => l.Voucher).Distinct().Count());
        Assert.All(journal.GroupBy(l => l.Voucher), g => Assert.Equal(g.Sum(l => l.Debit), g.Sum(l => l.Credit)));
        Assert.DoesNotContain(journal, l => l.Type == "Sales");
        var statement = await Parties.StatementAsync(Vendor, October, October);
        Assert.Equal(136m, statement.Closing);
        Assert.Equal(await PartyLedgerAsync(Vendor), statement.Closing);
        Assert.Empty(await Parties.JournalsAsync(Vendor, October.AddDays(1), October.AddDays(2)));
        await AssertBooksBalanceAsync();
    }

    [Fact]
    public async Task Full_journals_require_accounting_permission_and_company_access()
    {
        await BuyAsync("J/2", new BillingLineInput(Led, 1));
        await SignInAsAsync("buyer", "Inventory Manager");
        Assert.NotEmpty((await Parties.StatementAsync(Vendor, October, October)).Lines);
        await Assert.ThrowsAsync<AccessDeniedException>(() => Parties.JournalsAsync(Vendor, October, October));
        await SignInAsAdministratorAsync();
        var demo = (await Db.Workspace.GetCompaniesAsync()).Single(c => c.IsDemo);
        await Db.Workspace.SwitchCompanyAsync(demo.Id);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Parties.JournalsAsync(Vendor, October, October));
    }

    [Fact]
    public async Task Purchase_dates_and_order_reference_survive_edit_post_and_database_immutability()
    {
        var input = new BillingDraftInput(null, BillingDocumentKind.PurchaseBill, October, Vendor, Main, SettlementMode.Credit,
            [new BillingLineInput(Led, 2)], Reference: "SUP/778", SupplierInvoiceDate: October.AddDays(-2), ReceivedDate: October,
            PurchaseOrderNumber: "PO/12", PurchaseOrderYear: "2026-27");
        var id = await Billing.SaveDraftAsync(input);
        await Billing.SaveDraftAsync(input with { Id = id, PurchaseOrderNumber = "PO/13" });
        await Billing.PostAsync(id);
        var saved = await Billing.GetAsync(id);
        Assert.Equal(input.SupplierInvoiceDate, saved.SupplierInvoiceDate);
        Assert.Equal(input.ReceivedDate, saved.ReceivedDate);
        Assert.Equal("PO/13", saved.PurchaseOrderNumber);
        Assert.Equal("2026-27", saved.PurchaseOrderYear);
        Assert.Equal("SUP/778", saved.Reference);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Billing.SaveDraftAsync(input with { Id = id }));
        using var connection = Db.OpenRawConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE BillingDocuments SET PurchaseOrderNumber = 'tampered' WHERE Status = 'Posted'";
        Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
        var returned = await ReturnAsync(id, (1, 1));
        Assert.Null((await Billing.GetAsync(returned.Id)).SupplierInvoiceDate);
        await AssertBooksBalanceAsync();
    }

    [Fact]
    public async Task Purchase_details_are_rejected_on_sales_and_overlong_order_references()
    {
        var input = new BillingDraftInput(null, BillingDocumentKind.SalesInvoice, October, Local, Main, SettlementMode.Credit,
            [new BillingLineInput(Led, 1)], SupplierInvoiceDate: October);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Billing.SaveDraftAsync(input));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Billing.SaveDraftAsync(input with
        {
            Kind = BillingDocumentKind.PurchaseBill, PartyId = Vendor, PurchaseOrderNumber = new string('x', 41),
        }));
        Assert.Empty(await Billing.ListAsync(new BillingListFilter([BillingDocumentKind.PurchaseBill, BillingDocumentKind.SalesInvoice])));
    }
}
