using Nexora.Accounting;
using Nexora.Application;
using Nexora.Application.Billing;
using Nexora.Application.Inventory;
using Nexora.Domain.Billing;
using Nexora.Domain.Inventory;

namespace Nexora.Tests.Billing;

public sealed class PurchaseBillTests : BillingTestBase
{
    [Fact]
    public async Task Bill_receives_stock_at_its_taxable_value_and_claims_input_tax()
    {
        var (id, number) = await BuyAsync("PW/881", new BillingLineInput(Led, 10, Rate: 110, DiscountPercent: 10));

        Assert.Equal("PB/2026-27/0001", number);
        var bill = await Billing.GetAsync(id);
        // 10 × 110 = 1,100 less 10% = 990; 9% + 9% = 89.10 each; total 1,168.20 → 1,168.
        Assert.Equal((990m, 89.10m, 89.10m, -0.20m, 1_168m, 990m), (bill.Taxable, bill.Cgst, bill.Sgst, bill.RoundOff, bill.Total, bill.CostOfGoods));
        Assert.Equal(30m, await OnHandAsync(Led));
        Assert.Equal(1_168m, await PartyLedgerAsync(Vendor));
        Assert.Equal(89.10m, await LedgerAsync(SystemAccounts.InputCgst, October));
        Assert.Equal(-0.20m, await LedgerAsync(SystemAccounts.RoundOff, October));
        Assert.Equal(2_000m + 990m, await LedgerInventoryAsync(October));
        Assert.Equal(-99.67m, (await IssueAsync(Led, 1)).ValueChange);   // average (2,000 + 990) / 30 = 99.666… → 99.67
        await AssertBooksBalanceAsync();
    }

    [Fact]
    public async Task Bills_create_batches_with_expiry()
    {
        var syrup = await Catalogue.SaveProductAsync(new ProductInput(null, new ProductDetails("SYRUP", "Syrup", TaxCategoryId: Gst18, PurchasePrice: 40, SellingPrice: 60),
            Units["PCS"], TrackingMode.Batch, true));

        var (id, _) = await BuyAsync("PW/9", new BillingLineInput(syrup, 5, BatchNumber: "b-77", ExpiresOn: new DateOnly(2027, 3, 31)));

        var line = Assert.Single((await Billing.GetAsync(id)).Lines);
        Assert.Equal(("B-77", new DateOnly(2027, 3, 31)), (line.Batch, line.ExpiresOn));
        Assert.NotNull(line.BatchId);
        var (sale, _) = await SellAsync(Local, new BillingLineInput(syrup, 2, BatchId: line.BatchId));
        Assert.Equal(80m, (await Billing.GetAsync(sale)).CostOfGoods);
    }

    [Fact]
    public async Task The_same_supplier_bill_cannot_be_recorded_twice()
    {
        var (first, _) = await BuyAsync("PW/881", new BillingLineInput(Led, 1));

        var error = await Assert.ThrowsAsync<WorkspaceValidationException>(() => BuyAsync("PW/881", new BillingLineInput(Led, 1)));

        Assert.Contains("already recorded", error.Message, StringComparison.Ordinal);
        await Billing.CancelAsync(first, October, "Duplicate entry");
        await BuyAsync("PW/881", new BillingLineInput(Led, 1));
    }

    [Fact]
    public async Task Debit_note_returns_goods_at_book_cost_and_reduces_the_payable()
    {
        var (bill, _) = await BuyAsync("PW/12", new BillingLineInput(Led, 10, Rate: 130));   // pool: 20 at 100 + 10 at 130 → average 110

        var (note, number) = await ReturnAsync(bill, (1, 2));

        Assert.Equal("DBN/2026-27/0001", number);
        var view = await Billing.GetAsync(note);
        Assert.Equal((260m, 23.40m, 307m, 220m), (view.Taxable, view.Cgst, view.Total, view.CostOfGoods));
        Assert.Equal(28m, await OnHandAsync(Led));
        Assert.Equal(1_534m - 307m, await PartyLedgerAsync(Vendor));
        Assert.Equal(1_227m, (await Billing.GetAsync(bill)).Outstanding);
        // 260 charged back for goods carried at 220: the 40 difference is credited to Inventory Adjustments.
        Assert.Equal(-40m, await LedgerAsync(SystemAccounts.InventoryAdjustment, October));
        await AssertBooksBalanceAsync();
    }

    [Fact]
    public async Task Debit_notes_cannot_return_more_than_the_bill_or_stock_on_hand()
    {
        var (bill, _) = await BuyAsync("PW/1", new BillingLineInput(Led, 2));

        await Assert.ThrowsAsync<WorkspaceValidationException>(() => ReturnAsync(bill, (1, 3)));
        await SellAsync(Local, new BillingLineInput(Led, 22));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => ReturnAsync(bill, (1, 1)));
    }

    [Fact]
    public async Task Cash_purchase_credits_the_cash_account()
    {
        var id = await Billing.SaveDraftAsync(new BillingDraftInput(null, BillingDocumentKind.PurchaseBill, October, Vendor, Main, SettlementMode.Cash,
            [new BillingLineInput(Led, 1, Rate: 100)], CashAccountId: CashAccount, Reference: "CASH-1"));
        await Billing.PostAsync(id);

        Assert.Equal(-118m, await LedgerAsync(SystemAccounts.Cash, October));
        Assert.Equal(0m, await PartyLedgerAsync(Vendor));
        Assert.Equal(PaymentStatus.Paid, (await Billing.GetAsync(id)).Status);
    }

    [Fact]
    public async Task Bill_receipt_settles_negative_stock_through_the_inventory_engine()
    {
        await Catalogue.SaveSettingsAsync(CostingMethod.WeightedAverage, allowNegativeStock: true);
        await SellAsync(Local, new BillingLineInput(Led, 22));       // 2 short, issued at the last cost 100

        await BuyAsync("PW/2", new BillingLineInput(Led, 2, Rate: 120));

        Assert.Equal(0m, await OnHandAsync(Led));
        Assert.Equal(0m, await LedgerInventoryAsync(October));
        Assert.Equal(40m, await LedgerAsync(SystemAccounts.InventoryAdjustment, October));
        await AssertBooksBalanceAsync();
    }
}

public sealed class SettlementTests : BillingTestBase
{
    [Fact]
    public async Task One_receipt_settles_several_invoices_partly_and_fully()
    {
        var (a, _) = await SellAsync(Local, new BillingLineInput(Led, 1));      // 177
        var (b, _) = await SellAsync(Local, new BillingLineInput(Led, 2));      // 354

        var (receipt, number) = await Settlements.PostAsync(new SettlementInput(SettlementKind.CustomerReceipt, October, Local, CashAccount, 300,
            PaymentMethod.Cheque, [new AllocationInput(a, 177), new AllocationInput(b, 123)], Reference: "CHQ 004512"));

        Assert.Equal("REC/2026-27/0001", number);
        Assert.Equal((PaymentStatus.Paid, 0m), ((await Billing.GetAsync(a)).Status, (await Billing.GetAsync(a)).Outstanding));
        Assert.Equal((PaymentStatus.PartlyPaid, 231m), ((await Billing.GetAsync(b)).Status, (await Billing.GetAsync(b)).Outstanding));
        Assert.Equal(231m, await PartyLedgerAsync(Local));
        Assert.Equal(300m, await LedgerAsync(SystemAccounts.Cash, October));
        var row = Assert.Single(await Settlements.ListAsync(PartyKind.Customer));
        Assert.Equal((receipt, 300m, 0m, "CHQ 004512"), (row.Id, row.Allocated, row.Unapplied, row.Reference));
        await AssertBooksBalanceAsync();
    }

    [Fact]
    public async Task Allocations_cannot_exceed_the_amount_or_what_is_outstanding()
    {
        var (a, _) = await SellAsync(Local, new BillingLineInput(Led, 1));
        var (other, _) = await SellAsync(Bengaluru, new BillingLineInput(Led, 1));
        var (cash, _) = await CashSaleAsync(new BillingLineInput(Led, 1));

        SettlementInput Receipt(decimal amount, params AllocationInput[] allocations) =>
            new(SettlementKind.CustomerReceipt, October, Local, CashAccount, amount, PaymentMethod.Cash, allocations);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Settlements.PostAsync(Receipt(100, new AllocationInput(a, 150))));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Settlements.PostAsync(Receipt(500, new AllocationInput(a, 200))));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Settlements.PostAsync(Receipt(100, new AllocationInput(other, 50))));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Settlements.PostAsync(Receipt(100, new AllocationInput(cash, 50))));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Settlements.PostAsync(Receipt(0)));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Settlements.PostAsync(Receipt(100, new AllocationInput(a, 60), new AllocationInput(a, 60))
            with { Amount = 100 }));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Settlements.PostAsync(Receipt(100) with { AccountId = Accounts[SystemAccounts.Sales] }));

        Assert.Empty(await Settlements.ListAsync(PartyKind.Customer));
        Assert.Equal(177m, await PartyLedgerAsync(Local));
        await AssertBooksBalanceAsync();
    }

    [Fact]
    public async Task Advances_stay_unapplied_until_allocated_to_a_later_invoice()
    {
        var (advance, _) = await Settlements.PostAsync(new SettlementInput(SettlementKind.CustomerReceipt, October, Local, CashAccount, 500, PaymentMethod.Upi, []));
        Assert.Equal((-500m, 500m), ((await Parties.GetAsync(Local)).LedgerBalance, (await Parties.GetAsync(Local)).Unapplied));

        var (invoice, _) = await SellAsync(Local, new BillingLineInput(Led, 2));     // 354
        await Settlements.AllocateAsync(Local, invoice, advance, 354, October);

        var party = await Parties.GetAsync(Local);
        Assert.Equal((0m, 146m, -146m), (party.Outstanding, party.Unapplied, party.LedgerBalance));
        Assert.Equal(PaymentStatus.Paid, (await Billing.GetAsync(invoice)).Status);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Settlements.AllocateAsync(Local, invoice, advance, 1, October));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Settlements.AllocateAsync(Local, advance, invoice, 1, October));
    }

    [Fact]
    public async Task Reversing_a_receipt_reopens_its_invoices_and_restores_the_ledger()
    {
        var (invoice, _) = await SellAsync(Local, new BillingLineInput(Led, 1));
        var (receipt, _) = await Settlements.PostAsync(new SettlementInput(SettlementKind.CustomerReceipt, October, Local, CashAccount, 177, PaymentMethod.Cheque,
            [new AllocationInput(invoice, 177)]));

        await Settlements.ReverseAsync(receipt, October, "Cheque returned unpaid");

        Assert.Equal((PaymentStatus.Unpaid, 177m), ((await Billing.GetAsync(invoice)).Status, (await Billing.GetAsync(invoice)).Outstanding));
        Assert.Equal(177m, await PartyLedgerAsync(Local));
        Assert.Equal(0m, await LedgerAsync(SystemAccounts.Cash, October));
        Assert.True(Assert.Single(await Settlements.ListAsync(PartyKind.Customer)).IsReversed);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Settlements.ReverseAsync(receipt, October, "again"));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Settlements.AllocateAsync(Local, invoice, receipt, 1, October));
        await AssertBooksBalanceAsync();
    }

    [Fact]
    public async Task Supplier_payments_settle_bills_and_debit_notes_count_against_them()
    {
        var (bill, _) = await BuyAsync("PW/50", new BillingLineInput(Led, 10, Rate: 100));        // 1,180
        await ReturnAsync(bill, (1, 1));                                                          // debit note 118

        var (_, number) = await Settlements.PostAsync(new SettlementInput(SettlementKind.SupplierPayment, October, Vendor, CashAccount, 1_062,
            PaymentMethod.BankTransfer, [new AllocationInput(bill, 1_062)], Reference: "NEFT 77"));

        Assert.Equal("PAY/2026-27/0001", number);
        Assert.Equal(PaymentStatus.Paid, (await Billing.GetAsync(bill)).Status);
        var supplier = await Parties.GetAsync(Vendor);
        Assert.Equal((0m, 0m, 0m), (supplier.LedgerBalance, supplier.Outstanding, supplier.Unapplied));
        Assert.Equal(-1_062m, await LedgerAsync(SystemAccounts.Cash, October));
        await AssertBooksBalanceAsync();
    }

    [Fact]
    public async Task Supplier_refund_settles_an_unapplied_debit_note()
    {
        var (bill, _) = await BuyAsync("PW/51", new BillingLineInput(Led, 1, Rate: 100));
        await Settlements.PostAsync(new SettlementInput(SettlementKind.SupplierPayment, October, Vendor, CashAccount, 118, PaymentMethod.Cash, [new AllocationInput(bill, 118)]));
        var (note, _) = await ReturnAsync(bill, (1, 1));

        await Settlements.PostAsync(new SettlementInput(SettlementKind.SupplierRefund, October, Vendor, CashAccount, 118, PaymentMethod.Cash, [new AllocationInput(note, 118)]));

        Assert.Equal((0m, 0m, 0m), ((await Parties.GetAsync(Vendor)).LedgerBalance, (await Parties.GetAsync(Vendor)).Outstanding, (await Parties.GetAsync(Vendor)).Unapplied));
        await AssertBooksBalanceAsync();
    }

    [Fact]
    public async Task Party_ledgers_reconcile_with_open_items_after_mixed_activity()
    {
        var (a, _) = await SellAsync(Local, new BillingLineInput(Led, 3));
        var (b, _) = await SellAsync(Local, new BillingLineInput(Led, 1));
        await ReturnAsync(a, (1, 1));
        await Settlements.PostAsync(new SettlementInput(SettlementKind.CustomerReceipt, October, Local, CashAccount, 400, PaymentMethod.Cash, [new AllocationInput(b, 177)]));
        var (bill, _) = await BuyAsync("PW/60", new BillingLineInput(Led, 5));
        await Settlements.PostAsync(new SettlementInput(SettlementKind.SupplierPayment, October, Vendor, CashAccount, 200, PaymentMethod.Cash, [new AllocationInput(bill, 200)]));

        foreach (var party in (await Parties.ListAsync(PartyKind.Customer)).Concat(await Parties.ListAsync(PartyKind.Supplier)))
            Assert.Equal(party.LedgerBalance, party.Outstanding - party.Unapplied);
        var open = await Parties.OpenItemsAsync(Local, openOnly: true);
        Assert.Equal(["Customer receipt", "Sales invoice"], open.Select(i => i.Type).Order());
        await AssertBooksBalanceAsync();
    }
}

public sealed class PartyTests : BillingTestBase
{
    [Fact]
    public async Task Parties_get_their_own_ledger_under_debtors_or_creditors()
    {
        var customer = await Parties.GetAsync(Local);
        var supplier = await Parties.GetAsync(Vendor);
        var accounts = await Db.Get<ChartOfAccountsService>().ListAsync();

        Assert.Equal(Accounts[SystemAccounts.SundryDebtors], accounts.Single(x => x.Id == customer.AccountId).ParentId);
        Assert.Equal(Accounts[SystemAccounts.SundryCreditors], accounts.Single(x => x.Id == supplier.AccountId).ParentId);
        Assert.Equal(("27", "Maharashtra"), (supplier.StateCode, supplier.State));
        Assert.Equal("29", (await Parties.GetAsync(Bengaluru)).StateCode);
    }

    [Fact]
    public async Task Same_name_as_customer_and_supplier_gets_distinct_ledgers_but_duplicates_are_refused()
    {
        var id = await Parties.SaveAsync(new PartyInput(null, PartyKind.Supplier, new PartyDetails("Meera Electricals")));

        Assert.NotEqual((await Parties.GetAsync(Local)).AccountId, (await Parties.GetAsync(id)).AccountId);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Parties.SaveAsync(new PartyInput(null, PartyKind.Customer, new PartyDetails("Meera Electricals"))));
    }

    [Fact]
    public async Task Invalid_tax_details_are_refused_without_leaving_a_ledger()
    {
        var valid = Gstin("27", "AAACK1234A");
        var wrongCheck = valid[..14] + (valid[14] == '0' ? '1' : '0');
        var before = (await Db.Get<ChartOfAccountsService>().ListAsync()).Count;

        foreach (var details in new[] { new PartyDetails("Bad Data Co", Gstin: wrongCheck), new PartyDetails("Bad Data Co", StateCode: "99"),
                     new PartyDetails("Bad Data Co", Gstin: valid, StateCode: "29"), new PartyDetails("Bad Data Co", Pincode: "12") })
        {
            await Assert.ThrowsAsync<WorkspaceValidationException>(() => Parties.SaveAsync(new PartyInput(null, PartyKind.Customer, details)));
        }

        Assert.Equal(before, (await Db.Get<ChartOfAccountsService>().ListAsync()).Count);
    }

    [Fact]
    public async Task Renaming_a_party_renames_its_ledger_and_keeps_posted_documents()
    {
        var (invoice, _) = await SellAsync(Local, new BillingLineInput(Led, 1));

        await Parties.SaveAsync(new PartyInput(Local, PartyKind.Customer, new PartyDetails("Meera Electricals Pvt Ltd", StateCode: "27")));

        var accountId = (await Parties.GetAsync(Local)).AccountId;
        var account = (await Db.Get<ChartOfAccountsService>().ListAsync()).Single(a => a.Id == accountId);
        Assert.Equal("Meera Electricals Pvt Ltd", account.Name);
        Assert.Equal("Meera Electricals", (await Billing.GetAsync(invoice)).PartyName);
    }

    [Fact]
    public async Task Inactive_parties_cannot_be_billed()
    {
        await Parties.SetActiveAsync(Local, false);

        Assert.DoesNotContain(await Parties.ListAsync(PartyKind.Customer), p => p.Id == Local);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => SellAsync(Local, new BillingLineInput(Led, 1)));
    }
}
