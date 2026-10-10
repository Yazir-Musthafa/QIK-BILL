using Microsoft.Data.Sqlite;
using Nexora.Application;
using Nexora.Application.Billing;
using Nexora.Application.Companies;
using Nexora.Application.Security;
using Nexora.Domain.Billing;
using Nexora.Domain.Numbering;

namespace Nexora.Tests.Billing;

public sealed class BillingAccessTests : BillingTestBase
{
    [Fact]
    public async Task Billing_operator_sells_within_limits_but_cannot_buy_cancel_or_see_cost()
    {
        var (invoice, _) = await SellAsync(Local, new BillingLineInput(Led, 1));
        await SignInAsAsync("bina", "Billing Operator");

        var (own, _) = await SellAsync(Local, new BillingLineInput(Led, 1, DiscountPercent: 5));
        Assert.Null((await Billing.GetAsync(own)).CostOfGoods);
        Assert.All((await Billing.GetAsync(own)).Lines, l => Assert.Null(l.Cost));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => SellAsync(Local, new BillingLineInput(Led, 1, DiscountPercent: 5.01m)));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => SellAsync(Local, new BillingLineInput(Led, 1, Rate: 140)));
        await Assert.ThrowsAsync<AccessDeniedException>(() => BuyAsync("X", new BillingLineInput(Led, 1)));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Billing.CancelAsync(invoice, October, "No"));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Parties.ListAsync(PartyKind.Supplier));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Billing.ListAsync(new BillingListFilter([BillingDocumentKind.PurchaseBill])));
    }

    [Fact]
    public async Task Price_and_discount_limits_are_checked_again_by_whoever_posts()
    {
        var draft = await DraftAsync(BillingDocumentKind.SalesInvoice, Local, new BillingLineInput(Led, 1, Rate: 120, DiscountPercent: 20));
        await SignInAsAsync("bina", "Billing Operator");

        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Billing.PostAsync(draft));

        await SignInAsAdministratorAsync();
        Assert.StartsWith("INV/", await Billing.PostAsync(draft), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_only_users_view_but_cannot_change_anything()
    {
        var (invoice, _) = await SellAsync(Local, new BillingLineInput(Led, 1));
        var draft = await DraftAsync(BillingDocumentKind.SalesInvoice, Local, new BillingLineInput(Led, 1));
        await SignInAsAsync("ravi", "Read-only");

        Assert.Equal(2, (await Billing.ListAsync(new BillingListFilter([BillingDocumentKind.SalesInvoice]))).Count);
        await Assert.ThrowsAsync<AccessDeniedException>(() => Billing.PostAsync(draft));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Billing.DeleteDraftAsync(draft));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Parties.SaveAsync(new PartyInput(null, PartyKind.Customer, new PartyDetails("New"))));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Settlements.PostAsync(new SettlementInput(SettlementKind.CustomerReceipt, October, Local,
            CashAccount, 10, PaymentMethod.Cash, [new AllocationInput(invoice, 10)])));
    }

    [Fact]
    public async Task Inventory_manager_buys_but_cannot_see_sales()
    {
        await SignInAsAsync("ivan", "Inventory Manager");

        var (bill, _) = await BuyAsync("PW/1", new BillingLineInput(Led, 1));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Billing.ListAsync(new BillingListFilter([BillingDocumentKind.SalesInvoice])));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Billing.CancelAsync(bill, October, "Wrong supplier"));
    }
}

public sealed class BillingIsolationAndImmutabilityTests : BillingTestBase
{
    [Fact]
    public async Task Another_companys_documents_and_parties_are_out_of_reach()
    {
        var (invoice, _) = await SellAsync(Local, new BillingLineInput(Led, 1));
        var demo = (await Db.Workspace.GetCompaniesAsync()).Single(c => c.IsDemo);
        await Db.Workspace.SwitchCompanyAsync(demo.Id);

        Assert.Empty(await Parties.ListAsync(PartyKind.Customer));
        Assert.Empty(await Billing.ListAsync(new BillingListFilter(Enum.GetValues<BillingDocumentKind>())));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Billing.GetAsync(invoice));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Billing.CancelAsync(invoice, October, "x"));
        var warehouse = await Db.Get<CompanyService>().SaveWarehouseAsync(new WarehouseInput(null, "DW", "Demo store", null));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Billing.SaveDraftAsync(new BillingDraftInput(null, BillingDocumentKind.SalesInvoice,
            October, Local, warehouse, SettlementMode.Credit, [new BillingLineInput(Led, 1)])));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Settlements.PostAsync(new SettlementInput(SettlementKind.CustomerReceipt, October, Local,
            CashAccount, 10, PaymentMethod.Cash, [])));
    }

    [Fact]
    public async Task Database_refuses_to_change_posted_documents_lines_and_settlements()
    {
        var (invoice, _) = await SellAsync(Local, new BillingLineInput(Led, 1));
        await Settlements.PostAsync(new SettlementInput(SettlementKind.CustomerReceipt, October, Local, CashAccount, 100, PaymentMethod.Cash, [new AllocationInput(invoice, 100)]));
        var draft = await DraftAsync(BillingDocumentKind.SalesInvoice, Local, new BillingLineInput(Led, 1));

        using var connection = Db.OpenRawConnection();
        foreach (var sql in new[]
                 {
                     "UPDATE BillingDocuments SET TotalPaise = 1 WHERE Status = 'Posted'",
                     "DELETE FROM BillingDocuments WHERE Status = 'Posted'",
                     "UPDATE BillingLines SET RatePaise = 1 WHERE IsLocked = 1",
                     "UPDATE Settlements SET AmountPaise = 1",
                     "DELETE FROM Allocations",
                 })
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
        }

        using var edit = connection.CreateCommand();
        edit.CommandText = $"UPDATE BillingDocuments SET Narration = 'changed' WHERE Id = '{draft.ToString().ToUpperInvariant()}'";
        Assert.Equal(1, edit.ExecuteNonQuery());
    }

    [Fact]
    public async Task New_companies_get_billing_settings_and_numbering()
    {
        var numbering = await Db.Get<CompanyService>().ListNumberingAsync();

        foreach (var type in BillingDocumentTypes.All)
            Assert.Contains(numbering, r => r.DocumentType == type.Key);
        Assert.Contains(numbering, r => r.DocumentType == DocumentTypes.TaxInvoice && r.Example == "INV/2026-27/0001");
        Assert.Equal(new BillingSettingsView(true, false), await Billing.GetSettingsAsync());
    }

    /// <summary>A Phase 4 database has no billing settings or billing numbering; starting the new version adds them once.</summary>
    [Fact]
    public async Task Existing_companies_receive_billing_settings_and_numbering_on_upgrade()
    {
        using (var connection = Db.OpenRawConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM BillingSettings; DELETE FROM NumberingRules WHERE DocumentType IN ("
                + string.Join(", ", BillingDocumentTypes.All.Select(t => $"'{t.Key}'")) + ");";
            command.ExecuteNonQuery();
        }

        await Db.Workspace.InitializeAsync();
        await Db.Workspace.InitializeAsync();
        await SignInAsAdministratorAsync();

        var numbering = await Db.Get<CompanyService>().ListNumberingAsync();
        foreach (var type in BillingDocumentTypes.All)
            Assert.Single(numbering, r => r.DocumentType == type.Key);
        Assert.Equal(new BillingSettingsView(true, false), await Billing.GetSettingsAsync());
        var (invoice, _) = await SellAsync(Local, new BillingLineInput(Led, 1));
        Assert.Equal("CRN/2026-27/0001", (await ReturnAsync(invoice, (1, 1))).Number);
    }
}
