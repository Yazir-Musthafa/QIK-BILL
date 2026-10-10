using Nexora.Application;
using Nexora.Application.Companies;
using Nexora.Application.Security;
using Nexora.Application.Users;
using Nexora.Domain.Companies;
using Nexora.Domain.Security;
using Nexora.Tests.Infrastructure;
using Nexora.Tests.Workspace;

namespace Nexora.Tests.Security;

/// <summary>Permissions are enforced by Application services, independent of what the UI shows.</summary>
public sealed class PermissionTests : IAsyncLifetime
{
    private const string AdminPassword = "quiet ledger 42";
    private const string Temporary = "temporary pass 1";
    private const string Chosen = "my own phrase 9";
    private readonly SqliteTestDatabase _db = new();

    private IWorkspaceService Workspace => _db.Workspace;

    private UserService Users => _db.Get<UserService>();

    private RoleService Roles => _db.Get<RoleService>();

    private CompanyService Companies => _db.Get<CompanyService>();

    public async Task InitializeAsync()
    {
        await Workspace.InitializeAsync();
        await Workspace.CompleteSetupAsync(WorkspaceServiceTests.ValidRequest());
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task New_user_must_change_temporary_password_before_anything_else()
    {
        await CreateUserAsync("ravi", "Read-only");
        Workspace.SignOut();

        var result = await Workspace.SignInAsync("ravi", Temporary);
        Assert.True(result.Succeeded);
        Assert.True(result.Session!.MustChangePassword);
        var blocked = await Assert.ThrowsAsync<AccessDeniedException>(() => Companies.ListBranchesAsync());
        Assert.Contains("Change your password", blocked.Message, StringComparison.Ordinal);

        await Workspace.ChangePasswordAsync(Temporary, Chosen);
        Assert.Empty(await Companies.ListBranchesAsync());
        Workspace.SignOut();
        Assert.False((await Workspace.SignInAsync("ravi", Temporary)).Succeeded);
        Assert.True((await Workspace.SignInAsync("ravi", Chosen)).Succeeded);
    }

    [Fact]
    public async Task Read_only_user_can_view_but_not_change()
    {
        await SignInAsNewUserAsync("ravi", "Read-only");

        await Companies.GetDetailsAsync();
        await Users.ListAsync();
        await Assert.ThrowsAsync<AccessDeniedException>(() => Companies.SaveBranchAsync(new BranchInput(null, "B1", "Branch", "27", "")));
        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            Companies.UpdateProfileAsync(new CompanyProfile("Renamed", "", "27", "", "", "", "", "", "", "", "")));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Users.CreateAsync(new NewUserRequest("eve", "Eve", Temporary, Guid.NewGuid())));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Roles.SaveAsync(new RoleInput(null, "Mine", "", [], 0)));
        await Assert.ThrowsAsync<AccessDeniedException>(() => _db.Get<Nexora.Application.Audit.AuditService>().VerifyAsync());
        await Assert.ThrowsAsync<AccessDeniedException>(() => Companies.CreateCompanyAsync(new NewCompanyRequest("Other", "27", 2026)));
    }

    [Fact]
    public async Task Accountant_cannot_manage_users_or_company()
    {
        await SignInAsNewUserAsync("anu", "Accountant");

        await Assert.ThrowsAsync<AccessDeniedException>(() => Users.ListAsync());
        await Assert.ThrowsAsync<AccessDeniedException>(() => Companies.SaveBranchAsync(new BranchInput(null, "B1", "Branch", "27", "")));
        Assert.True(Workspace.CurrentSession!.Has(Permissions.VouchersPost));
    }

    [Fact]
    public async Task Role_managers_cannot_grant_or_assign_more_than_they_hold()
    {
        await Roles.SaveAsync(new RoleInput(null, "User manager", "", [Permissions.UsersView, Permissions.UsersManage, Permissions.RolesManage, Permissions.CompanyView], 0));
        await SignInAsNewUserAsync("uma", "User manager");

        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            Roles.SaveAsync(new RoleInput(null, "Sneaky", "", [Permissions.AccountsManage], 0)));
        var admin = (await Roles.ListAsync()).Single(r => r.IsAdministrator);
        await Assert.ThrowsAsync<AccessDeniedException>(() => Users.CreateAsync(new NewUserRequest("max", "Max", Temporary, admin.Id)));
        var self = (await Users.ListAsync()).Single(u => u.Username == "uma");
        await Assert.ThrowsAsync<AccessDeniedException>(() => Users.ChangeRoleAsync(self.MembershipId, admin.Id));

        // Within their own permissions they can work normally.
        await Roles.SaveAsync(new RoleInput(null, "Viewer", "", [Permissions.CompanyView], 0));
    }

    [Fact]
    public async Task Every_company_keeps_an_active_administrator()
    {
        var me = (await Users.ListAsync()).Single();
        var accountant = (await Roles.ListAsync()).Single(r => r.Name == "Accountant");

        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Users.ChangeRoleAsync(me.MembershipId, accountant.Id));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Users.SetActiveAsync(me.MembershipId, false));
    }

    [Fact]
    public async Task Deactivated_user_cannot_sign_in()
    {
        await CreateUserAsync("ravi", "Read-only");
        var ravi = (await Users.ListAsync()).Single(u => u.Username == "ravi");
        await Users.SetActiveAsync(ravi.MembershipId, false);
        Workspace.SignOut();

        var result = await Workspace.SignInAsync("ravi", Temporary);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkspaceService.NoAccessError, result.Error);
    }

    [Fact]
    public async Task Editing_a_role_changes_what_its_members_can_do()
    {
        await SignInAsNewUserAsync("ravi", "Read-only");
        await Companies.ListBranchesAsync();
        Workspace.SignOut();

        await Workspace.SignInAsync("asha", AdminPassword);
        var readOnly = (await Roles.ListAsync()).Single(r => r.Name == "Read-only");
        await Roles.SaveAsync(new RoleInput(readOnly.Id, readOnly.Name, readOnly.Description,
            readOnly.Permissions.Where(p => p != Permissions.CompanyView).ToList(), 0));
        Workspace.SignOut();

        await Workspace.SignInAsync("ravi", Chosen);
        await Assert.ThrowsAsync<AccessDeniedException>(() => Companies.ListBranchesAsync());
    }

    [Fact]
    public async Task Password_reset_is_refused_for_users_who_also_work_in_a_company_the_caller_does_not_manage()
    {
        // Asha creates a second company and gives Ravi access to both; then Ravi's admin rights in the second are irrelevant:
        // a third admin who manages only the first company must not be able to reset Ravi's password.
        var ravi = await CreateUserAsync("ravi", "Read-only");
        var second = await Companies.CreateCompanyAsync(new NewCompanyRequest("Second Co", "27", 2026));
        await Workspace.SwitchCompanyAsync(second.Id);
        var secondReadOnly = (await Roles.ListAsync()).Single(r => r.Name == "Read-only");
        await Users.AddExistingAsync("ravi", secondReadOnly.Id);
        await Workspace.SwitchCompanyAsync((await Workspace.GetCompaniesAsync()).Single(c => c.Name == "Acme Traders").Id);

        await CreateUserAsync("omar", "Administrator");
        Workspace.SignOut();
        await Workspace.SignInAsync("omar", Temporary);
        await Workspace.ChangePasswordAsync(Temporary, Chosen);

        await Assert.ThrowsAsync<AccessDeniedException>(() => Users.ResetPasswordAsync(ravi, "another temp 22"));
    }

    [Fact]
    public async Task Admin_can_reset_password_and_user_must_change_it()
    {
        var ravi = await CreateUserAsync("ravi", "Read-only");
        await Users.ResetPasswordAsync(ravi, "another temp 22");
        Workspace.SignOut();

        var result = await Workspace.SignInAsync("ravi", "another temp 22");
        Assert.True(result.Session!.MustChangePassword);
    }

    [Fact]
    public async Task Usernames_are_unique_across_companies()
    {
        await CreateUserAsync("ravi", "Read-only");
        var role = (await Roles.ListAsync()).Single(r => r.Name == "Accountant");
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Users.CreateAsync(new NewUserRequest("RAVI", "Ravi 2", Temporary, role.Id)));
    }

    [Fact]
    public async Task Built_in_roles_cannot_be_deleted_and_used_roles_are_protected()
    {
        var roles = await Roles.ListAsync();
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Roles.DeleteAsync(roles.Single(r => r.Name == "Accountant").Id));

        var custom = await Roles.SaveAsync(new RoleInput(null, "Custom", "", [Permissions.CompanyView], 0));
        await CreateUserAsync("ravi", "Custom");
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Roles.DeleteAsync(custom));
    }

    private async Task<Guid> CreateUserAsync(string username, string roleName)
    {
        var role = (await Roles.ListAsync()).Single(r => r.Name == roleName);
        return await Users.CreateAsync(new NewUserRequest(username, username.ToUpperInvariant(), Temporary, role.Id));
    }

    private async Task SignInAsNewUserAsync(string username, string roleName)
    {
        await CreateUserAsync(username, roleName);
        Workspace.SignOut();
        Assert.True((await Workspace.SignInAsync(username, Temporary)).Succeeded);
        await Workspace.ChangePasswordAsync(Temporary, Chosen);
    }
}
