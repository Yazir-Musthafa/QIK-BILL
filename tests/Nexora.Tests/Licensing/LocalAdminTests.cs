using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nexora.Application;
using Nexora.Application.Licensing;
using Nexora.Application.Security;
using Nexora.Infrastructure.Licensing;

namespace Nexora.Tests.Licensing;

public sealed class LocalAdminTests
{
    [Fact]
    public async Task Every_admin_operation_demands_permission_before_read_or_write()
    {
        await using var f = new LocalActivationFixture(); await f.InitializeAsync(); await f.RegisterAsync();
        await Assert.ThrowsAsync<AccessDeniedException>(() => f.Service.StatusAsync());
        await Assert.ThrowsAsync<AccessDeniedException>(() => f.Service.HistoryAsync());
        await Assert.ThrowsAsync<AccessDeniedException>(() => f.Service.CustomersAsync());
        await Assert.ThrowsAsync<AccessDeniedException>(() => f.Service.ChangeAsync("Active", "Paid"));
        Assert.Empty(await f.Store.HistoryAsync());
    }
    [Fact]
    public async Task Admin_signin_opens_only_admin_permissions_not_company_session()
    {
        await using var f = new LocalActivationFixture(); await f.InitializeAsync();
        Assert.False(await f.Admin.SignInAsync("customer", LocalActivationFixture.AdminPassword));
        Assert.False(await f.Admin.SignInAsync("admin", "wrong"));
        Assert.True(await f.Admin.SignInAsync(" Admin ", LocalActivationFixture.AdminPassword));
        Assert.True(f.Admin.Has(LocalAdministrator.ManagePermission));
        Assert.False(f.Admin.Has("company.manage"));
        Assert.Throws<AccessDeniedException>(() => f.Admin.Demand("company.manage"));
        Assert.Null(f.Workspace.Workspace.CurrentSession);
    }
    [Fact]
    public async Task Company_administrator_cannot_manage_activation_without_separate_admin_signin()
    {
        await using var f = new LocalActivationFixture(); await f.InitializeAsync(); await f.RegisterAsync(); await f.ActivateAsync();
        await f.Workspace.Workspace.SignInAsync("asha", "quiet ledger 42");
        await Assert.ThrowsAsync<AccessDeniedException>(() => f.Service.ChangeAsync("Suspended", "Unauthorized"));
        Assert.True(f.Store.IsActive);
    }
    [Fact]
    public async Task Five_bad_passwords_lock_admin_across_restarts_for_fifteen_minutes()
    {
        await using var f = new LocalActivationFixture(); await f.InitializeAsync();
        for (var i = 0; i < 5; i++) Assert.False(await f.Admin.SignInAsync("admin", "wrong"));
        var restarted = new LocalAdministrator(new LocalActivationStore(f.DatabasePath, f.Hasher, f.Time, f.Hash), f.Time);
        Assert.False(await restarted.SignInAsync("admin", LocalActivationFixture.AdminPassword));
        f.Time.Advance(TimeSpan.FromMinutes(14));
        Assert.False(await restarted.SignInAsync("admin", LocalActivationFixture.AdminPassword));
        f.Time.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await restarted.SignInAsync("admin", LocalActivationFixture.AdminPassword));
    }
    [Fact]
    public async Task Admin_signout_idle_timeout_and_failed_reauthentication_revoke_permission()
    {
        await using var f = new LocalActivationFixture(); await f.InitializeAsync();
        await f.Admin.SignInAsync("admin", LocalActivationFixture.AdminPassword);
        f.Time.Advance(TimeSpan.FromMinutes(15)); f.Admin.RecordActivity();
        Assert.False(f.Admin.IsSignedIn); await Assert.ThrowsAsync<AccessDeniedException>(() => f.Service.StatusAsync());
        await f.Admin.SignInAsync("admin", LocalActivationFixture.AdminPassword); f.Admin.SignOut();
        Assert.False(f.Admin.IsSignedIn);
        await f.Admin.SignInAsync("admin", LocalActivationFixture.AdminPassword);
        Assert.False(await f.Admin.SignInAsync("admin", "wrong")); Assert.False(f.Admin.IsSignedIn);
    }
    [Theory]
    [InlineData("Pending", "reason")]
    [InlineData("Delete", "reason")]
    [InlineData("Active", "")]
    [InlineData("Active", "  ")]
    public async Task Invalid_changes_leave_status_and_history_unchanged(string status, string note)
    {
        await using var f = new LocalActivationFixture(); await f.InitializeAsync(); await f.RegisterAsync();
        await f.Admin.SignInAsync("admin", LocalActivationFixture.AdminPassword);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => f.Service.ChangeAsync(status, note));
        Assert.Equal("Pending", (await f.Store.GetAsync()).Status); Assert.Empty(await f.Store.HistoryAsync());
    }
    [Fact]
    public async Task Long_reason_and_activation_before_registration_are_rejected()
    {
        await using var f = new LocalActivationFixture(); await f.InitializeAsync();
        await f.Admin.SignInAsync("admin", LocalActivationFixture.AdminPassword);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => f.Service.ChangeAsync("Active", "Paid"));
        await f.RegisterAsync();
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => f.Service.ChangeAsync("Active", new string('x', 501)));
        Assert.False(f.Store.IsActive); Assert.Empty(await f.Store.HistoryAsync());
    }
    [Fact]
    public async Task Admin_lists_real_accounts_and_activation_changes_have_append_only_history()
    {
        await using var f = new LocalActivationFixture(); await f.InitializeAsync(); await f.RegisterAsync();
        await f.Admin.SignInAsync("admin", LocalActivationFixture.AdminPassword);
        Assert.Equal("asha", Assert.Single(await f.Service.CustomersAsync()).Username);
        await f.Service.ChangeAsync("Active", "  Paid in cash  "); await f.Service.ChangeAsync("Suspended", "Overdue");
        var history = await f.Service.HistoryAsync(); Assert.Equal(2, history.Count);
        Assert.Equal("Suspend", history[0].Action); Assert.Equal("admin", history[0].Actor);
        Assert.Contains("Paid in cash", history[1].Note);
        await using var db = f.Open();
        var row = await db.History.FirstAsync(); row.Note = "altered";
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => db.SaveChangesAsync());
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("UPDATE History SET Note = 'altered'"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM History"));
    }
    [Fact]
    public async Task Status_and_audit_save_atomically_when_audit_insert_fails()
    {
        await using var f = new LocalActivationFixture(); await f.InitializeAsync(); await f.RegisterAsync();
        await f.Admin.SignInAsync("admin", LocalActivationFixture.AdminPassword);
        await using var db = f.Open();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailAudit BEFORE INSERT ON History BEGIN SELECT RAISE(ABORT, 'Test write failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Service.ChangeAsync("Active", "Paid"));
        Assert.False(f.Store.IsActive); Assert.Empty(await f.Store.HistoryAsync());
    }
    [Fact]
    public async Task Migration_is_current_and_initialization_does_not_reset_activation()
    {
        await using var f = new LocalActivationFixture(); await f.InitializeAsync(); await f.RegisterAsync(); await f.ActivateAsync();
        await f.Store.InitializeAsync(); Assert.True(f.Store.IsActive);
        await using var db = f.Open();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Single(await db.Installations.ToListAsync());
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("UPDATE Installations SET Status = 'Unknown'"));
    }
}

[Collection("SQLite global pool operations")]
public sealed class LocalActivationPoolTests
{
    [Fact]
    public async Task Activation_and_admin_status_reads_survive_concurrent_backup_pool_cleanup()
    {
        await using var f = new LocalActivationFixture(); await f.InitializeAsync(); await f.RegisterAsync(); await f.ActivateAsync();
        await f.Admin.SignInAsync("admin", LocalActivationFixture.AdminPassword);
        using var stop = new CancellationTokenSource();
        var cleanup = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                SqliteConnection.ClearAllPools();
                await Task.Delay(1);
            }
        });
        try
        {
            for (var i = 0; i < 100; i++)
            {
                Assert.True(f.Store.IsActive);
                Assert.Equal("Active", (await f.Service.StatusAsync()).Status);
            }
        }
        finally { stop.Cancel(); await cleanup; }
    }
}
