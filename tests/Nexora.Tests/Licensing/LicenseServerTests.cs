using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexora.Application;
using Nexora.Application.Licensing;
using Nexora.Application.Security;
using Nexora.Licensing;
using Nexora.Licensing.Persistence;

namespace Nexora.Tests.Licensing;

public sealed class LicenseServerTests
{
    [Fact]
    public async Task Registration_admin_activation_offline_grant_suspension_and_reactivation_work_over_http()
    {
        await using var factory = new Server();
        using var customer = factory.Client();
        var installation = Guid.NewGuid();
        var pending = await Register(customer, installation);
        Assert.Equal("Pending", pending.Status);
        Assert.Null(pending.Lease);
        Assert.NotNull(pending.Token);
        var check = new ActivationCheck(pending.Token!, installation);
        Assert.Equal("Pending", (await Check(customer, check)).Status);

        using var admin = factory.Client();
        Assert.Equal(HttpStatusCode.Redirect, (await admin.GetAsync("/")).StatusCode);
        // Direct POST without CSRF token cannot alter status, even after login.
        await LoginAdmin(admin);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LicenseDb>();
        var account = await db.Accounts.AsNoTracking().SingleAsync();
        Assert.DoesNotContain("quiet ledger 42", account.PasswordHash);
        Assert.NotEqual(pending.Token, account.TokenHash);
        var forged = await admin.PostAsync("/", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["Selected"] = account.Id.ToString(), ["Action"] = "Activate", ["Note"] = "Paid" }));
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        Assert.Equal("Pending", (await Check(customer, check)).Status);

        await Change(admin, account.Id, "Activate", "Payment received");
        var active = await Check(customer, check);
        Assert.Equal("Active", active.Status);
        var grant = ActivationSignature.Verify(active.Lease, factory.PublicKey, installation, DateTimeOffset.UtcNow);
        Assert.NotNull(grant);
        Assert.Equal(TimeSpan.FromDays(30), grant.ExpiresUtc - grant.IssuedUtc);
        await Change(admin, account.Id, "Suspend", "Payment not received");
        Assert.Equal("Suspended", (await Check(customer, check)).Status);
        // Offline customer retains the already-signed grant until expiry, by the owner's policy.
        Assert.NotNull(ActivationSignature.Verify(active.Lease, factory.PublicKey, installation, DateTimeOffset.UtcNow));
        await Change(admin, account.Id, "Activate", "Renewal paid");
        Assert.Equal("Active", (await Check(customer, check)).Status);
        Assert.Equal(4, await db.Audit.CountAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Contains("Payment received", await admin.GetStringAsync($"/?Selected={account.Id}"));
        var history = await db.Audit.FirstAsync();
        history.Note = "rewritten";
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => db.Database.ExecuteSqlRawAsync("UPDATE Audit SET Note = 'rewritten'"));
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM Audit"));
    }

    [Fact]
    public async Task Installation_binding_token_rotation_lockout_and_admin_reset_are_enforced()
    {
        await using var factory = new Server();
        using var client = factory.Client();
        var installation = Guid.NewGuid();
        var registered = await Register(client, installation);
        var duplicate = await client.PostAsJsonAsync("/api/activation/register", new ActivationRegistration("CUSTOMER", "quiet ledger 42", "Duplicate", "contact", installation));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Equal("SignInRequired", (await Check(client, new(registered.Token!, Guid.NewGuid()))).Status);
        var otherInstall = new ActivationLogin("customer", "quiet ledger 42", Guid.NewGuid());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/activation/login", otherInstall)).StatusCode);
        var login = await client.PostAsJsonAsync("/api/activation/login", new ActivationLogin("CUSTOMER", "quiet ledger 42", installation));
        var signedIn = (await login.Content.ReadFromJsonAsync<ActivationReply>())!;
        Assert.NotEqual(registered.Token, signedIn.Token);
        Assert.Equal("SignInRequired", (await Check(client, new(registered.Token!, installation))).Status);
        using var admin = factory.Client(); await LoginAdmin(admin);
        using var scope = factory.Services.CreateScope();
        var id = await scope.ServiceProvider.GetRequiredService<LicenseDb>().Accounts.Select(x => x.Id).SingleAsync();
        await Change(admin, id, "ResetInstallation", "Computer replacement");
        Assert.Equal("SignInRequired", (await Check(client, new(signedIn.Token!, installation))).Status);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/activation/login", otherInstall)).StatusCode);
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/activation/login", otherInstall with { Password = "wrong" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/activation/login", otherInstall)).StatusCode);
    }

    [Fact]
    public async Task Admin_service_requires_permission_and_invalid_changes_leave_no_audit_or_status_change()
    {
        await using var factory = new Server();
        using var client = factory.Client();
        await Register(client, Guid.NewGuid());
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<LicenseAdministrationService>();
        await Assert.ThrowsAsync<AccessDeniedException>(() => service.ListAsync());
        await Assert.ThrowsAsync<AccessDeniedException>(() => service.ChangeAsync(Guid.NewGuid(), "Activate", "paid"));
        using var admin = factory.Client(); await LoginAdmin(admin);
        var db = scope.ServiceProvider.GetRequiredService<LicenseDb>();
        var id = await db.Accounts.Select(x => x.Id).SingleAsync();
        await Change(admin, id, "Activate", "", expectRedirect: false);
        await Change(admin, id, "Delete", "bad action", expectRedirect: false);
        Assert.Equal("Pending", await db.Accounts.Select(x => x.Status).SingleAsync());
        Assert.Equal(1, await db.Audit.CountAsync());
    }

    [Fact]
    public void Admin_password_attempts_lock_after_five_failures_and_recover_after_fifteen_minutes()
    {
        var time = new ActivationTime();
        var hasher = new Pbkdf2PasswordHasher(1000);
        var credentials = new AdminCredentials("admin", hasher.Hash("correct phrase 42"), hasher, time);
        for (var i = 0; i < 5; i++) Assert.False(credentials.Verify("admin", "wrong"));
        Assert.False(credentials.Verify("admin", "correct phrase 42"));
        time.Advance(TimeSpan.FromMinutes(15));
        Assert.True(credentials.Verify("admin", "correct phrase 42"));
        Assert.False(credentials.Verify("customer", "correct phrase 42"));
    }

    private static async Task<ActivationReply> Register(HttpClient client, Guid installation)
    {
        var response = await client.PostAsJsonAsync("/api/activation/register", new ActivationRegistration("customer", "quiet ledger 42", "Test Traders", "owner@example.test", installation));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ActivationReply>())!;
    }
    private static async Task<ActivationReply> Check(HttpClient client, ActivationCheck request) =>
        (await (await client.PostAsJsonAsync("/api/activation/check", request)).Content.ReadFromJsonAsync<ActivationReply>())!;
    private static string Csrf(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private static async Task LoginAdmin(HttpClient client)
    {
        var token = Csrf(await client.GetStringAsync("/Login"));
        Assert.NotEmpty(token);
        var response = await client.PostAsync("/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["Username"] = "admin", ["Password"] = "correct phrase 42", ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
    private static async Task Change(HttpClient client, Guid id, string action, string note, bool expectRedirect = true)
    {
        var token = Csrf(await client.GetStringAsync($"/?Selected={id}"));
        var response = await client.PostAsync("/", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["Selected"] = id.ToString(), ["Action"] = action, ["Note"] = note, ["__RequestVerificationToken"] = token }));
        Assert.Equal(expectRedirect ? HttpStatusCode.Redirect : HttpStatusCode.OK, response.StatusCode);
    }
    private sealed class Server : WebApplicationFactory<Program>
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "qik-license-tests", Guid.NewGuid().ToString("N"));
        private readonly RSA _key = RSA.Create(2048);
        public string PublicKey => Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo());
        public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Licensing:DataDirectory", _directory);
            builder.UseSetting("Licensing:AdminUsername", "admin");
            builder.UseSetting("Licensing:AdminPasswordHash", new Pbkdf2PasswordHasher(1000).Hash("correct phrase 42"));
            builder.UseSetting("Licensing:SigningKeyPem", _key.ExportPkcs8PrivateKeyPem());
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPasswordHasher>();
                services.AddSingleton<IPasswordHasher>(new Pbkdf2PasswordHasher(1000));
            });
        }
        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync(); _key.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(_directory, true); } catch (IOException) { }
        }
    }
}
