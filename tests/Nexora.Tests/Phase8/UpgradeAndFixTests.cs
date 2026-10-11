using Nexora.Accounting;
using Nexora.Application.Billing;
using Nexora.Application.Users;
using Nexora.Domain.Billing;
using Nexora.Domain.Security;
using Nexora.Tests.Billing;

namespace Nexora.Tests.Phase8;

public sealed class Phase8UpgradeTests : BillingTestBase
{
    /// <summary>Accountant roles created before Phase 7 lack expenses.approve; the first start on Phase 8 adds it once, and only to that role.</summary>
    [Fact]
    public async Task Older_accountant_roles_receive_expense_approval_once()
    {
        using (var connection = Db.OpenRawConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE Roles SET Permissions = REPLACE(Permissions, 'expenses.approve', '') WHERE SystemKey = 'accountant'; DELETE FROM ReportSettings;";
            command.ExecuteNonQuery();
        }

        await SignInAsAdministratorAsync();
        Assert.DoesNotContain(Permissions.ExpensesApprove, (await Roles()).Single(r => r.Name == "Accountant").Permissions);

        await Db.Workspace.InitializeAsync();
        await SignInAsAdministratorAsync();

        var roles = await Roles();
        Assert.Contains(Permissions.ExpensesApprove, roles.Single(r => r.Name == "Accountant").Permissions);
        Assert.DoesNotContain(Permissions.ExpensesApprove, roles.Single(r => r.Name == "Billing Operator").Permissions);

        // Once upgraded, an administrator's later decision to remove it is respected on the next start.
        var accountant = roles.Single(r => r.Name == "Accountant");
        await Db.Get<RoleService>().SaveAsync(new RoleInput(accountant.Id, accountant.Name, accountant.Description,
            accountant.Permissions.Where(p => p != Permissions.ExpensesApprove).ToList(), accountant.MaxDiscountPercent));
        await Db.Workspace.InitializeAsync();
        await SignInAsAdministratorAsync();
        Assert.DoesNotContain(Permissions.ExpensesApprove, (await Roles()).Single(r => r.Name == "Accountant").Permissions);
    }

    private Task<IReadOnlyList<RoleRow>> Roles() => Db.Get<RoleService>().ListAsync();
}

public sealed class ReturnRoundOffTests : BillingTestBase
{
    /// <summary>A rounded invoice returned in three parts: the credit notes add up exactly to the invoice, and the last one takes the round-off difference.</summary>
    [Fact]
    public async Task Returns_in_several_parts_add_up_to_the_rounded_invoice()
    {
        var (invoiceId, _) = await SellAsync(Local, new BillingLineInput(Led, 3, Rate: 149.5m));
        var invoice = await Billing.GetAsync(invoiceId);
        Assert.Equal((448.50m, 80.74m, 529m), (invoice.Taxable, invoice.Cgst + invoice.Sgst, invoice.Total));

        var totals = new List<decimal>();
        for (var i = 0; i < 3; i++)
        {
            var (returnId, _) = await ReturnAsync(invoiceId, (1, 1));
            totals.Add((await Billing.GetAsync(returnId)).Total);
        }

        Assert.Equal([176m, 176m, 177m], totals);
        Assert.Equal(invoice.Total, totals.Sum());
        Assert.Equal(0m, (await Parties.ListAsync(PartyKind.Customer)).Single(p => p.Id == Local).LedgerBalance);
        await AssertBooksBalanceAsync();
    }

    [Fact]
    public async Task A_partial_return_keeps_its_own_rounding()
    {
        var (invoiceId, _) = await SellAsync(Local, new BillingLineInput(Led, 3, Rate: 149.5m));
        var (returnId, _) = await ReturnAsync(invoiceId, (1, 2));
        Assert.Equal(353m, (await Billing.GetAsync(returnId)).Total);   // 299.00 + 53.82 = 352.82, rounded
    }
}
