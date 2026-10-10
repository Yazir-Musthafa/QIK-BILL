using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nexora.Accounting;
using Nexora.Application;
using Nexora.Application.Companies;
using Nexora.Application.Inventory;
using Nexora.Application.Security;
using Nexora.Application.Users;
using Nexora.Domain.Accounting;
using Nexora.Domain.Common;
using Nexora.Domain.Inventory;
using Nexora.Infrastructure.Persistence;
using Nexora.Tests.Accounting;
using Nexora.Tests.Infrastructure;
using Nexora.Tests.Workspace;

namespace Nexora.Tests.Inventory;

/// <summary>Shared setup: a company with two warehouses, a supplier ledger and a few products.</summary>
public abstract class StockTestBase : IAsyncLifetime
{
    protected static readonly DateOnly October = new(2026, 10, 10);
    protected readonly SqliteTestDatabase Db = new();

    protected InventoryCatalogueService Catalogue => Db.Get<InventoryCatalogueService>();

    protected StockService Stock => Db.Get<StockService>();

    protected Guid Main { get; private set; }

    protected Guid Second { get; private set; }

    protected Guid Supplier { get; private set; }

    protected Dictionary<string, Guid> Accounts { get; private set; } = [];

    protected Dictionary<string, Guid> Units { get; private set; } = [];

    public virtual async Task InitializeAsync()
    {
        await Db.Workspace.InitializeAsync();
        await Db.Workspace.CompleteSetupAsync(WorkspaceServiceTests.ValidRequest(demo: true));
        var companies = Db.Get<CompanyService>();
        Main = await companies.SaveWarehouseAsync(new WarehouseInput(null, "MAIN", "Main store", null));
        Second = await companies.SaveWarehouseAsync(new WarehouseInput(null, "SHOP", "Shop floor", null));
        Accounts = await PostingTests.LoadAccountsAsync(Db);
        Supplier = await Db.Get<ChartOfAccountsService>().SaveAsync(new AccountInput(null, "2111", "Kaveri Distributors", Accounts[SystemAccounts.SundryCreditors], AccountKind.Payable));
        Units = (await Catalogue.ListUnitsAsync()).ToDictionary(u => u.Code, u => u.Id);
    }

    public Task DisposeAsync() => Db.DisposeAsync().AsTask();

    protected Task<Guid> ProductAsync(string sku, TrackingMode tracking = TrackingMode.None, bool expiry = false, string unit = "PCS", decimal reorder = 0) =>
        Catalogue.SaveProductAsync(new ProductInput(null, new ProductDetails(sku, sku + " item", PurchasePrice: 90, SellingPrice: 150, Mrp: 160, ReorderLevel: reorder),
            Units[unit], tracking, expiry));

    protected Task<StockPostingResult> ReceiveAsync(Guid product, decimal quantity, decimal unitCost, Guid? warehouse = null, DateOnly? date = null,
        string? batch = null, DateOnly? expires = null, string? serial = null, Guid? unitId = null) =>
        Stock.PostAsync(new StockDocumentInput(StockDocumentType.Receipt, date ?? October, warehouse ?? Main, "Goods received",
            [new(product, quantity, unitId, unitCost, BatchNumber: batch, ExpiresOn: expires, SerialNumber: serial)], CounterAccountId: Supplier));

    protected Task<StockPostingResult> IssueAsync(Guid product, decimal quantity, Guid? warehouse = null, DateOnly? date = null, Guid? batchId = null,
        string? serial = null, StockState state = StockState.Good, StockDocumentType type = StockDocumentType.Issue) =>
        Stock.PostAsync(new StockDocumentInput(type, date ?? October, warehouse ?? Main, "Issued",
            [new(product, quantity, BatchId: batchId, SerialNumber: serial)], FromState: state));

    protected async Task<decimal> OnHandAsync(Guid product, Guid? warehouse = null, StockState? state = null) =>
        (await Stock.BalancesAsync()).Where(b => b.ProductId == product && (warehouse == null || b.WarehouseId == warehouse) && (state == null || b.State == state))
        .Sum(b => b.Quantity);

    protected async Task<decimal> LedgerInventoryAsync(DateOnly asOf) =>
        (await Db.Get<LedgerService>().TrialBalanceAsync(asOf)).Rows.Where(r => r.AccountId == Accounts[SystemAccounts.Inventory]).Sum(r => r.Net);

    protected async Task<decimal> LedgerAsync(string key, DateOnly asOf) =>
        (await Db.Get<LedgerService>().TrialBalanceAsync(asOf)).Rows.Where(r => r.AccountId == Accounts[key]).Sum(r => r.Net);
}

public sealed class StockPostingTests : StockTestBase
{
    [Fact]
    public async Task New_companies_get_inventory_settings_units_tax_categories_and_numbering()
    {
        var settings = await Catalogue.GetSettingsAsync();
        Assert.Equal((CostingMethod.WeightedAverage, false, false), (settings.CostingMethod, settings.AllowNegativeStock, settings.HasMovements));
        Assert.Equal(10, Units.Count);
        Assert.Equal(4, (await Catalogue.ListTaxCategoriesAsync()).Count);
        Assert.Contains(await Db.Get<CompanyService>().ListNumberingAsync(), r => r.DocumentType == InventoryDocumentTypes.Receipt && r.Example == "SR/2026-27/0001");
    }

    [Fact]
    public async Task Receipt_posts_stock_and_a_balanced_voucher_that_reconciles()
    {
        var led = await ProductAsync("LED-18W");

        var result = await ReceiveAsync(led, 10, 100.25m);

        Assert.Equal("SR/2026-27/0001", result.Number);
        Assert.Equal(1_002.50m, result.ValueChange);
        Assert.NotNull(result.VoucherId);
        Assert.Equal(10m, await OnHandAsync(led));
        Assert.Equal(1_002.50m, await LedgerInventoryAsync(October));
        Assert.Equal(-1_002.50m, (await Db.Get<LedgerService>().TrialBalanceAsync(October)).Rows.Single(r => r.AccountId == Supplier).Net);
        var reconciliation = await Stock.ReconcileAsync(October);
        Assert.True(reconciliation.IsReconciled);
        Assert.Equal(1_002.50m, reconciliation.StockValue);
    }

    [Fact]
    public async Task Weighted_average_cost_flows_into_issues_and_the_ledger()
    {
        var led = await ProductAsync("LED-18W");
        await ReceiveAsync(led, 10, 100);
        await ReceiveAsync(led, 10, 130);

        var issue = await IssueAsync(led, 5);

        Assert.Equal(-575m, issue.ValueChange);
        var row = Assert.Single(await Stock.ValuationAsync());
        Assert.Equal((15m, 1_725m, 115m), (row.Quantity, row.Value, row.UnitCost));
        Assert.Equal(575m, await LedgerAsync(SystemAccounts.InventoryAdjustment, October));
        Assert.True((await Stock.ReconcileAsync(October)).IsReconciled);
    }

    [Fact]
    public async Task Fifo_company_issues_oldest_cost_first()
    {
        await Catalogue.SaveSettingsAsync(CostingMethod.Fifo, allowNegativeStock: false);
        var led = await ProductAsync("LED-18W");
        await ReceiveAsync(led, 10, 100);
        await ReceiveAsync(led, 10, 130);

        var issue = await IssueAsync(led, 12);

        Assert.Equal(-1_260m, issue.ValueChange);
        Assert.Equal(1_040m, Assert.Single(await Stock.ValuationAsync()).Value);
        Assert.True((await Stock.ReconcileAsync(October)).IsReconciled);
    }

    [Fact]
    public async Task Costing_method_cannot_change_after_stock_moves_and_negative_fifo_is_refused()
    {
        await Assert.ThrowsAsync<DomainException>(() => Catalogue.SaveSettingsAsync(CostingMethod.Fifo, allowNegativeStock: true));
        var led = await ProductAsync("LED-18W");
        await ReceiveAsync(led, 1, 100);

        await Assert.ThrowsAsync<DomainException>(() => Catalogue.SaveSettingsAsync(CostingMethod.Fifo, false));
        Assert.True((await Catalogue.GetSettingsAsync()).HasMovements);
    }

    [Fact]
    public async Task Negative_stock_is_blocked_unless_the_company_allows_it()
    {
        var led = await ProductAsync("LED-18W");
        await ReceiveAsync(led, 3, 100);

        var error = await Assert.ThrowsAsync<WorkspaceValidationException>(() => IssueAsync(led, 5));
        Assert.Contains("only 3 of LED-18W", error.Message, StringComparison.Ordinal);
        Assert.Equal(3m, await OnHandAsync(led));

        await Catalogue.SaveSettingsAsync(CostingMethod.WeightedAverage, allowNegativeStock: true);
        var issue = await IssueAsync(led, 5);
        Assert.Equal(-500m, issue.ValueChange);
        Assert.Equal(-2m, await OnHandAsync(led));
        Assert.True((await Stock.ReconcileAsync(October)).IsReconciled);
    }

    [Fact]
    public async Task Transfers_move_quantity_without_changing_value_or_posting_a_voucher()
    {
        var led = await ProductAsync("LED-18W");
        await ReceiveAsync(led, 10, 100);

        var transfer = await Stock.PostAsync(new StockDocumentInput(StockDocumentType.Transfer, October, Main, "To shop", [new(led, 4)], ToWarehouseId: Second));

        Assert.Null(transfer.VoucherId);
        Assert.Equal((6m, 4m), (await OnHandAsync(led, Main), await OnHandAsync(led, Second)));
        Assert.Equal(1_000m, Assert.Single(await Stock.ValuationAsync()).Value);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() =>
            Stock.PostAsync(new StockDocumentInput(StockDocumentType.Transfer, October, Second, "Too many", [new(led, 5)], ToWarehouseId: Main)));
        await Assert.ThrowsAsync<DomainException>(() =>
            Stock.PostAsync(new StockDocumentInput(StockDocumentType.Transfer, October, Main, "Same place", [new(led, 1)], ToWarehouseId: Main)));
    }

    [Fact]
    public async Task Damaged_stock_is_unavailable_and_its_write_off_posts_to_adjustments()
    {
        var led = await ProductAsync("LED-18W");
        await ReceiveAsync(led, 10, 100);
        await Stock.PostAsync(new StockDocumentInput(StockDocumentType.StateChange, October, Main, "Broken in transit", [new(led, 2)],
            FromState: StockState.Good, ToState: StockState.Damaged));

        var product = (await Catalogue.ListProductsAsync()).Single();
        Assert.Equal((10m, 8m), (product.OnHand, product.Available));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => IssueAsync(led, 9));

        var writeOff = await IssueAsync(led, 2, state: StockState.Damaged, type: StockDocumentType.AdjustmentOut);
        Assert.Equal(-200m, writeOff.ValueChange);
        Assert.Equal(200m, await LedgerAsync(SystemAccounts.InventoryAdjustment, October));
        Assert.Equal(0m, await OnHandAsync(led, state: StockState.Damaged));
    }

    [Fact]
    public async Task Physical_count_posts_gains_and_losses_against_the_book_quantity()
    {
        var led = await ProductAsync("LED-18W");
        var fan = await ProductAsync("FAN-1200");
        await ReceiveAsync(led, 15, 100);
        await ReceiveAsync(fan, 2, 1_000);

        var count = await Stock.PostAsync(new StockDocumentInput(StockDocumentType.PhysicalCount, October, Main, "Month-end count",
            [new(led, 12), new(fan, 3)]));

        Assert.Equal((12m, 3m), (await OnHandAsync(led), await OnHandAsync(fan)));
        Assert.Equal(-300m + 1_000m, count.ValueChange);
        Assert.True((await Stock.ReconcileAsync(October)).IsReconciled);
    }

    [Fact]
    public async Task Opening_stock_credits_opening_balance_difference()
    {
        var led = await ProductAsync("LED-18W");

        await Stock.PostAsync(new StockDocumentInput(StockDocumentType.Opening, new DateOnly(2026, 4, 1), Main, "Brought forward", [new(led, 50, UnitCost: 80)]));

        Assert.Equal(4_000m, await LedgerInventoryAsync(October));
        Assert.Equal(-4_000m, await LedgerAsync(SystemAccounts.OpeningBalanceDifference, October));
    }

    [Fact]
    public async Task Alternate_units_convert_to_the_base_unit()
    {
        var led = await ProductAsync("LED-18W");
        await Catalogue.AddConversionAsync(led, Units["BOX"], 12);

        await ReceiveAsync(led, 2, 1_200, unitId: Units["BOX"]);

        Assert.Equal(24m, await OnHandAsync(led));
        Assert.Equal(2_400m, Assert.Single(await Stock.ValuationAsync()).Value);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => ReceiveAsync(led, 1, 10, unitId: Units["DOZ"]));
        await Assert.ThrowsAsync<DomainException>(() => ReceiveAsync(led, 1.5m, 10));
    }

    [Fact]
    public async Task Batches_need_numbers_and_expiry_and_cannot_go_negative()
    {
        var syrup = await ProductAsync("SYRUP", TrackingMode.Batch, expiry: true, unit: "NOS");
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => ReceiveAsync(syrup, 10, 50));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => ReceiveAsync(syrup, 10, 50, batch: "B1"));

        await ReceiveAsync(syrup, 10, 50, batch: "b1", expires: new DateOnly(2026, 10, 20));
        await ReceiveAsync(syrup, 5, 55, batch: "B2", expires: new DateOnly(2027, 6, 30));
        var b1 = (await Stock.BalancesAsync()).Single(b => b.Batch == "B1");
        Assert.Equal(new DateOnly(2026, 10, 20), b1.ExpiresOn);

        await Assert.ThrowsAsync<WorkspaceValidationException>(() => IssueAsync(syrup, 11, batchId: b1.BatchId));
        await IssueAsync(syrup, 4, batchId: b1.BatchId);

        var expiring = await Stock.ExpiringBatchesAsync(October, 30);
        var row = Assert.Single(expiring);
        Assert.Equal(("B1", 6m, false), (row.Batch, row.Quantity, row.IsExpired));
        Assert.True(Assert.Single(await Stock.ExpiringBatchesAsync(new DateOnly(2026, 10, 25), 0)).IsExpired);
    }

    [Fact]
    public async Task Serial_numbers_are_unique_in_stock_and_issued_one_by_one()
    {
        var phone = await ProductAsync("PHONE-X", TrackingMode.Serial);
        await ReceiveAsync(phone, 1, 9_000, serial: "sn-001");
        await ReceiveAsync(phone, 1, 9_000, serial: "SN-002");

        await Assert.ThrowsAsync<WorkspaceValidationException>(() => ReceiveAsync(phone, 1, 9_000, serial: "SN-001"));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => ReceiveAsync(phone, 2, 9_000, serial: "SN-003"));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => IssueAsync(phone, 1, serial: "SN-999"));

        await IssueAsync(phone, 1, serial: "SN-001");
        Assert.Equal(["SN-002"], (await Stock.BalancesAsync()).Select(b => b.SerialNumber));
        await ReceiveAsync(phone, 1, 9_100, serial: "SN-001");
    }

    [Fact]
    public async Task Stock_dates_cannot_go_back_before_a_products_last_movement()
    {
        var led = await ProductAsync("LED-18W");
        await ReceiveAsync(led, 10, 100, date: October);

        var error = await Assert.ThrowsAsync<WorkspaceValidationException>(() => IssueAsync(led, 1, date: October.AddDays(-1)));
        Assert.Contains("movements up to 10-10-2026", error.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => ReceiveAsync(led, 1, 100, date: new DateOnly(2027, 4, 1)));
    }

    [Fact]
    public async Task Reversal_restores_stock_value_and_ledger_and_only_for_the_latest_document()
    {
        var led = await ProductAsync("LED-18W");
        var first = await ReceiveAsync(led, 10, 100);
        var second = await ReceiveAsync(led, 10, 130);

        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Stock.ReverseAsync(first.DocumentId, October, ""));
        var number = await Stock.ReverseAsync(second.DocumentId, October, "Wrong supplier");

        Assert.Equal("SX/2026-27/0001", number);
        Assert.Equal((10m, 1_000m), (await OnHandAsync(led), Assert.Single(await Stock.ValuationAsync()).Value));
        Assert.Equal(1_000m, await LedgerInventoryAsync(October));
        Assert.True((await Stock.ReconcileAsync(October)).IsReconciled);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Stock.ReverseAsync(second.DocumentId, October, ""));

        // Costing ignores the reversed pair: the next issue is at 100, not the average with 130.
        Assert.Equal(-500m, (await IssueAsync(led, 5)).ValueChange);
        Assert.Contains(await Stock.DocumentsAsync(), d => d.Id == second.DocumentId && d.IsReversed);
    }

    [Fact]
    public async Task Stock_vouchers_cannot_be_reversed_or_imitated_from_the_voucher_screen()
    {
        var led = await ProductAsync("LED-18W");
        var receipt = await ReceiveAsync(led, 10, 100);

        var reverse = await Assert.ThrowsAsync<WorkspaceValidationException>(() => Db.Get<VoucherService>().ReverseAsync(receipt.VoucherId!.Value, October, ""));
        Assert.Contains("Reverse that document under Stock", reverse.Message, StringComparison.Ordinal);

        var manual = await Assert.ThrowsAsync<WorkspaceValidationException>(() => Db.Get<VoucherService>().SaveAndPostAsync(
            new VoucherInput(null, VoucherType.Journal, October, "Manual stock", [new(Accounts[SystemAccounts.Inventory], 50, 0), new(Accounts[SystemAccounts.Capital], 0, 50)])));
        Assert.Contains("kept by stock documents", manual.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Db.Get<VoucherService>().PostOpeningBalancesAsync(2026,
            [new(Accounts[SystemAccounts.Inventory], 50, 0), new(Accounts[SystemAccounts.Capital], 0, 50)]));
        Assert.True((await Stock.ReconcileAsync(October)).IsReconciled);
    }

    [Fact]
    public async Task Concurrent_issues_never_take_more_than_is_in_stock()
    {
        var led = await ProductAsync("LED-18W");
        await ReceiveAsync(led, 10, 100);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            try
            {
                await IssueAsync(led, 2);
                return true;
            }
            catch (WorkspaceValidationException)
            {
                return false;
            }
        })));

        Assert.Equal(5, outcomes.Count(o => o));
        Assert.Equal(0m, await OnHandAsync(led));
        Assert.True((await Stock.ReconcileAsync(October)).IsReconciled);
    }

    /// <summary>Every document type in one company: stock valuation, the stock ledger and the general ledger agree.</summary>
    [Fact]
    public async Task Inventory_valuation_reconciles_to_the_general_ledger()
    {
        var led = await ProductAsync("LED-18W");
        var fan = await ProductAsync("FAN-1200");
        var syrup = await ProductAsync("SYRUP", TrackingMode.Batch, expiry: true, unit: "NOS");
        await Stock.PostAsync(new StockDocumentInput(StockDocumentType.Opening, new DateOnly(2026, 4, 1), Main, "Opening", [new(led, 20, UnitCost: 95), new(fan, 3, UnitCost: 1_850)]));
        await ReceiveAsync(led, 30, 101.37m);
        await ReceiveAsync(syrup, 40, 42.5m, batch: "L1", expires: new DateOnly(2027, 1, 31));
        await IssueAsync(led, 17);
        await Stock.PostAsync(new StockDocumentInput(StockDocumentType.Transfer, October, Main, "Shop", [new(led, 10), new(fan, 1)], ToWarehouseId: Second));
        await Stock.PostAsync(new StockDocumentInput(StockDocumentType.StateChange, October, Second, "Dented", [new(fan, 1)], ToState: StockState.Damaged));
        await IssueAsync(fan, 1, warehouse: Second, state: StockState.Damaged, type: StockDocumentType.AdjustmentOut);
        await Stock.PostAsync(new StockDocumentInput(StockDocumentType.AdjustmentIn, October, Main, "Found", [new(fan, 1)]));
        await Stock.PostAsync(new StockDocumentInput(StockDocumentType.PhysicalCount, October, Main, "Count", [new(led, 22)]));
        var batch = (await Stock.BalancesAsync()).Single(b => b.Batch == "L1").BatchId;
        var issued = await IssueAsync(syrup, 7, batchId: batch);
        await Stock.ReverseAsync(issued.DocumentId, October, "");

        var asOf = new DateOnly(2027, 3, 31);
        var valuation = (await Stock.ValuationAsync(asOf)).Sum(r => r.Value);
        var reconciliation = await Stock.ReconcileAsync(asOf);
        var sheet = await Db.Get<LedgerService>().BalanceSheetAsync(asOf);

        Assert.True(reconciliation.IsReconciled, $"Stock {reconciliation.StockValue} vs ledger {reconciliation.LedgerValue}");
        Assert.Equal(valuation, reconciliation.StockValue);
        Assert.Equal(valuation, await LedgerInventoryAsync(asOf));
        Assert.Equal(valuation, sheet.Assets.Single(l => l.AccountId == Accounts[SystemAccounts.Inventory]).Amount);
        Assert.True(sheet.IsBalanced);
        Assert.True((await Db.Get<LedgerService>().TrialBalanceAsync(asOf)).IsBalanced);
    }
}

public sealed class ProductCatalogueTests : StockTestBase
{
    [Fact]
    public async Task Products_are_created_updated_and_codes_are_unique()
    {
        var id = await Catalogue.SaveProductAsync(new ProductInput(null, new ProductDetails("led-18w", "LED panel", Barcode: "4006381333931", QrCode: "QIK:LED-18W",
            Category: "Lighting", Brand: "Brightline", HsnCode: "9405", PurchasePrice: 310, SellingPrice: 450, Mrp: 499), Units["PCS"], TrackingMode.None, false));

        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Catalogue.SaveProductAsync(new ProductInput(null, new ProductDetails("LED-18W", "Copy"), Units["PCS"], TrackingMode.None, false)));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Catalogue.SaveProductAsync(new ProductInput(null, new ProductDetails("OTHER", "Copy", Barcode: "4006381333931"), Units["PCS"], TrackingMode.None, false)));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Catalogue.SaveProductAsync(new ProductInput(null, new ProductDetails("OTHER", "Copy", QrCode: "QIK:LED-18W"), Units["PCS"], TrackingMode.None, false)));

        await Catalogue.SaveProductAsync(new ProductInput(id, new ProductDetails("LED-18W", "LED panel 18 W", Barcode: "4006381333931", PurchasePrice: 315, SellingPrice: 460, Mrp: 499), Units["PCS"], TrackingMode.None, false));
        var row = Assert.Single(await Catalogue.ListProductsAsync());
        Assert.Equal(("LED panel 18 W", 315m, 460m), (row.Name, row.PurchasePrice!.Value, row.SellingPrice));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() =>
            Catalogue.SaveProductAsync(new ProductInput(id, new ProductDetails("LED-18W", "LED"), Units["BOX"], TrackingMode.None, false)));
    }

    [Fact]
    public async Task Archiving_requires_zero_stock_and_hides_the_product()
    {
        var led = await ProductAsync("LED-18W");
        await ReceiveAsync(led, 2, 100);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Catalogue.SetArchivedAsync(led, true));

        await IssueAsync(led, 2);
        await Catalogue.SetArchivedAsync(led, true);
        Assert.Empty(await Catalogue.ListProductsAsync());
        Assert.Single(await Catalogue.ListProductsAsync(includeArchived: true));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => ReceiveAsync(led, 1, 100));
    }

    [Fact]
    public async Task Variants_hold_stock_and_their_parent_does_not()
    {
        var shirt = await ProductAsync("TSHIRT");
        var medium = await Catalogue.AddVariantAsync(shirt, new ProductDetails("TSHIRT-M-RED", "", VariantSize: "M", VariantColour: "Red", SellingPrice: 150, Mrp: 160));

        var rows = await Catalogue.ListProductsAsync();
        Assert.True(rows.Single(r => r.Id == shirt).HasVariants);
        Assert.Equal("TSHIRT item / M / Red", rows.Single(r => r.Id == medium).DisplayName);
        await ReceiveAsync(medium, 5, 80);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => ReceiveAsync(shirt, 5, 80));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Catalogue.AddVariantAsync(medium, new ProductDetails("X", "", VariantSize: "L")));
    }

    [Fact]
    public async Task Low_stock_lists_products_at_or_below_their_reorder_level()
    {
        var led = await ProductAsync("LED-18W", reorder: 10);
        var fan = await ProductAsync("FAN-1200", reorder: 2);
        await ProductAsync("NOREORDER");
        await ReceiveAsync(led, 10, 100);
        await ReceiveAsync(fan, 5, 1_000);

        Assert.Equal(["LED-18W"], (await Stock.LowStockAsync()).Select(r => r.Sku));
    }

    [Fact]
    public async Task Units_and_tax_categories_can_be_added_and_duplicates_are_refused()
    {
        await Catalogue.SaveUnitAsync(null, "set", "Set", 0);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Catalogue.SaveUnitAsync(null, "SET", "Again", 0));
        await Catalogue.SaveTaxCategoryAsync(null, "GST 3%", 3);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Catalogue.SaveTaxCategoryAsync(null, "GST 3%", 3));
        Assert.Contains(await Catalogue.ListUnitsAsync(), u => u.Code == "SET");
    }
}

public sealed class InventoryAccessTests : StockTestBase
{
    private async Task SignInAsAsync(string username, string role)
    {
        var roleId = (await Db.Get<RoleService>().ListAsync()).Single(r => r.Name == role).Id;
        await Db.Get<UserService>().CreateAsync(new NewUserRequest(username, username, "temporary pass 1", roleId));
        Db.Workspace.SignOut();
        await Db.Workspace.SignInAsync(username, "temporary pass 1");
        await Db.Workspace.ChangePasswordAsync("temporary pass 1", "my own phrase 9");
    }

    [Fact]
    public async Task Users_without_view_cost_never_receive_cost_figures()
    {
        var led = await ProductAsync("LED-18W");
        await ReceiveAsync(led, 4, 100);
        await SignInAsAsync("bina", "Billing Operator");

        var row = Assert.Single(await Catalogue.ListProductsAsync());
        Assert.Null(row.PurchasePrice);
        Assert.Equal(150m, row.SellingPrice);
        Assert.All(await Stock.MovementsAsync(), m => Assert.Null(m.Value));
        Assert.All(await Stock.DocumentsAsync(), d => Assert.Null(d.Value));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Stock.ValuationAsync());
        await Assert.ThrowsAsync<AccessDeniedException>(() => Stock.ReconcileAsync(October));
    }

    [Fact]
    public async Task Read_only_users_cannot_change_products_or_stock()
    {
        var led = await ProductAsync("LED-18W");
        await SignInAsAsync("ravi", "Read-only");

        Assert.Single(await Catalogue.ListProductsAsync());
        await Assert.ThrowsAsync<AccessDeniedException>(() => Catalogue.SaveProductAsync(new ProductInput(null, new ProductDetails("NEW", "New"), Units["PCS"], TrackingMode.None, false)));
        await Assert.ThrowsAsync<AccessDeniedException>(() => ReceiveAsync(led, 1, 100));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Catalogue.SaveSettingsAsync(CostingMethod.Fifo, false));
    }

    [Fact]
    public async Task Inventory_managers_post_stock_and_accountants_only_view_it()
    {
        await SignInAsAsync("ivan", "Inventory Manager");
        var led = await ProductAsync("LED-18W");
        await ReceiveAsync(led, 5, 100);
        Assert.Single(await Stock.ValuationAsync());
        await Assert.ThrowsAsync<AccessDeniedException>(() => Catalogue.SaveSettingsAsync(CostingMethod.Fifo, false));

        Db.Workspace.SignOut();
        await Db.Workspace.SignInAsync("asha", "quiet ledger 42");
        await SignInAsAsync("anu", "Accountant");
        Assert.True((await Stock.ReconcileAsync(October)).IsReconciled);
        await Assert.ThrowsAsync<AccessDeniedException>(() => IssueAsync(led, 1));
    }

    [Fact]
    public async Task Editing_a_product_without_view_cost_keeps_the_stored_purchase_price()
    {
        var led = await ProductAsync("LED-18W");
        var role = await Db.Get<RoleService>().SaveAsync(new RoleInput(null, "Catalogue editor", "",
            [Nexora.Domain.Security.Permissions.InventoryView, Nexora.Domain.Security.Permissions.InventoryManage], 0));
        var roleName = (await Db.Get<RoleService>().ListAsync()).Single(r => r.Id == role).Name;
        await SignInAsAsync("cat", roleName);

        await Catalogue.SaveProductAsync(new ProductInput(led, new ProductDetails("LED-18W", "Renamed", PurchasePrice: 1, SellingPrice: 150, Mrp: 160), Units["PCS"], TrackingMode.None, false));

        Db.Workspace.SignOut();
        await Db.Workspace.SignInAsync("asha", "quiet ledger 42");
        var row = Assert.Single(await Catalogue.ListProductsAsync());
        Assert.Equal(("Renamed", 90m), (row.Name, row.PurchasePrice!.Value));
    }
}

public sealed class InventoryIsolationAndImmutabilityTests : StockTestBase
{
    [Fact]
    public async Task Another_companys_products_and_stock_are_out_of_reach()
    {
        var led = await ProductAsync("LED-18W");
        await ReceiveAsync(led, 5, 100);
        var demo = (await Db.Workspace.GetCompaniesAsync()).Single(c => c.IsDemo);
        await Db.Workspace.SwitchCompanyAsync(demo.Id);

        Assert.Empty(await Catalogue.ListProductsAsync());
        Assert.Empty(await Stock.BalancesAsync());
        Assert.Empty(await Stock.DocumentsAsync());
        var demoWarehouse = await Db.Get<CompanyService>().SaveWarehouseAsync(new WarehouseInput(null, "DW", "Demo store", null));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Stock.PostAsync(new StockDocumentInput(StockDocumentType.AdjustmentIn, October, demoWarehouse, "", [new(led, 1, UnitCost: 1)])));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Stock.PostAsync(new StockDocumentInput(StockDocumentType.AdjustmentIn, October, Main, "", [new(led, 1, UnitCost: 1)])));
    }

    [Fact]
    public async Task Database_rejects_a_movement_for_another_companys_product()
    {
        var led = await ProductAsync("LED-18W");
        var receipt = await ReceiveAsync(led, 5, 100);
        var demo = (await Db.Workspace.GetCompaniesAsync()).Single(c => c.IsDemo).Id;

        using var connection = Db.OpenRawConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO StockMovements (Id, CompanyId, DocumentId, LineNumber, Sequence, ProductId, WarehouseId, State, Date, QuantityMilli, ValuePaise, IsInternal) " +
                              "VALUES ($id, $company, $doc, 1, 99, $product, $warehouse, 'Good', '2026-10-10', 1000, 0, 1)";
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("$company", demo.ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("$doc", receipt.DocumentId.ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("$product", led.ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("$warehouse", Main.ToString().ToUpperInvariant());
        Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
    }

    [Theory]
    [InlineData("UPDATE StockMovements SET QuantityMilli = QuantityMilli + 1000")]
    [InlineData("DELETE FROM StockMovements")]
    [InlineData("UPDATE StockDocuments SET Narration = 'edited'")]
    [InlineData("DELETE FROM StockDocuments")]
    public async Task Database_triggers_keep_the_stock_ledger_append_only(string sql)
    {
        var led = await ProductAsync("LED-18W");
        await ReceiveAsync(led, 5, 100);

        using var connection = Db.OpenRawConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        Assert.Contains("cannot be", Assert.Throws<SqliteException>(() => command.ExecuteNonQuery()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Data_context_refuses_to_change_stock_records()
    {
        var led = await ProductAsync("LED-18W");
        await ReceiveAsync(led, 5, 100);
        var factory = Db.Get<IDbContextFactory<NexoraDbContext>>();

        await using (var context = await factory.CreateDbContextAsync())
        {
            context.Remove(await context.StockMovements.FirstAsync());
            await Assert.ThrowsAsync<ImmutableRecordException>(() => context.SaveChangesAsync());
        }

        await using (var context = await factory.CreateDbContextAsync())
        {
            context.Remove(await context.StockDocuments.FirstAsync());
            await Assert.ThrowsAsync<ImmutableRecordException>(() => context.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Demonstration_company_with_stock_can_still_be_deleted()
    {
        var real = Db.Workspace.CurrentSession!.CompanyId;
        var demo = (await Db.Workspace.GetCompaniesAsync()).Single(c => c.IsDemo);
        await Db.Workspace.SwitchCompanyAsync(demo.Id);
        var warehouse = await Db.Get<CompanyService>().SaveWarehouseAsync(new WarehouseInput(null, "DW", "Demo store", null));
        var units = (await Catalogue.ListUnitsAsync()).ToDictionary(u => u.Code, u => u.Id);
        var product = await Catalogue.SaveProductAsync(new ProductInput(null, new ProductDetails("DEMO-1", "Demo"), units["PCS"], TrackingMode.None, false));
        await Stock.PostAsync(new StockDocumentInput(StockDocumentType.Opening, October, warehouse, "", [new(product, 3, UnitCost: 10)]));
        await Db.Workspace.SwitchCompanyAsync(real);

        await Db.Workspace.DeleteDemonstrationCompanyAsync(demo.Id);

        Assert.DoesNotContain(await Db.Workspace.GetCompaniesAsync(), c => c.IsDemo);
    }
}
