using Nexora.Accounting;
using Nexora.Application;
using Nexora.Application.Inventory;
using Nexora.Domain.Inventory;

namespace Nexora.Tests.Inventory;

/// <summary>
/// Regression tests for the three inventory edge cases raised before Phase 5 (billing depends on them):
/// replenishing negative stock, duplicate serials in physical counts, and reversing a chain of documents.
/// </summary>
public sealed class InventoryEdgeCaseTests : StockTestBase
{
    // ----- 1. Replenishing negative stock (weighted average) -----

    [Fact]
    public async Task Receipt_that_brings_negative_stock_back_to_zero_leaves_no_orphan_value()
    {
        await Catalogue.SaveSettingsAsync(CostingMethod.WeightedAverage, allowNegativeStock: true);
        var led = await ProductAsync("LED-18W");
        await ReceiveAsync(led, 1, 100);
        await IssueAsync(led, 3);                                  // 1 at 100, then 2 more at the last cost 100: −300

        await ReceiveAsync(led, 2, 120);                           // the 2 missing units really cost 120 each

        Assert.Equal(0m, await OnHandAsync(led));
        Assert.Equal(0m, (await Stock.ValuationAsync()).Sum(r => r.Value));
        Assert.True((await Stock.ReconcileAsync(October)).IsReconciled);
        // Supplier is owed the full 240; the 40 under-costed on the earlier issue is recognised as an adjustment.
        Assert.Equal(-340m, (await Db.Get<LedgerService>().TrialBalanceAsync(October)).Rows.Single(r => r.AccountId == Supplier).Net);
        Assert.Equal(300m + 40m, await LedgerAsync(SystemAccounts.InventoryAdjustment, October));
    }

    [Fact]
    public async Task Receipt_beyond_the_negative_quantity_values_the_remainder_at_the_receipt_cost()
    {
        await Catalogue.SaveSettingsAsync(CostingMethod.WeightedAverage, allowNegativeStock: true);
        var led = await ProductAsync("LED-18W");
        await ReceiveAsync(led, 1, 100);
        await IssueAsync(led, 3);

        await ReceiveAsync(led, 3, 120);

        var row = Assert.Single(await Stock.ValuationAsync());
        Assert.Equal((1m, 120m), (row.Quantity, row.Value));
        Assert.True((await Stock.ReconcileAsync(October)).IsReconciled);
        Assert.Equal(-120m, (await IssueAsync(led, 1)).ValueChange);
    }

    [Fact]
    public async Task Partial_replenishment_settles_only_the_units_received()
    {
        await Catalogue.SaveSettingsAsync(CostingMethod.WeightedAverage, allowNegativeStock: true);
        var led = await ProductAsync("LED-18W");
        await ReceiveAsync(led, 1, 100);
        await IssueAsync(led, 5);                                  // issue charged 500; stock is −4 units valued −400

        await ReceiveAsync(led, 1, 130);                           // settles one of the four at 130

        var row = Assert.Single(await Stock.ValuationAsync());
        Assert.Equal((-3m, -300m), (row.Quantity, row.Value));
        Assert.True((await Stock.ReconcileAsync(October)).IsReconciled);
        Assert.Equal(500m + 30m, await LedgerAsync(SystemAccounts.InventoryAdjustment, October));
    }

    [Fact]
    public async Task Adjustment_in_while_negative_keeps_stock_and_ledger_equal()
    {
        await Catalogue.SaveSettingsAsync(CostingMethod.WeightedAverage, allowNegativeStock: true);
        var led = await ProductAsync("LED-18W");
        await ReceiveAsync(led, 2, 100);
        await IssueAsync(led, 4);

        await Stock.PostAsync(new StockDocumentInput(StockDocumentType.AdjustmentIn, October, Main, "Found", [new(led, 2)]));

        Assert.Equal(0m, await OnHandAsync(led));
        Assert.Equal(0m, (await Stock.ValuationAsync()).Sum(r => r.Value));
        Assert.True((await Stock.ReconcileAsync(October)).IsReconciled);
    }

    // ----- 2. Duplicate serials in physical counts -----

    [Fact]
    public async Task Count_cannot_create_a_serial_that_is_in_stock_in_another_warehouse()
    {
        var phone = await ProductAsync("PHONE-X", TrackingMode.Serial);
        await ReceiveAsync(phone, 1, 9_000, serial: "SN-001");

        var error = await Assert.ThrowsAsync<WorkspaceValidationException>(() => Stock.PostAsync(new StockDocumentInput(
            StockDocumentType.PhysicalCount, October, Second, "Shop count", [new(phone, 1, SerialNumber: "SN-001")])));

        Assert.Contains("already in stock", error.Message, StringComparison.Ordinal);
        Assert.Equal(["SN-001"], (await Stock.BalancesAsync()).Select(b => b.SerialNumber));
    }

    [Fact]
    public async Task Count_cannot_list_the_same_serial_twice()
    {
        var phone = await ProductAsync("PHONE-X", TrackingMode.Serial);

        var error = await Assert.ThrowsAsync<WorkspaceValidationException>(() => Stock.PostAsync(new StockDocumentInput(
            StockDocumentType.PhysicalCount, October, Main, "Count", [new(phone, 1, UnitCost: null, SerialNumber: "SN-9"), new(phone, 1, SerialNumber: "sn-9")])));

        Assert.Contains("listed twice", error.Message, StringComparison.Ordinal);
        Assert.Empty(await Stock.BalancesAsync());
    }

    [Fact]
    public async Task Count_can_still_record_a_missing_serial_and_find_a_new_one()
    {
        var phone = await ProductAsync("PHONE-X", TrackingMode.Serial);
        await ReceiveAsync(phone, 1, 9_000, serial: "SN-001");

        await Stock.PostAsync(new StockDocumentInput(StockDocumentType.PhysicalCount, October, Main, "Count",
            [new(phone, 0, SerialNumber: "SN-001"), new(phone, 1, SerialNumber: "SN-002")]));

        Assert.Equal(["SN-002"], (await Stock.BalancesAsync()).Select(b => b.SerialNumber));
        Assert.True((await Stock.ReconcileAsync(October)).IsReconciled);
    }

    // ----- 3. Reversing a chain of stock documents -----

    [Fact]
    public async Task Documents_can_be_reversed_newest_first_down_the_chain()
    {
        var led = await ProductAsync("LED-18W");
        var receipt = await ReceiveAsync(led, 10, 100);
        var issue = await IssueAsync(led, 4);

        await Stock.ReverseAsync(issue.DocumentId, October, "");
        await Stock.ReverseAsync(receipt.DocumentId, October, "");

        Assert.Equal(0m, await OnHandAsync(led));
        Assert.Empty(await Stock.ValuationAsync());
        Assert.True((await Stock.ReconcileAsync(October)).IsReconciled);
        Assert.Equal(0m, await LedgerInventoryAsync(October));
    }

    [Fact]
    public async Task Costing_after_a_reversed_chain_ignores_every_reversed_document()
    {
        var led = await ProductAsync("LED-18W");
        await ReceiveAsync(led, 10, 100);
        var second = await ReceiveAsync(led, 10, 200);
        var issue = await IssueAsync(led, 4);                      // at the average 150

        await Stock.ReverseAsync(issue.DocumentId, October, "");
        await Stock.ReverseAsync(second.DocumentId, October, "");

        Assert.Equal(-100m, (await IssueAsync(led, 1)).ValueChange);
        Assert.True((await Stock.ReconcileAsync(October)).IsReconciled);
    }

    [Fact]
    public async Task An_earlier_document_still_cannot_be_reversed_while_a_later_live_one_exists()
    {
        var led = await ProductAsync("LED-18W");
        var receipt = await ReceiveAsync(led, 10, 100);
        var first = await IssueAsync(led, 4);
        var second = await IssueAsync(led, 1);

        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Stock.ReverseAsync(first.DocumentId, October, ""));
        await Stock.ReverseAsync(second.DocumentId, October, "");
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Stock.ReverseAsync(receipt.DocumentId, October, ""));   // the first issue is live
        Assert.Equal(6m, await OnHandAsync(led));
    }

    [Fact]
    public async Task Reversal_cannot_be_dated_before_the_products_latest_movement()
    {
        var led = await ProductAsync("LED-18W");
        var receipt = await ReceiveAsync(led, 10, 100, date: October);
        var issue = await IssueAsync(led, 4, date: October.AddDays(5));
        await Stock.ReverseAsync(issue.DocumentId, October.AddDays(5), "");

        var error = await Assert.ThrowsAsync<WorkspaceValidationException>(() => Stock.ReverseAsync(receipt.DocumentId, October.AddDays(1), ""));
        Assert.Contains("15-10-2026", error.Message, StringComparison.Ordinal);
    }
}
