using Nexora.Application;
using Nexora.Application.Security;
using Nexora.Application.Billing;
using Nexora.Domain.Billing;

namespace Nexora.Tests.Billing;

public sealed class WorkflowCompletionTests : BillingTestBase
{
    [Fact]
    public async Task Billing_defaults_apply_to_new_sales_but_preserve_saved_draft_choices_and_purchase_exclusive_prices()
    {
        await Billing.SaveSettingsAsync(roundToRupee: false, pricesIncludeTax: true);
        var sale = await DraftAsync(BillingDocumentKind.SalesInvoice, Local, new BillingLineInput(Led, 1));
        var purchase = await DraftAsync(BillingDocumentKind.PurchaseBill, Vendor, new BillingLineInput(Led, 1));
        Assert.True((await Billing.GetAsync(sale)).PricesIncludeTax);
        Assert.False((await Billing.GetAsync(purchase)).PricesIncludeTax);
        Assert.Equal(150, (await Billing.GetAsync(sale)).Total);
        await Billing.SaveSettingsAsync(roundToRupee: true, pricesIncludeTax: false);
        await Billing.PostAsync(sale);
        Assert.True((await Billing.GetAsync(sale)).PricesIncludeTax);
        Assert.False((await Billing.GetAsync(sale)).RoundToRupee);
        Assert.Equal(150, (await Billing.GetAsync(sale)).Total);
    }

    [Fact]
    public async Task Billing_defaults_require_company_management_and_are_company_scoped()
    {
        await Billing.SaveSettingsAsync(false, true);
        await SignInAsAsync("reader", "Read-only");
        await Assert.ThrowsAsync<AccessDeniedException>(() => Billing.SaveSettingsAsync(true, false));
        await SignInAsAdministratorAsync();
        await Db.Workspace.SwitchCompanyAsync((await Db.Workspace.GetCompaniesAsync()).Single(c => c.IsDemo).Id);
        Assert.False((await Billing.GetSettingsAsync()).PricesIncludeTax);
        Assert.True((await Billing.GetSettingsAsync()).RoundToRupee);
    }

    [Fact]
    public async Task Used_unit_codes_cannot_rewrite_historical_invoice_and_gst_units()
    {
        await SellAsync(Local, new BillingLineInput(Led, 1));
        var pcs = (await Catalogue.ListUnitsAsync()).Single(u => u.Code == "PCS");
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Catalogue.SaveUnitAsync(pcs.Id, "KGS", "Changed meaning", pcs.DecimalPlaces));
        await Catalogue.SaveUnitAsync(pcs.Id, "PCS", "Pieces", pcs.DecimalPlaces);
        Assert.Equal("PCS", (await Catalogue.ListUnitsAsync()).Single(u => u.Id == pcs.Id).Code);
    }

    [Fact]
    public async Task Alternate_unit_precision_cannot_be_reduced_after_it_is_used()
    {
        var box = await Catalogue.SaveUnitAsync(null, "BOX3", "Fractional box", 3);
        await Catalogue.AddConversionAsync(Led, box, 12);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Catalogue.SaveUnitAsync(box, "BOX3", "Boxes", 0));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Catalogue.SaveUnitAsync(box, "NEWBOX", "Boxes", 3));
    }

    [Fact]
    public async Task Editing_tax_rate_affects_new_documents_without_rewriting_posted_tax()
    {
        var (sale, _) = await SellAsync(Local, new BillingLineInput(Led, 1));
        await Catalogue.SaveTaxCategoryAsync(Gst18, "Updated configured rate", 5);
        var next = await DraftAsync(BillingDocumentKind.SalesInvoice, Local, new BillingLineInput(Led, 1));
        Assert.Equal(18, (await Billing.GetAsync(sale)).Lines.Single().TaxRatePercent);
        Assert.Equal(5, (await Billing.GetAsync(next)).Lines.Single().TaxRatePercent);
        await SignInAsAsync("reader", "Read-only");
        await Assert.ThrowsAsync<AccessDeniedException>(() => Catalogue.SaveTaxCategoryAsync(Gst18, "Denied", 9));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Catalogue.SaveUnitAsync(null, "DENIED", "Denied", 0));
    }
}
