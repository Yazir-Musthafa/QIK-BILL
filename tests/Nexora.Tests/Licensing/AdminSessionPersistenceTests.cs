using Microsoft.Extensions.DependencyInjection;
using Nexora.Application;
using Nexora.Application.Abstractions;
using Nexora.Application.Licensing;
using Nexora.Application.Security;
using Nexora.Infrastructure.Licensing;
using Nexora.Tests.Infrastructure;

namespace Nexora.Tests.Licensing;

internal sealed class AdminMemorySessionPersistence2 : ISessionPersistence
{
    private PersistedSession? _session;
    public PersistedSession? Load() => _session;
    public void Save(PersistedSession session) => _session = session;
    public void Clear() => _session = null;
}

public sealed class AdminSessionPersistenceTests
{
    [Fact]
    public async Task Admin_sign_out_then_user_login_persists_the_user_not_admin()
    {
        var directory = Path.Combine(Path.GetTempPath(), "qik-admin-session", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "local-activation.db");
        var hasher = new Pbkdf2PasswordHasher(1_000);
        const string adminPassword = "owner secret phrase 42";
        var adminHash = hasher.Hash(adminPassword);
        var time = new ActivationTime { Now = new(2026, 10, 18, 0, 0, 0, TimeSpan.Zero) };
        var store = new LocalActivationStore(databasePath, hasher, time, adminHash);
        await store.InitializeAsync();

        var persistence = new AdminMemorySessionPersistence2();
        await using var db = new SqliteTestDatabase(services =>
        {
            services.AddSingleton<IActivationAccess>(store);
            services.AddSingleton<ISessionPersistence>(persistence);
        });
        await db.Workspace.InitializeAsync();

        await db.Workspace.CompleteSetupAsync(new SetupRequest("Test Traders", "Asha", "asha", "quiet ledger 42", "27", 2026, false));
        var admin = new LocalAdministrator(store, time, persistence);
        Assert.True(await admin.SignInAsync("admin", adminPassword));
        await new LocalAdministrationService(store, db.Get<IWorkspaceStore>(), admin).ChangeAsync("Active", "Paid");
        admin.SignOut();

        Assert.True((await db.Workspace.SignInAsync("asha", "quiet ledger 42")).Succeeded);
        Assert.Equal(SessionKind.User, persistence.Load()!.Kind);

        Assert.False(await admin.TryResumeAsync());

        db.Get<ISessionContext>().Set(null);
        var restored = await db.Workspace.TryResumeSessionAsync();
        Assert.NotNull(restored);
        Assert.Equal("asha", restored!.Username);

        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}