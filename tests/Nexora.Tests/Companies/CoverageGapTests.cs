using Nexora.Accounting;
using Nexora.Application;
using Nexora.Application.Companies;
using Nexora.Application.Users;
using Nexora.Domain.Accounting;
using Nexora.Domain.Common;
using Nexora.Domain.Numbering;
using Nexora.Tests.Accounting;
using Nexora.Tests.Infrastructure;
using Nexora.Tests.Workspace;

namespace Nexora.Tests.Companies;

/// <summary>
/// Success paths that the working-guide audit (10 October 2026) found had no automated tests:
/// chart-of-accounts editing, warehouses, branch deactivation, numbering edits, logo upload and role changes.
/// </summary>
public sealed class CoverageGapTests : IAsyncLifetime
{
    private static readonly DateOnly October = new(2026, 10, 10);
    private readonly SqliteTestDatabase _db = new();

    private CompanyService Companies => _db.Get<CompanyService>();

    private ChartOfAccountsService Chart => _db.Get<ChartOfAccountsService>();

    public async Task InitializeAsync()
    {
        await _db.Workspace.InitializeAsync();
        await _db.Workspace.CompleteSetupAsync(WorkspaceServiceTests.ValidRequest());
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private async Task<AccountRow> AccountAsync(string code) => (await Chart.ListAsync()).Single(a => a.Code == code);

    [Fact]
    public async Task Chart_creates_a_ledger_under_a_group_with_the_groups_nature()
    {
        var debtors = await AccountAsync("1130");
        var id = await Chart.SaveAsync(new AccountInput(null, "s-001", " Sharma Traders ", debtors.Id, AccountKind.Receivable));

        var created = (await Chart.ListAsync()).Single(a => a.Id == id);
        Assert.Equal("S-001", created.Code);
        Assert.Equal("Sharma Traders", created.Name);
        Assert.Equal(AccountNature.Asset, created.Nature);
        Assert.Equal(debtors.Id, created.ParentId);
        Assert.Equal(debtors.Depth + 1, created.Depth);
    }

    [Fact]
    public async Task Chart_creates_groups_and_refuses_duplicate_codes_and_names()
    {
        var indirect = await AccountAsync("5300");
        var group = await Chart.SaveAsync(new AccountInput(null, "5400", "Marketing", indirect.Id, AccountKind.Group));
        Assert.True((await Chart.ListAsync()).Single(a => a.Id == group).IsGroup);

        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Chart.SaveAsync(new AccountInput(null, "5305", "Other rent", indirect.Id, AccountKind.General)));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Chart.SaveAsync(new AccountInput(null, "5999", "Rent", indirect.Id, AccountKind.General)));
        var rentLedger = await AccountAsync("5305");
        await Assert.ThrowsAsync<WorkspaceValidationException>(() =>
            Chart.SaveAsync(new AccountInput(null, "5998", "Under a ledger", rentLedger.Id, AccountKind.General)));
    }

    [Fact]
    public async Task Chart_renames_and_moves_within_the_same_nature_only()
    {
        var rent = await AccountAsync("5305");
        var direct = await AccountAsync("5200");
        await Chart.SaveAsync(new AccountInput(rent.Id, "5305", "Shop rent", direct.Id, AccountKind.General));
        var moved = await AccountAsync("5305");
        Assert.Equal("Shop rent", moved.Name);
        Assert.Equal(direct.Id, moved.ParentId);

        var assets = await AccountAsync("1100");
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Chart.SaveAsync(new AccountInput(rent.Id, "5305", "Shop rent", assets.Id, AccountKind.General)));

        var expenses = await AccountAsync("5000");
        var indirect = await AccountAsync("5300");
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Chart.SaveAsync(new AccountInput(expenses.Id, "5000", "Expenses", indirect.Id, AccountKind.Group)));
    }

    [Fact]
    public async Task Chart_refuses_to_change_the_type_of_an_account_with_postings()
    {
        var accounts = await PostingTests.LoadAccountsAsync(_db);
        await _db.Get<VoucherService>().SaveAndPostAsync(new VoucherInput(null, VoucherType.Journal, October, "Rent",
            [new(accounts["5305"], 100m, 0), new(accounts[SystemAccounts.Capital], 0, 100m)]));
        var rent = await AccountAsync("5305");

        await Assert.ThrowsAsync<WorkspaceValidationException>(() =>
            Chart.SaveAsync(new AccountInput(rent.Id, rent.Code, rent.Name, rent.ParentId!.Value, AccountKind.Bank)));
    }

    [Fact]
    public async Task Warehouses_are_created_listed_with_their_branch_and_deactivated()
    {
        var branch = await Companies.SaveBranchAsync(new BranchInput(null, "PNQ", "Pune branch", "27", "MG Road"));
        var warehouse = await Companies.SaveWarehouseAsync(new WarehouseInput(null, "w1", "Main store", branch));

        var row = Assert.Single(await Companies.ListWarehousesAsync());
        Assert.Equal(("W1", "Main store", "Pune branch", true), (row.Code, row.Name, row.BranchName, row.IsActive));

        await Companies.SaveWarehouseAsync(new WarehouseInput(warehouse, "W1", "Main store", null));
        await Companies.SetWarehouseActiveAsync(warehouse, false);
        row = Assert.Single(await Companies.ListWarehousesAsync());
        Assert.Equal(string.Empty, row.BranchName);
        Assert.False(row.IsActive);

        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Companies.SaveWarehouseAsync(new WarehouseInput(null, "W1", "Duplicate", null)));
    }

    [Fact]
    public async Task Branches_can_be_deactivated_and_reactivated()
    {
        var branch = await Companies.SaveBranchAsync(new BranchInput(null, "HO", "Head office", "27", ""));

        await Companies.SetBranchActiveAsync(branch, false);
        Assert.False(Assert.Single(await Companies.ListBranchesAsync()).IsActive);
        await Companies.SetBranchActiveAsync(branch, true);
        Assert.True(Assert.Single(await Companies.ListBranchesAsync()).IsActive);
    }

    [Fact]
    public async Task Numbering_changes_apply_to_the_next_posted_voucher()
    {
        var journal = (await Companies.ListNumberingAsync()).Single(r => r.DocumentType == DocumentTypes.JournalVoucher);
        Assert.Equal("JV/2026-27/0001", journal.Example);
        var accounts = await PostingTests.LoadAccountsAsync(_db);
        VoucherInput Rent() => new(null, VoucherType.Journal, October, "Rent", [new(accounts["5305"], 10m, 0), new(accounts[SystemAccounts.Capital], 0, 10m)]);
        Assert.Equal("JV/2026-27/0001", (await _db.Get<VoucherService>().SaveAndPostAsync(Rent())).Number);

        await Companies.UpdateNumberingAsync(journal.Id, "JRN/{FY}/", 3, resetEachFinancialYear: true);

        Assert.Equal("JRN/2026-27/002", (await _db.Get<VoucherService>().SaveAndPostAsync(Rent())).Number);
        await Assert.ThrowsAsync<DomainException>(() => Companies.UpdateNumberingAsync(journal.Id, "JRN", 0, true));
    }

    [Fact]
    public async Task Logo_is_stored_validated_and_removed()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01];

        await Companies.SetLogoAsync(png);
        Assert.True((await Companies.GetDetailsAsync()).HasLogo);

        await Assert.ThrowsAsync<DomainException>(() => Companies.SetLogoAsync([0x47, 0x49, 0x46, 0x38, 0x39]));
        await Assert.ThrowsAsync<DomainException>(() => Companies.SetLogoAsync(new byte[2 * 1024 * 1024]));
        Assert.True((await Companies.GetDetailsAsync()).HasLogo);

        await Companies.SetLogoAsync(null);
        Assert.False((await Companies.GetDetailsAsync()).HasLogo);
    }

    [Fact]
    public async Task Changing_a_users_role_applies_and_refreshes_your_own_session()
    {
        var roles = await _db.Get<RoleService>().ListAsync();
        var users = _db.Get<UserService>();
        var ravi = await users.CreateAsync(new NewUserRequest("ravi", "Ravi", "temporary pass 1", roles.Single(r => r.Name == "Read-only").Id));
        var membership = (await users.ListAsync()).Single(u => u.UserId == ravi).MembershipId;

        await users.ChangeRoleAsync(membership, roles.Single(r => r.Name == "Accountant").Id);
        Assert.Equal("Accountant", (await users.ListAsync()).Single(u => u.UserId == ravi).RoleName);

        // With a second administrator in place, Asha may give up her own administrator role; her session follows at once.
        await users.ChangeRoleAsync(membership, roles.Single(r => r.IsAdministrator).Id);
        var me = (await users.ListAsync()).Single(u => u.Username == "asha");
        await users.ChangeRoleAsync(me.MembershipId, roles.Single(r => r.Name == "Accountant").Id);

        Assert.Equal("Accountant", _db.Workspace.CurrentSession!.RoleName);
        Assert.False(_db.Workspace.CurrentSession.Has(Nexora.Domain.Security.Permissions.UsersManage));
    }
}
