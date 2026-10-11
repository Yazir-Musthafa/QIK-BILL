using Nexora.Accounting;
using Nexora.Application.Billing;
using Nexora.Application.Inventory;
using Nexora.Application.Users;
using Nexora.Domain.Billing;
using Nexora.Domain.Companies;
using Nexora.Domain.Inventory;
using Nexora.Tests.Inventory;

namespace Nexora.Tests.Billing;

/// <summary>
/// A Maharashtra (27) company with stock, a local customer, an inter-state (Karnataka, 29) customer and a local supplier.
/// LED-18W is taxed at the seeded "GST 18%" category (rate entered as data), sells at 150 and costs 100.
/// </summary>
public abstract class BillingTestBase : StockTestBase
{
    protected PartyService Parties => Db.Get<PartyService>();

    protected BillingService Billing => Db.Get<BillingService>();

    protected SettlementService Settlements => Db.Get<SettlementService>();

    protected Guid Gst18 { get; private set; }

    protected Guid Led { get; private set; }

    protected Guid Local { get; private set; }

    protected Guid Bengaluru { get; private set; }

    protected Guid Vendor { get; private set; }

    protected Guid CashAccount => Accounts[SystemAccounts.Cash];

    /// <summary>Chosen before any stock moves (the method cannot change afterwards).</summary>
    protected virtual CostingMethod Costing => CostingMethod.WeightedAverage;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        if (Costing != CostingMethod.WeightedAverage) await Catalogue.SaveSettingsAsync(Costing, allowNegativeStock: false);
        Gst18 = (await Catalogue.ListTaxCategoriesAsync()).Single(t => t.ReferenceRatePercent == 18).Id;
        Led = await TaxedProductAsync("LED-18W", Gst18);
        await ReceiveAsync(Led, 20, 100);
        Local = await Parties.SaveAsync(new PartyInput(null, PartyKind.Customer, new PartyDetails("Meera Electricals", Phone: "9820012345", StateCode: "27")));
        Bengaluru = await Parties.SaveAsync(new PartyInput(null, PartyKind.Customer, new PartyDetails("Kaveri Lights", Gstin: Gstin("29", "AAACK1234A"))));
        Vendor = await Parties.SaveAsync(new PartyInput(null, PartyKind.Supplier, new PartyDetails("Pune Wholesale", Gstin: Gstin("27", "AABCP5678B"))));
    }

    /// <summary>A valid GSTIN for the state and PAN (entity 1, Z, computed check character).</summary>
    public static string Gstin(string state, string pan)
    {
        var body = state + pan + "1Z";
        return body + TaxIdentifiers.GstinCheckCharacter(body + "0");
    }

    protected Task<Guid> TaxedProductAsync(string sku, Guid? taxCategory, TrackingMode tracking = TrackingMode.None, decimal selling = 150, decimal purchase = 90) =>
        Catalogue.SaveProductAsync(new ProductInput(null, new ProductDetails(sku, sku + " item", HsnCode: "9405", TaxCategoryId: taxCategory,
            PurchasePrice: purchase, SellingPrice: selling), Units["PCS"], tracking, false));

    protected Task<Guid> DraftAsync(BillingDocumentKind kind, Guid? party, params BillingLineInput[] lines) =>
        Billing.SaveDraftAsync(new BillingDraftInput(null, kind, October, party, Main, SettlementMode.Credit, lines));

    protected async Task<(Guid Id, string Number)> SellAsync(Guid? customer, params BillingLineInput[] lines)
    {
        var id = await DraftAsync(BillingDocumentKind.SalesInvoice, customer, lines);
        return (id, await Billing.PostAsync(id));
    }

    protected async Task<(Guid Id, string Number)> CashSaleAsync(params BillingLineInput[] lines)
    {
        var id = await Billing.SaveDraftAsync(new BillingDraftInput(null, BillingDocumentKind.SalesInvoice, October, null, Main, SettlementMode.Cash, lines,
            CashAccountId: CashAccount, WalkInName: "Walk-in"));
        return (id, await Billing.PostAsync(id));
    }

    protected async Task<(Guid Id, string Number)> BuyAsync(string reference, params BillingLineInput[] lines)
    {
        var id = await Billing.SaveDraftAsync(new BillingDraftInput(null, BillingDocumentKind.PurchaseBill, October, Vendor, Main, SettlementMode.Credit, lines,
            Reference: reference));
        return (id, await Billing.PostAsync(id));
    }

    protected async Task<(Guid Id, string Number)> ReturnAsync(Guid originalId, params (int Line, decimal Quantity)[] lines)
    {
        var original = await Billing.GetAsync(originalId);
        var kind = original.Kind == BillingDocumentKind.SalesInvoice ? BillingDocumentKind.SalesReturn : BillingDocumentKind.PurchaseReturn;
        var inputs = lines.Select(l =>
        {
            var source = original.Lines.Single(x => x.LineNumber == l.Line);
            return new BillingLineInput(source.ProductId ?? Guid.Empty, l.Quantity, source.UnitId, OriginalLineId: source.Id, SerialNumber: source.SerialNumber, BatchId: source.BatchId);
        }).ToArray();
        var id = await Billing.SaveDraftAsync(new BillingDraftInput(null, kind, October, original.PartyId, Main, original.Mode, inputs,
            CashAccountId: original.CashAccountId, OriginalDocumentId: originalId));
        return (id, await Billing.PostAsync(id));
    }

    protected async Task<decimal> PartyLedgerAsync(Guid partyId) => (await Parties.GetAsync(partyId)).LedgerBalance;

    protected async Task AssertBooksBalanceAsync()
    {
        var trial = await Db.Get<LedgerService>().TrialBalanceAsync(October);
        Assert.Equal(0m, trial.Rows.Sum(r => r.Net));
        Assert.True((await Stock.ReconcileAsync(October)).IsReconciled);
    }

    protected async Task SignInAsAsync(string username, string role)
    {
        var roleId = (await Db.Get<RoleService>().ListAsync()).Single(r => r.Name == role).Id;
        await Db.Get<UserService>().CreateAsync(new NewUserRequest(username, username, "temporary pass 1", roleId));
        Db.Workspace.SignOut();
        await Db.Workspace.SignInAsync(username, "temporary pass 1");
        await Db.Workspace.ChangePasswordAsync("temporary pass 1", "my own phrase 9");
    }

    protected async Task SignInAsAdministratorAsync()
    {
        Db.Workspace.SignOut();
        await Db.Workspace.SignInAsync("asha", "quiet ledger 42");
    }
}
