using Nexora.Application;
using Nexora.Application.Billing;
using Nexora.Domain.Billing;

namespace Nexora.Tests.Billing;

public sealed class BillingRegressionTests : BillingTestBase
{
    [Theory]
    [InlineData(BillingDocumentKind.SalesInvoice)]
    [InlineData(BillingDocumentKind.PurchaseBill)]
    public async Task Returns_preserve_original_rounding_after_settings_change(BillingDocumentKind kind)
    {
        await Billing.SaveSettingsAsync(false, false);
        var original = await Billing.SaveDraftAsync(new BillingDraftInput(null, kind, October,
            kind == BillingDocumentKind.SalesInvoice ? Local : Vendor, Main, SettlementMode.Credit,
            [new BillingLineInput(Led, 1, Rate: 99.99m)]));
        await Billing.PostAsync(original);
        await Billing.SaveSettingsAsync(true, false);

        var (returned, _) = await ReturnAsync(original, (1, 1));

        var credit = await Billing.GetAsync(returned);
        Assert.False(credit.RoundToRupee);
        Assert.Equal((await Billing.GetAsync(original)).Total, credit.Total);
        Assert.Equal(0m, await PartyLedgerAsync(kind == BillingDocumentKind.SalesInvoice ? Local : Vendor));
        await AssertBooksBalanceAsync();
    }

    [Fact]
    public async Task Many_small_returns_never_refund_more_than_the_original()
    {
        var product = await TaxedProductAsync("SMALL", null, selling: 0.60m);
        await ReceiveAsync(product, 4, 0.01m);
        var (original, _) = await SellAsync(Local, new BillingLineInput(product, 4));
        decimal total = 0;
        for (var i = 0; i < 4; i++)
        {
            var (returned, _) = await ReturnAsync(original, (1, 1));
            var credit = await Billing.GetAsync(returned);
            Assert.True(credit.Total >= 0);
            total += credit.Total;
            Assert.True(total <= (await Billing.GetAsync(original)).Total);
        }
        Assert.Equal((await Billing.GetAsync(original)).Total, total);
        Assert.Equal(0m, await PartyLedgerAsync(Local));
        await AssertBooksBalanceAsync();
    }

    [Fact]
    public async Task Invented_issued_part_source_cannot_bypass_stock_posting()
    {
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => DraftAsync(BillingDocumentKind.SalesInvoice, Local,
            new BillingLineInput(Led, 1, LineType: BillingLineType.IssuedPart, SourceId: Guid.NewGuid())));
        Assert.Equal(20m, await OnHandAsync(Led));
        Assert.Equal(0m, await PartyLedgerAsync(Local));
    }

    [Fact]
    public async Task Unknown_line_type_cannot_bypass_stock_posting()
    {
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => DraftAsync(BillingDocumentKind.SalesInvoice, Local,
            new BillingLineInput(Led, 1, LineType: (BillingLineType)99)));
    }
}
