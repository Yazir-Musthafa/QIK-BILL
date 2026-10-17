using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexora.Application;
using Nexora.Application.Abstractions;
using Nexora.Application.Licensing;
using Nexora.Application.Security;
using Nexora.Infrastructure.Licensing;
using Nexora.Tests.Infrastructure;

namespace Nexora.Tests.Licensing;

public sealed class ActivationTime : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);
    public long Ticks { get; set; }
    public override DateTimeOffset GetUtcNow() => Now;
    public override long GetTimestamp() => Ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public void Advance(TimeSpan duration) { Now += duration; Ticks += duration.Ticks; }
}

public sealed class LocalActivationFixture : IAsyncDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "qik-local-activation", Guid.NewGuid().ToString("N"));
    public ActivationTime Time { get; } = new();
    public Pbkdf2PasswordHasher Hasher { get; } = new(1000);
    public LocalActivationStore Store { get; }
    public LocalAdministrator Admin { get; }
    public LocalAdministrationService Service { get; }
    public SqliteTestDatabase Workspace { get; }
    public string DatabasePath => Path.Combine(_directory, "local-activation.db");
    public const string AdminPassword = "owner secret phrase 42";
    public string Hash { get; }
    public static SetupRequest Registration => new("Test Traders", "Asha", "asha", "quiet ledger 42", "27", 2026, false);
    public LocalActivationFixture()
    {
        Hash = Hasher.Hash(AdminPassword);
        Store = new(DatabasePath, Hasher, Time, Hash);
        Admin = new(Store, Time);
        Workspace = new(services =>
        {
            services.AddSingleton<IActivationAccess>(Store);
            services.AddSingleton<ISessionContext>(new LicensedSessionContext(new SessionContext(Time), Store));
        });
        Service = new(Store, Workspace.Get<IWorkspaceStore>(), Admin);
    }
    public async Task InitializeAsync()
    {
        await Store.InitializeAsync();
        await Workspace.Workspace.InitializeAsync();
    }
    public async Task RegisterAsync() => await Workspace.Workspace.CompleteSetupAsync(Registration);
    public async Task ActivateAsync()
    {
        Assert.True(await Admin.SignInAsync("admin", AdminPassword));
        await Service.ChangeAsync("Active", "Payment received");
        Admin.SignOut();
    }
    public LocalActivationDb Open() => new(new DbContextOptionsBuilder<LocalActivationDb>().UseSqlite($"Data Source={DatabasePath}").Options);
    public void ClearPools()
    {
        using var applicationConnection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = DatabasePath, ForeignKeys = true, DefaultTimeout = 5 }.ToString());
        SqliteConnection.ClearPool(applicationConnection);
        using var inspectionConnection = new SqliteConnection($"Data Source={DatabasePath}");
        SqliteConnection.ClearPool(inspectionConnection);
    }
    public async ValueTask DisposeAsync()
    {
        await Workspace.DisposeAsync();
        ClearPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}

public sealed class ActivationTests
{
    [Fact]
    public async Task Registration_saves_customer_but_never_opens_pending_workspace()
    {
        await using var f = new LocalActivationFixture(); await f.InitializeAsync();
        await f.RegisterAsync();
        Assert.False(await f.Workspace.Workspace.NeedsSetupAsync());
        Assert.Null(f.Workspace.Workspace.CurrentSession);
        Assert.False(f.Store.IsActive);
        Assert.Equal("Pending", (await f.Store.GetAsync()).Status);
        await Assert.ThrowsAsync<AccessDeniedException>(() => f.Workspace.Workspace.SignInAsync("asha", "quiet ledger 42"));
        await Assert.ThrowsAsync<AccessDeniedException>(() => f.Workspace.Get<Nexora.Application.Companies.CompanyService>().GetDetailsAsync());
        await using var context = await f.Workspace.Get<IDbContextFactory<Nexora.Infrastructure.Persistence.NexoraDbContext>>().CreateDbContextAsync();
        Assert.Empty(await context.Companies.ToListAsync());
    }
    [Fact]
    public async Task Customer_uses_registration_password_after_local_admin_approval()
    {
        await using var f = new LocalActivationFixture(); await f.InitializeAsync(); await f.RegisterAsync(); await f.ActivateAsync();
        Assert.False((await f.Workspace.Workspace.SignInAsync("asha", "wrong")).Succeeded);
        Assert.True((await f.Workspace.Workspace.SignInAsync("asha", "quiet ledger 42")).Succeeded);
        Assert.NotNull(await f.Workspace.Get<Nexora.Application.Companies.CompanyService>().GetDetailsAsync());
    }
    [Fact]
    public async Task Suspension_immediately_invalidates_services_sessions_and_database_filters()
    {
        await using var f = new LocalActivationFixture(); await f.InitializeAsync(); await f.RegisterAsync(); await f.ActivateAsync();
        await f.Workspace.Workspace.SignInAsync("asha", "quiet ledger 42");
        var sessions = f.Workspace.Get<ISessionContext>(); var current = sessions.Current!;
        await using var context = await f.Workspace.Get<IDbContextFactory<Nexora.Infrastructure.Persistence.NexoraDbContext>>().CreateDbContextAsync();
        Assert.Single(await context.Companies.ToListAsync());
        Assert.True(await f.Admin.SignInAsync("admin", LocalActivationFixture.AdminPassword));
        await f.Service.ChangeAsync("Suspended", "Payment pending");
        Assert.Null(sessions.Current);
        Assert.False(sessions.TryUpdate(current, current));
        Assert.Throws<AccessDeniedException>(() => sessions.Set(current));
        await Assert.ThrowsAsync<AccessDeniedException>(() => f.Workspace.Get<Nexora.Application.Companies.CompanyService>().GetDetailsAsync());
        await Assert.ThrowsAsync<AccessDeniedException>(() => f.Workspace.Workspace.RefreshSessionAsync());
        await Assert.ThrowsAsync<AccessDeniedException>(() => f.Workspace.Workspace.SignInAsync("asha", "quiet ledger 42"));
        Assert.Empty(await context.Companies.ToListAsync());
    }
    [Fact]
    public async Task Reactivation_requires_fresh_customer_login_and_preserves_company_data()
    {
        await using var f = new LocalActivationFixture(); await f.InitializeAsync(); await f.RegisterAsync(); await f.ActivateAsync();
        await f.Workspace.Workspace.SignInAsync("asha", "quiet ledger 42");
        Assert.True(await f.Admin.SignInAsync("admin", LocalActivationFixture.AdminPassword));
        await f.Service.ChangeAsync("Suspended", "Overdue");
        Assert.Null(f.Workspace.Workspace.CurrentSession);
        await f.Service.ChangeAsync("Active", "Paid");
        Assert.Null(f.Workspace.Workspace.CurrentSession);
        Assert.True((await f.Workspace.Workspace.SignInAsync("asha", "quiet ledger 42")).Succeeded);
        Assert.Single(await f.Workspace.Workspace.GetCompaniesAsync());
    }
    [Fact]
    public async Task Offline_activation_survives_restart_and_more_than_thirty_days()
    {
        await using var f = new LocalActivationFixture(); await f.InitializeAsync(); await f.RegisterAsync(); await f.ActivateAsync();
        f.Time.Advance(TimeSpan.FromDays(365));
        var restarted = new LocalActivationStore(f.DatabasePath, f.Hasher, f.Time, f.Hash);
        await restarted.InitializeAsync();
        Assert.True(restarted.IsActive);
        restarted.Demand();
        Assert.True((await f.Workspace.Workspace.SignInAsync("asha", "quiet ledger 42")).Succeeded);
    }
    [Fact]
    public async Task Different_installation_stays_pending_and_shared_store_sees_suspension()
    {
        await using var f = new LocalActivationFixture(); await f.InitializeAsync(); await f.RegisterAsync(); await f.ActivateAsync();
        await using var other = new LocalActivationFixture(); await other.InitializeAsync();
        Assert.False(other.Store.IsActive);
        var secondProcess = new LocalActivationStore(f.DatabasePath, f.Hasher, f.Time, f.Hash);
        Assert.True(secondProcess.IsActive);
        await f.Admin.SignInAsync("admin", LocalActivationFixture.AdminPassword);
        await f.Service.ChangeAsync("Suspended", "Unpaid");
        Assert.False(secondProcess.IsActive);
    }
    [Fact]
    public async Task Missing_or_corrupt_activation_database_fails_closed()
    {
        await using var f = new LocalActivationFixture(); await f.InitializeAsync(); await f.RegisterAsync(); await f.ActivateAsync();
        f.ClearPools(); File.Delete(f.DatabasePath);
        Assert.False(f.Store.IsActive); Assert.Throws<AccessDeniedException>(f.Store.Demand);
        f.ClearPools(); File.WriteAllText(f.DatabasePath, "corrupt");
        Assert.False(f.Store.IsActive);
    }
    [Fact]
    public async Task Reserved_admin_username_cannot_be_registered_as_customer()
    {
        await using var f = new LocalActivationFixture(); await f.InitializeAsync();
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => f.Workspace.Workspace.CompleteSetupAsync(LocalActivationFixture.Registration with { Username = " Admin " }));
        Assert.True(await f.Workspace.Workspace.NeedsSetupAsync());
    }
    [Fact]
    public async Task Repeat_registration_cannot_replace_customer_or_reset_status()
    {
        await using var f = new LocalActivationFixture(); await f.InitializeAsync(); await f.RegisterAsync(); await f.ActivateAsync();
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => f.Workspace.Workspace.CompleteSetupAsync(LocalActivationFixture.Registration with { Username = "another" }));
        Assert.True(f.Store.IsActive);
    }
    [Fact]
    public async Task Staff_login_shares_activation_and_is_blocked_when_installation_suspended()
    {
        await using var f = new LocalActivationFixture(); await f.InitializeAsync(); await f.RegisterAsync(); await f.ActivateAsync();
        var inner = new SessionContext(f.Time); var session = new LicensedSessionContext(inner, f.Store);
        var staff = new SessionInfo(Guid.NewGuid(), "staff", "Staff", Guid.NewGuid(), "Customer", false, 2026,
            Guid.NewGuid(), "Sales", new HashSet<string> { "sales.view" }, 0, false);
        session.Set(staff); Assert.Same(staff, new Authorizer(session).Demand("sales.view"));
        await f.Admin.SignInAsync("admin", LocalActivationFixture.AdminPassword); await f.Service.ChangeAsync("Suspended", "Overdue");
        session.RecordActivity(); Assert.Null(inner.Current);
        Assert.Throws<AccessDeniedException>(() => new Authorizer(session).Demand("sales.view"));
    }
}
