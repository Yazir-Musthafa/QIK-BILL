using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexora.Application;
using Nexora.Application.Abstractions;
using Nexora.Application.Licensing;
using Nexora.Application.Security;
using Nexora.Infrastructure.Licensing;

namespace Nexora.Tests.Licensing;

public sealed class ActivationTime : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);
    public long Ticks { get; set; }
    public override DateTimeOffset GetUtcNow() => Now;
    public override long GetTimestamp() => Ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public void Advance(TimeSpan duration) { Now += duration; Ticks += duration.Ticks; }
}

public sealed class ActivationTests : IDisposable
{
    private readonly RSA _key = RSA.Create(2048);
    private readonly Guid _installation = Guid.NewGuid();
    private readonly ActivationTime _time = new();
    private string PublicKey => Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo());
    private SignedActivation Sign(TimeSpan? duration = null) => ActivationSignature.Sign(new(Guid.NewGuid(), _installation,
        _time.Now, _time.Now + (duration ?? TimeSpan.FromDays(30))), _key);

    [Fact]
    public void Signature_binds_installation_expiry_and_maximum_thirty_days()
    {
        var signed = Sign();
        Assert.NotNull(ActivationSignature.Verify(signed, PublicKey, _installation, _time.Now));
        Assert.Null(ActivationSignature.Verify(signed, PublicKey, Guid.NewGuid(), _time.Now));
        Assert.Null(ActivationSignature.Verify(signed, PublicKey, _installation, _time.Now.AddDays(30)));
        Assert.Null(ActivationSignature.Verify(Sign(TimeSpan.FromDays(31)), PublicKey, _installation, _time.Now));
        Assert.Null(ActivationSignature.Verify(signed, PublicKey, _installation, _time.Now.AddSeconds(-1)));
        var grant = JsonSerializer.Deserialize<ActivationGrant>(Convert.FromBase64String(signed.Payload))!;
        var changed = signed with { Payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(grant with { ExpiresUtc = grant.ExpiresUtc.AddDays(1) })) };
        Assert.Null(ActivationSignature.Verify(changed, PublicKey, _installation, _time.Now));
        using var other = RSA.Create(2048);
        Assert.Null(ActivationSignature.Verify(signed, Convert.ToBase64String(other.ExportSubjectPublicKeyInfo()), _installation, _time.Now));
        Assert.Null(ActivationSignature.Verify(new("invalid", "invalid"), PublicKey, _installation, _time.Now));
    }

    [Fact]
    public void Offline_access_survives_restart_but_expires_at_thirty_days_without_network()
    {
        var store = new MemoryState { State = new(_installation, "customer", "token", Sign(), _time.Now) };
        var client = Client(store);
        client.Load();
        Assert.True(client.IsActive);
        _time.Advance(TimeSpan.FromDays(29));
        client.ObserveClock();
        var restarted = Client(store);
        restarted.Load();
        Assert.True(restarted.IsActive);
        _time.Advance(TimeSpan.FromDays(1));
        Assert.False(restarted.IsActive);
        Assert.Throws<AccessDeniedException>(restarted.Demand);
    }

    [Fact]
    public void Clock_rollback_and_monotonic_expiry_cannot_extend_an_open_grant()
    {
        var store = new MemoryState { State = new(_installation, "customer", "token", Sign(), _time.Now) };
        var client = Client(store); client.Load();
        _time.Advance(TimeSpan.FromDays(2)); client.ObserveClock();
        _time.Now -= TimeSpan.FromDays(1);
        Assert.False(client.IsActive);
        var restarted = Client(store); restarted.Load(); Assert.False(restarted.IsActive);
        _time.Now = store.State!.LastSeenUtc;
        var next = Client(store); next.Load();
        _time.Ticks += TimeSpan.FromDays(30).Ticks;
        Assert.False(next.IsActive);
    }

    [Fact]
    public async Task Network_failure_does_not_extend_or_destroy_a_valid_grant()
    {
        var store = new MemoryState { State = new(_installation, "customer", "token", Sign(), _time.Now) };
        var client = Client(store); client.Load();
        var expiry = client.ExpiresUtc;
        await client.CheckAsync();
        Assert.True(client.IsActive);
        Assert.Equal(expiry, client.ExpiresUtc);
        _time.Advance(TimeSpan.FromDays(30));
        await client.CheckAsync();
        Assert.False(client.IsActive);
        Assert.Contains("Internet is needed", client.Message);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Suspended")]
    [InlineData("SignInRequired")]
    public async Task Explicit_server_denial_removes_cached_access(string status)
    {
        var store = new MemoryState { State = new(_installation, "customer", "token", Sign(), _time.Now) };
        var client = Client(store, new ReplyHandler(new(status, "Contact admin"))); client.Load();
        await client.CheckAsync();
        Assert.False(client.IsActive);
        Assert.Null(store.State!.Lease);
        if (status == "SignInRequired") Assert.Null(store.State.Token);
    }

    [Fact]
    public async Task Forged_server_response_or_failed_save_cannot_open_the_desktop()
    {
        var store = new MemoryState { State = new(_installation) };
        var client = Client(store, new ReplyHandler(new("Active", "Active", Lease: new("fake", "fake")))); client.Load();
        await client.CheckAsync(); Assert.False(client.IsActive);
        store.FailWrites = true;
        client = Client(store, new ReplyHandler(new("Active", "Active", Lease: Sign()))); client.Load();
        await client.CheckAsync(); Assert.False(client.IsActive);
    }

    [Fact]
    public void Missing_configuration_is_fail_closed_and_installation_id_is_saved_before_registration()
    {
        var store = new MemoryState();
        var client = new ActivationClient(new HttpClient(), "", store, _time);
        client.Load();
        Assert.NotEqual(Guid.Empty, store.State!.InstallationId);
        Assert.False(client.IsActive);
        Assert.False(client.IsConfigured);
    }

    [Fact]
    public async Task Registration_initializes_and_persists_the_installation_before_sending()
    {
        var store = new MemoryState();
        var handler = new RegistrationHandler(store);
        var client = Client(store, handler);
        await client.RegisterAsync("customer", "quiet ledger 42", "Test Traders", "owner@example.test");
        Assert.NotEqual(Guid.Empty, handler.SentInstallation);
        Assert.Equal(store.State!.InstallationId, handler.SentInstallation);
        Assert.False(client.IsActive);
        Assert.Equal("pending-token", store.State.Token);
    }

    [Fact]
    public void Service_permissions_and_session_refresh_fail_when_activation_expires()
    {
        var store = new MemoryState { State = new(_installation, "customer", "token", Sign(), _time.Now) };
        var client = Client(store); client.Load();
        var inner = new SessionContext();
        var sessions = new LicensedSessionContext(inner, client);
        var session = new SessionInfo(Guid.NewGuid(), "staff", "Staff", Guid.NewGuid(), "Customer", false, 2026,
            Guid.NewGuid(), "Administrator", new HashSet<string> { "sales.view" }, 100, false);
        sessions.Set(session);
        Assert.Same(session, new Authorizer(sessions).Demand("sales.view"));
        _time.Advance(TimeSpan.FromDays(30));
        Assert.Throws<AccessDeniedException>(() => new Authorizer(sessions).Demand("sales.view"));
        Assert.Null(sessions.Current);
        Assert.Null(inner.Current);
        Assert.False(sessions.TryUpdate(session, session));
        Assert.Throws<AccessDeniedException>(() => sessions.Set(session));
    }

    private ActivationClient Client(MemoryState state, HttpMessageHandler? handler = null) => new(new HttpClient(handler ?? new OfflineHandler())
        { BaseAddress = new Uri("https://activation.example/") }, PublicKey, state, _time);

    [Fact]
    public async Task Real_workspace_and_database_access_are_blocked_before_activation_and_after_expiry()
    {
        var store = new MemoryState { State = new(_installation) };
        var client = Client(store, new ReplyHandler(new("Active", "Active", Lease: Sign()))); client.Load();
        await using var db = new Infrastructure.SqliteTestDatabase(services =>
        {
            services.AddSingleton<IActivationAccess>(client);
            services.AddSingleton<ISessionContext>(new LicensedSessionContext(new SessionContext(), client));
        });
        await db.Workspace.InitializeAsync();
        var setup = new SetupRequest("Licensed Company", "Asha", "asha", "quiet ledger 42", "27", 2026, false);
        await Assert.ThrowsAsync<AccessDeniedException>(() => db.Workspace.CompleteSetupAsync(setup));
        Assert.True(await db.Workspace.NeedsSetupAsync());
        await client.CheckAsync();
        await db.Workspace.CompleteSetupAsync(setup);
        var companies = db.Get<Nexora.Application.Companies.CompanyService>();
        Assert.NotNull(await companies.GetDetailsAsync());
        await using var context = await db.Get<IDbContextFactory<Nexora.Infrastructure.Persistence.NexoraDbContext>>().CreateDbContextAsync();
        Assert.Single(await context.Companies.ToListAsync());
        _time.Advance(TimeSpan.FromDays(30));
        await Assert.ThrowsAsync<AccessDeniedException>(() => companies.GetDetailsAsync());
        await Assert.ThrowsAsync<AccessDeniedException>(() => db.Workspace.SignInAsync("asha", "quiet ledger 42"));
        Assert.Empty(await context.Companies.ToListAsync());
    }
    public void Dispose() => _key.Dispose();
    private sealed class MemoryState : IActivationStateStore
    {
        public ActivationState? State { get; set; }
        public bool FailWrites { get; set; }
        public ActivationState? Read() => State;
        public void Write(ActivationState state) { if (FailWrites) throw new IOException(); State = state; }
    }
    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw new HttpRequestException("Offline");
    }
    private sealed class ReplyHandler(ActivationReply reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(reply) });
    }
    private sealed class RegistrationHandler(MemoryState store) : HttpMessageHandler
    {
        public Guid SentInstallation { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var registration = (await request.Content!.ReadFromJsonAsync<ActivationRegistration>(cancellationToken))!;
            SentInstallation = registration.InstallationId;
            Assert.Equal(store.State!.InstallationId, SentInstallation);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new ActivationReply("Pending", "Contact admin", "pending-token")) };
        }
    }
}
