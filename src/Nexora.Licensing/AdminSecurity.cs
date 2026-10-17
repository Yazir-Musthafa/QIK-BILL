using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Nexora.Application;
using Nexora.Application.Licensing;
using Nexora.Application.Security;

namespace Nexora.Licensing;

public sealed class AdminCredentials(string username, string passwordHash, IPasswordHasher hasher, TimeProvider time)
{
    private readonly object _gate = new();
    private int _failed;
    private DateTimeOffset _lockedUntil;
    public string Username { get; } = username;
    public string Version { get; } = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(username + passwordHash)));
    public bool Verify(string? name, string? password)
    {
        lock (_gate)
        {
            if (_lockedUntil > time.GetUtcNow() || password is null || password.Length > 128) return false;
            var valid = hasher.Verify(password, passwordHash) != PasswordVerification.Failed
                && string.Equals(name, Username, StringComparison.Ordinal);
            if (valid) { _failed = 0; return true; }
            if (++_failed >= 5) { _lockedUntil = time.GetUtcNow().AddMinutes(15); _failed = 0; }
            return false;
        }
    }
}

/// <summary>Only the server's authenticated operator receives the licensing permission.</summary>
public sealed class AdminAuthorizer(IHttpContextAccessor context, AdminCredentials credentials) : IAuthorizer
{
    public SessionInfo RequireSession()
    {
        var user = context.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true || user.FindFirstValue("credential-version") != credentials.Version)
            throw new AccessDeniedException("Sign in as the licensing administrator.");
        return new SessionInfo(Guid.Empty, credentials.Username, "Licensing administrator", Guid.Empty, "QIK", false, 2026,
            Guid.Empty, "Licensing administrator", new HashSet<string> { LicenseAdministrationService.ManagePermission }, 0, false);
    }
    public SessionInfo Demand(string permission)
    {
        var session = RequireSession();
        if (!session.Has(permission)) throw new AccessDeniedException("This permission is not available.");
        return session;
    }
    public bool Has(string permission)
    {
        try { return RequireSession().Has(permission); }
        catch (AccessDeniedException) { return false; }
    }
}
