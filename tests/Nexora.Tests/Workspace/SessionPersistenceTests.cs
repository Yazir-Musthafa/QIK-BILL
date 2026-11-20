using Microsoft.Extensions.DependencyInjection;
using Nexora.Application;
using Nexora.Application.Abstractions;
using Nexora.Application.Licensing;
using Nexora.Application.Security;
using Nexora.Tests.Infrastructure;

namespace Nexora.Tests.Workspace;

internal sealed class MemorySessionPersistence : ISessionPersistence
{
    private PersistedSession? _session;
    public PersistedSession? Load() => _session;
    public void Save(PersistedSession session) => _session = session;
    public void Clear() => _session = null;
}

public sealed class SessionPersistenceTests : IAsyncLifetime
{
    private readonly MemorySessionPersistence _persistence = new();
    private readonly SqliteTestDatabase _db;

    public SessionPersistenceTests()
    {
        _db = new SqliteTestDatabase(services => services.AddSingleton<ISessionPersistence>(_persistence));
    }

    private IWorkspaceService Workspace => _db.Workspace;

    public Task InitializeAsync() => Workspace.InitializeAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Signing_in_persists_a_session_that_can_be_restored_after_sign_out()
    {
        await Workspace.CompleteSetupAsync(WorkspaceServiceTests.ValidRequest());
        var original = Workspace.CurrentSession;
        Assert.NotNull(original);

        _db.Get<ISessionContext>().Set(null);
        Assert.Null(Workspace.CurrentSession);

        var restored = await Workspace.TryResumeSessionAsync();
        Assert.NotNull(restored);
        Assert.Equal(original!.UserId, restored!.UserId);
        Assert.Equal(original.CompanyId, restored.CompanyId);
        Assert.Equal(original.PasswordChangedUtc, restored.PasswordChangedUtc);
    }

    [Fact]
    public async Task Explicit_sign_out_clears_the_persisted_session()
    {
        await Workspace.CompleteSetupAsync(WorkspaceServiceTests.ValidRequest());
        Workspace.SignOut();
        Assert.Null(await Workspace.TryResumeSessionAsync());
    }

    [Fact]
    public async Task Password_change_updates_the_persisted_session_marker()
    {
        await Workspace.CompleteSetupAsync(WorkspaceServiceTests.ValidRequest());
        const string password = "quiet ledger 42";
        await Workspace.ChangePasswordAsync(password, "an even quieter ledger 99");
        var original = Workspace.CurrentSession;
        Assert.NotNull(original);

        _db.Get<ISessionContext>().Set(null);
        var restored = await Workspace.TryResumeSessionAsync();
        Assert.NotNull(restored);
        Assert.Equal(original!.PasswordChangedUtc, restored!.PasswordChangedUtc);
    }
}