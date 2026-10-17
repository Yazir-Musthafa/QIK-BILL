using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Nexora.Application;
using Nexora.Application.Licensing;
using Nexora.Application.Security;
using Nexora.Licensing;
using Nexora.Licensing.Persistence;

if (args is ["--configure", var directory]) { ConfigureServer.Run(directory); return; }

var builder = WebApplication.CreateBuilder(args);
var settings = Environment.GetEnvironmentVariable("QIK_LICENSING_SETTINGS");
// Integration tests supply an isolated database and keys; never load an operator's real settings there.
if (!builder.Environment.IsEnvironment("Testing"))
{
    if (!string.IsNullOrWhiteSpace(settings)) builder.Configuration.AddJsonFile(Path.GetFullPath(settings), optional: false, reloadOnChange: false);
    builder.Configuration.AddEnvironmentVariables();
}
var section = builder.Configuration.GetSection("Licensing");
var data = Path.GetFullPath(section["DataDirectory"] ?? throw new InvalidOperationException("Configure Licensing:DataDirectory first."));
var adminName = section["AdminUsername"] ?? throw new InvalidOperationException("Configure the administrator first.");
var adminHash = section["AdminPasswordHash"] ?? throw new InvalidOperationException("Configure the administrator password hash first.");
var signingKey = section["SigningKeyPem"] ?? throw new InvalidOperationException("Configure the signing key first.");
Directory.CreateDirectory(data);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 16 * 1024);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddSingleton(sp => new AdminCredentials(adminName, adminHash, sp.GetRequiredService<IPasswordHasher>(), TimeProvider.System));
builder.Services.AddSingleton(new LicenseSigner(signingKey));
builder.Services.AddDbContext<LicenseDb>(options => options.UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
    { DataSource = Path.Combine(data, "licensing.db"), ForeignKeys = true }.ToString()));
builder.Services.AddScoped<LicenseOperations>();
builder.Services.AddScoped<ILicenseAdministrationStore, LicenseAdministrationStore>();
builder.Services.AddScoped<LicenseAdministrationService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAuthorizer, AdminAuthorizer>();
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(data, "cookie-keys"))).SetApplicationName("QIK.Licensing");
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.LoginPath = "/Login";
    options.Cookie.Name = "QIK.Admin";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.SlidingExpiration = false;
    options.Events.OnValidatePrincipal = context =>
    {
        if (context.Principal?.FindFirst("credential-version")?.Value != context.HttpContext.RequestServices.GetRequiredService<AdminCredentials>().Version)
            context.RejectPrincipal();
        return Task.CompletedTask;
    };
});
builder.Services.AddAuthorization();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    if (section["TrustedProxy"] is { Length: > 0 } proxy) options.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
});
builder.Services.AddAntiforgery(options => options.Cookie.SecurePolicy = CookieSecurePolicy.Always);
builder.Services.AddRazorPages(options => { options.Conventions.AuthorizeFolder("/"); options.Conventions.AllowAnonymousToPage("/Login"); });
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("activation", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
using (var scope = app.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<LicenseDb>().Database.MigrateAsync();
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; style-src 'self'; form-action 'self'; frame-ancestors 'none'; base-uri 'self'";
    try { await next(); }
    catch (WorkspaceValidationException ex) when (context.Request.Path.StartsWithSegments("/api"))
    { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { message = ex.Message }); }
});
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages().RequireRateLimiting("activation");
app.MapPost("/api/activation/register", (ActivationRegistration request, LicenseOperations operations, CancellationToken ct) => operations.RegisterAsync(request, ct)).RequireRateLimiting("activation");
app.MapPost("/api/activation/login", (ActivationLogin request, LicenseOperations operations, CancellationToken ct) => operations.LoginAsync(request, ct)).RequireRateLimiting("activation");
app.MapPost("/api/activation/check", (ActivationCheck request, LicenseOperations operations, CancellationToken ct) => operations.CheckAsync(request, ct)).RequireRateLimiting("activation");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.Run();

public partial class Program;
