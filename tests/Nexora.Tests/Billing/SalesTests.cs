using Nexora.Accounting;
using Nexora.Application;
using Nexora.Application.Billing;
using Nexora.Application.Inventory;
using Nexora.Domain.Billing;
using Nexora.Domain.Inventory;

namespace Nexora.Tests.Billing;

public sealed class SalesInvoiceTests : BillingTestBase
{
    [Fact]
    public async Task Credit_sale_posts_tax_receivable_cost_and_stock_in_one_document()
    {
        var (id, number) = await SellAsync(Local, new BillingLineInput(Led, 3));

        Assert.Equal("INV/2026-27/0001", number);
        var invoice = await Billing.GetAsync(id);
        // 3 × 150 = 450; 9% CGST and SGST = 40.50 each; total 531.
        Assert.Equal((450m, 40.50m, 40.50m, 0m, 531m, 300m), (invoice.Taxable, invoice.Cgst, invoice.Sgst, invoice.Igst, invoice.Total, invoice.CostOfGoods));
        Assert.Equal(BillingStatus.Posted, invoice.State);
        Assert.Equal((PaymentStatus.Unpaid, 531m), (invoice.Status, invoice.Outstanding));
        Assert.Equal(18m, Assert.Single(invoice.Lines).TaxRatePercent);

        Assert.Equal(17m, await OnHandAsync(Led));
        Assert.Equal(531m, await PartyLedgerAsync(Local));
        Assert.Equal(-450m, await LedgerAsync(SystemAccounts.Sales, October));
        Assert.Equal(-40.50m, await LedgerAsync(SystemAccounts.OutputCgst, October));
        Assert.Equal(-40.50m, await LedgerAsync(SystemAccounts.OutputSgst, October));
        Assert.Equal(300m, await LedgerAsync(SystemAccounts.CostOfGoodsSold, October));
        Assert.Equal(1_700m, await LedgerInventoryAsync(October));
        await AssertBooksBalanceAsync();
    }

    [Fact]
    public async Task Inter_state_customer_is_charged_igst()
    {
        var (id, _) = await SellAsync(Bengaluru, new BillingLineInput(Led, 2));

        var invoice = await Billing.GetAsync(id);
        Assert.Equal(("29", true, 0m, 54m), (invoice.PlaceOfSupply, invoice.IsInterState, invoice.Cgst, invoice.Igst));
        Assert.Equal(-54m, await LedgerAsync(SystemAccounts.OutputIgst, October));
    }

    [Fact]
    public async Task Cash_sale_to_a_walk_in_customer_debits_cash_and_is_paid()
    {
        var (id, _) = await CashSaleAsync(new BillingLineInput(Led, 1));

        var invoice = await Billing.GetAsync(id);
        Assert.Equal(("Walk-in", PaymentStatus.Paid, 0m), (invoice.PartyName, invoice.Status, invoice.Outstanding));
        Assert.Equal(177m, await LedgerAsync(SystemAccounts.Cash, October));
        await AssertBooksBalanceAsync();
    }

    [Fact]
    public async Task Total_is_rounded_to_rupees_through_round_off()
    {
        var odd = await TaxedProductAsync("ODD", Gst18, selling: 99.99m);
        await ReceiveAsync(odd, 5, 50);

        var (id, _) = await SellAsync(Local, new BillingLineInput(odd, 1));

        var invoice = await Billing.GetAsync(id);
        // 99.99 + 9.00 + 9.00 = 117.99 → 118.00
        Assert.Equal((0.01m, 118m), (invoice.RoundOff, invoice.Total));
        Assert.Equal(-0.01m, await LedgerAsync(SystemAccounts.RoundOff, October));
        await AssertBooksBalanceAsync();
    }

    [Fact]
    public async Task Units_discounts_and_tax_inclusive_rates_are_recalculated_by_the_service()
    {
        var box = Units["BOX"];
        await Catalogue.AddConversionAsync(Led, box, 10);
        var id = await Billing.SaveDraftAsync(new BillingDraftInput(null, BillingDocumentKind.SalesInvoice, October, Local, Main, SettlementMode.Credit,
            [new BillingLineInput(Led, 1, box, Rate: 1_500, DiscountPercent: 10)], PricesIncludeTax: true, RoundToRupee: false));
        await Billing.PostAsync(id);

        var invoice = await Billing.GetAsync(id);
        // 1 BOX = 10 PCS at 1,500 inclusive; 10% off → 1,350 inclusive of 18%.
        Assert.Equal(1_350m, invoice.Total);
        Assert.Equal(1_350m, invoice.Taxable + invoice.Tax);
        Assert.Equal(10m, await OnHandAsync(Led));
        Assert.Equal(1_000m, invoice.CostOfGoods);
    }

    [Fact]
    public async Task Drafts_touch_neither_stock_nor_ledger_and_can_be_edited_and_deleted()
    {
        var id = await DraftAsync(BillingDocumentKind.SalesInvoice, Local, new BillingLineInput(Led, 2));
        await Billing.SaveDraftAsync(new BillingDraftInput(id, BillingDocumentKind.SalesInvoice, October, Local, Main, SettlementMode.Credit,
            [new BillingLineInput(Led, 4), new BillingLineInput(Led, 1)]));

        var draft = await Billing.GetAsync(id);
        Assert.Equal((BillingStatus.Draft, (string?)null, 2, 885m), (draft.State, draft.Number, draft.Lines.Count, draft.Total));
        Assert.Equal(20m, await OnHandAsync(Led));
        Assert.Equal(0m, await PartyLedgerAsync(Local));

        await Billing.DeleteDraftAsync(id);
        Assert.Empty(await Billing.ListAsync(new BillingListFilter([BillingDocumentKind.SalesInvoice])));
    }

    [Fact]
    public async Task Posting_twice_is_refused_and_posts_once()
    {
        var (id, _) = await SellAsync(Local, new BillingLineInput(Led, 2));

        var error = await Assert.ThrowsAsync<WorkspaceValidationException>(() => Billing.PostAsync(id));

        Assert.Contains("already posted", error.Message, StringComparison.Ordinal);
        Assert.Equal(18m, await OnHandAsync(Led));
        Assert.Equal(354m, await PartyLedgerAsync(Local));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Billing.DeleteDraftAsync(id));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Billing.SaveDraftAsync(new BillingDraftInput(id, BillingDocumentKind.SalesInvoice,
            October, Local, Main, SettlementMode.Credit, [new BillingLineInput(Led, 1)])));
    }

    [Fact]
    public async Task Failed_posting_rolls_back_everything_including_the_number()
    {
        var id = await DraftAsync(BillingDocumentKind.SalesInvoice, Local, new BillingLineInput(Led, 25));

        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Billing.PostAsync(id));

        Assert.Equal(BillingStatus.Draft, (await Billing.GetAsync(id)).State);
        Assert.Equal(20m, await OnHandAsync(Led));
        Assert.Equal(0m, await PartyLedgerAsync(Local));
        Assert.Equal(0m, await LedgerAsync(SystemAccounts.Sales, October));
        Assert.Equal("INV/2026-27/0001", (await SellAsync(Local, new BillingLineInput(Led, 1))).Number);
        await AssertBooksBalanceAsync();
    }

    [Fact]
    public async Task Posted_documents_keep_the_tax_rate_they_were_posted_with()
    {
        var (id, _) = await SellAsync(Local, new BillingLineInput(Led, 1));

        await Catalogue.SaveTaxCategoryAsync(Gst18, "GST 18%", 12);
        var later = await SellAsync(Local, new BillingLineInput(Led, 1));

        Assert.Equal((18m, 177m), (Assert.Single((await Billing.GetAsync(id)).Lines).TaxRatePercent, (await Billing.GetAsync(id)).Total));
        Assert.Equal((12m, 168m), (Assert.Single((await Billing.GetAsync(later.Id)).Lines).TaxRatePercent, (await Billing.GetAsync(later.Id)).Total));
    }

    [Fact]
    public async Task Untaxed_products_post_without_tax()
    {
        var plain = await TaxedProductAsync("PLAIN", null, selling: 40);
        await ReceiveAsync(plain, 2, 10);

        var (id, _) = await SellAsync(Local, new BillingLineInput(plain, 2));

        Assert.Equal((0m, 80m), ((await Billing.GetAsync(id)).Tax, (await Billing.GetAsync(id)).Total));
    }

    [Fact]
    public async Task Serial_numbers_must_be_in_stock_to_be_sold()
    {
        var phone = await TaxedProductAsync("PHONE", Gst18, TrackingMode.Serial, selling: 10_000);
        await ReceiveAsync(phone, 1, 8_000, serial: "IMEI-1");

        await Assert.ThrowsAsync<WorkspaceValidationException>(() => SellAsync(Local, new BillingLineInput(phone, 1, SerialNumber: "IMEI-2")));
        var (id, _) = await SellAsync(Local, new BillingLineInput(phone, 1, SerialNumber: "imei-1"));

        Assert.Equal(("IMEI-1", 8_000m), (Assert.Single((await Billing.GetAsync(id)).Lines).SerialNumber, (await Billing.GetAsync(id)).CostOfGoods));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => SellAsync(Local, new BillingLineInput(phone, 1, SerialNumber: "IMEI-1")));
    }

    [Fact]
    public async Task Credit_sales_need_a_customer_and_suppliers_cannot_be_invoiced()
    {
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => DraftAsync(BillingDocumentKind.SalesInvoice, null, new BillingLineInput(Led, 1)));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => DraftAsync(BillingDocumentKind.SalesInvoice, Vendor, new BillingLineInput(Led, 1)));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => DraftAsync(BillingDocumentKind.SalesInvoice, Local));
    }

    [Fact]
    public async Task Stock_screen_cannot_reverse_an_invoices_stock()
    {
        await SellAsync(Local, new BillingLineInput(Led, 1));
        var stockDocument = (await Stock.DocumentsAsync()).Single(d => d.Type == StockDocumentType.Sale);

        var error = await Assert.ThrowsAsync<WorkspaceValidationException>(() => Stock.ReverseAsync(stockDocument.Id, October, ""));
        Assert.Contains("sales or purchase document", error.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Stock.PostAsync(new StockDocumentInput(StockDocumentType.Sale, October, Main, "", [new(Led, 1)])));
    }

    [Fact]
    public async Task List_searches_and_reports_payment_status()
    {
        await SellAsync(Local, new BillingLineInput(Led, 1));
        await SellAsync(Bengaluru, new BillingLineInput(Led, 1));
        await CashSaleAsync(new BillingLineInput(Led, 1));

        var all = await Billing.ListAsync(new BillingListFilter([BillingDocumentKind.SalesInvoice]));
        var kaveri = await Billing.ListAsync(new BillingListFilter([BillingDocumentKind.SalesInvoice], Search: "kaveri"));

        Assert.Equal(3, all.Count);
        Assert.Equal(["Paid", "Unpaid", "Unpaid"], all.Select(r => r.StatusLabel).Order());
        Assert.Equal("Kaveri Lights", Assert.Single(kaveri).PartyName);
    }
}

public sealed class FifoSalesTests : BillingTestBase
{
    protected override CostingMethod Costing => CostingMethod.Fifo;

    [Fact]
    public async Task Fifo_companies_charge_cost_of_goods_from_the_oldest_layers()
    {
        await ReceiveAsync(Led, 2, 130);                           // 20 at 100, then 2 at 130

        var (id, _) = await SellAsync(Local, new BillingLineInput(Led, 21));
        var (credit, _) = await ReturnAsync(id, (1, 21));

        Assert.Equal(2_130m, (await Billing.GetAsync(id)).CostOfGoods);
        Assert.Equal(2_130m, (await Billing.GetAsync(credit)).CostOfGoods);
        await AssertBooksBalanceAsync();
    }
}

public sealed class SalesReturnTests : BillingTestBase
{
    [Fact]
    public async Task Partial_returns_reverse_tax_cost_and_stock_and_reduce_the_outstanding()
    {
        var (invoice, _) = await SellAsync(Local, new BillingLineInput(Led, 3));

        var (credit, number) = await ReturnAsync(invoice, (1, 1));

        Assert.Equal("CRN/2026-27/0001", number);
        var note = await Billing.GetAsync(credit);
        Assert.Equal((150m, 13.50m, 13.50m, 177m, 100m), (note.Taxable, note.Cgst, note.Sgst, note.Total, note.CostOfGoods));
        Assert.Equal(18m, await OnHandAsync(Led));
        Assert.Equal(354m, (await Billing.GetAsync(invoice)).Outstanding);
        Assert.Equal(PaymentStatus.PartlyPaid, (await Billing.GetAsync(invoice)).Status);
        Assert.Equal(354m, await PartyLedgerAsync(Local));
        Assert.Equal(150m, await LedgerAsync(SystemAccounts.SalesReturns, October));
        Assert.Equal(200m, await LedgerAsync(SystemAccounts.CostOfGoodsSold, October));
        await AssertBooksBalanceAsync();
    }

    [Fact]
    public async Task Returning_everything_in_parts_reverses_the_invoice_exactly()
    {
        var odd = await TaxedProductAsync("ODD", Gst18, selling: 33.33m);
        await ReceiveAsync(odd, 3, 10);
        await Billing.SaveSettingsAsync(roundToRupee: false, pricesIncludeTax: false);
        var (invoice, _) = await SellAsync(Local, new BillingLineInput(odd, 3));

        await ReturnAsync(invoice, (1, 1));
        await ReturnAsync(invoice, (1, 2));

        Assert.Equal(0m, await PartyLedgerAsync(Local));
        Assert.Equal(PaymentStatus.Paid, (await Billing.GetAsync(invoice)).Status);
        Assert.Equal(0m, await LedgerAsync(SystemAccounts.Sales, October) + await LedgerAsync(SystemAccounts.SalesReturns, October));
        Assert.Equal(0m, await LedgerAsync(SystemAccounts.OutputCgst, October));
        Assert.Equal(0m, await LedgerAsync(SystemAccounts.CostOfGoodsSold, October));
        Assert.Equal(3m, await OnHandAsync(odd));
        await AssertBooksBalanceAsync();
    }

    [Fact]
    public async Task Returns_beyond_the_quantity_sold_are_refused()
    {
        var (invoice, _) = await SellAsync(Local, new BillingLineInput(Led, 3));
        await ReturnAsync(invoice, (1, 2));

        var error = await Assert.ThrowsAsync<WorkspaceValidationException>(() => ReturnAsync(invoice, (1, 2)));

        Assert.Contains("only 1", error.Message, StringComparison.Ordinal);
        Assert.Single(await Billing.ReturnableLinesAsync(invoice), l => l.Remaining == 1);
    }

    [Fact]
    public async Task Two_drafts_cannot_together_return_more_than_was_sold()
    {
        var (invoice, _) = await SellAsync(Local, new BillingLineInput(Led, 3));
        var full = await Billing.FullReturnDraftAsync(invoice, October);
        var first = await Billing.SaveDraftAsync(full);
        var second = await Billing.SaveDraftAsync(full);

        await Billing.PostAsync(first);

        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Billing.PostAsync(second));
        Assert.Equal(20m, await OnHandAsync(Led));
    }

    [Fact]
    public async Task Credit_beyond_the_outstanding_stays_unapplied_and_can_be_refunded()
    {
        var (invoice, _) = await SellAsync(Local, new BillingLineInput(Led, 2));
        await Settlements.PostAsync(new SettlementInput(SettlementKind.CustomerReceipt, October, Local, CashAccount, 300, PaymentMethod.Cash,
            [new AllocationInput(invoice, 300)]));

        var (credit, _) = await ReturnAsync(invoice, (1, 2));

        var party = await Parties.GetAsync(Local);
        Assert.Equal((0m, 300m, -300m), (party.Outstanding, party.Unapplied, party.LedgerBalance));
        await Settlements.PostAsync(new SettlementInput(SettlementKind.CustomerRefund, October, Local, CashAccount, 300, PaymentMethod.Upi,
            [new AllocationInput(credit, 300)], Reference: "UPI-77"));
        party = await Parties.GetAsync(Local);
        Assert.Equal((0m, 0m, 0m), (party.Outstanding, party.Unapplied, party.LedgerBalance));
        Assert.Equal(PaymentStatus.Paid, (await Billing.GetAsync(credit)).Status);
        await AssertBooksBalanceAsync();
    }

    [Fact]
    public async Task Cash_sale_returns_are_refunded_from_cash()
    {
        var (invoice, _) = await CashSaleAsync(new BillingLineInput(Led, 2));

        var (credit, _) = await ReturnAsync(invoice, (1, 1));

        Assert.Equal(SettlementMode.Cash, (await Billing.GetAsync(credit)).Mode);
        Assert.Equal(354m - 177m, await LedgerAsync(SystemAccounts.Cash, October));
        await AssertBooksBalanceAsync();
    }

    [Fact]
    public async Task Returned_serials_come_back_into_stock_once()
    {
        var phone = await TaxedProductAsync("PHONE", Gst18, TrackingMode.Serial, selling: 10_000);
        await ReceiveAsync(phone, 1, 8_000, serial: "IMEI-1");
        var (invoice, _) = await SellAsync(Local, new BillingLineInput(phone, 1, SerialNumber: "IMEI-1"));

        await ReturnAsync(invoice, (1, 1));

        Assert.Equal(["IMEI-1"], (await Stock.BalancesAsync()).Where(b => b.ProductId == phone).Select(b => b.SerialNumber));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => ReturnAsync(invoice, (1, 1)));
    }

    [Fact]
    public async Task Returns_must_refer_to_a_posted_invoice()
    {
        var draft = await DraftAsync(BillingDocumentKind.SalesInvoice, Local, new BillingLineInput(Led, 1));

        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Billing.SaveDraftAsync(new BillingDraftInput(null, BillingDocumentKind.SalesReturn,
            October, Local, Main, SettlementMode.Credit, [new BillingLineInput(Led, 1)], OriginalDocumentId: draft)));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Billing.SaveDraftAsync(new BillingDraftInput(null, BillingDocumentKind.SalesReturn,
            October, Local, Main, SettlementMode.Credit, [new BillingLineInput(Led, 1)])));
    }
}

public sealed class SalesCancellationTests : BillingTestBase
{
    [Fact]
    public async Task Cancelling_reverses_stock_and_ledger_and_keeps_the_document()
    {
        var (invoice, number) = await SellAsync(Local, new BillingLineInput(Led, 3));

        await Billing.CancelAsync(invoice, October, "Entered for the wrong customer");

        var view = await Billing.GetAsync(invoice);
        Assert.Equal((PaymentStatus.Cancelled, number, "Entered for the wrong customer"), (view.Status, view.Number, view.CancellationReason));
        Assert.Equal(20m, await OnHandAsync(Led));
        Assert.Equal(0m, await PartyLedgerAsync(Local));
        Assert.Equal(0m, await LedgerAsync(SystemAccounts.Sales, October));
        Assert.Equal(0m, await LedgerAsync(SystemAccounts.CostOfGoodsSold, October));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Billing.CancelAsync(invoice, October, "again"));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => ReturnAsync(invoice, (1, 1)));
        await AssertBooksBalanceAsync();
    }

    [Fact]
    public async Task Cancelling_is_refused_while_a_receipt_or_a_return_is_attached()
    {
        var (returned, _) = await SellAsync(Local, new BillingLineInput(Led, 2));
        await ReturnAsync(returned, (1, 1));
        var (paid, _) = await SellAsync(Local, new BillingLineInput(Led, 1));
        var (receipt, _) = await Settlements.PostAsync(new SettlementInput(SettlementKind.CustomerReceipt, October, Local, CashAccount, 100, PaymentMethod.Cash,
            [new AllocationInput(paid, 100)]));

        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Billing.CancelAsync(paid, October, "Mistake"));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Billing.CancelAsync(returned, October, "Mistake"));

        await Settlements.ReverseAsync(receipt, October, "Cheque bounced");
        await Billing.CancelAsync(paid, October, "Mistake");
        Assert.Equal(PaymentStatus.Cancelled, (await Billing.GetAsync(paid)).Status);
    }

    [Fact]
    public async Task Cancelling_needs_a_reason_and_refuses_when_later_stock_moved()
    {
        var (invoice, _) = await SellAsync(Local, new BillingLineInput(Led, 1));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Billing.CancelAsync(invoice, October, " "));

        await SellAsync(Local, new BillingLineInput(Led, 1));
        var error = await Assert.ThrowsAsync<WorkspaceValidationException>(() => Billing.CancelAsync(invoice, October, "Mistake"));
        Assert.Contains("Later stock movements", error.Message, StringComparison.Ordinal);
        Assert.Equal(PaymentStatus.Unpaid, (await Billing.GetAsync(invoice)).Status);
    }
}
