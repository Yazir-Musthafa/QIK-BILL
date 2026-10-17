using System.Security.Cryptography;
using System.Text.Json;
using Nexora.Application.Abstractions;
using Nexora.Application.Security;

namespace Nexora.Application.Licensing;

public sealed record ActivationRegistration(string Username, string Password, string CustomerName, string Contact, Guid InstallationId);
public sealed record ActivationLogin(string Username, string Password, Guid InstallationId);
public sealed record ActivationCheck(string Token, Guid InstallationId);
public sealed record ActivationReply(string Status, string Message, string? Token = null, SignedActivation? Lease = null);
public sealed record SignedActivation(string Payload, string Signature);
public sealed record ActivationGrant(Guid AccountId, Guid InstallationId, DateTimeOffset IssuedUtc, DateTimeOffset ExpiresUtc);

/// <summary>Only a server-signed grant can open the desktop, for at most thirty days.</summary>
public static class ActivationSignature
{
    public static readonly TimeSpan OfflinePeriod = TimeSpan.FromDays(30);

    public static SignedActivation Sign(ActivationGrant grant, RSA privateKey)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(grant);
        return new(Convert.ToBase64String(bytes), Convert.ToBase64String(privateKey.SignData(bytes,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pss)));
    }

    public static ActivationGrant? Verify(SignedActivation? signed, string publicKey, Guid installationId, DateTimeOffset now)
    {
        if (signed is null || string.IsNullOrEmpty(signed.Payload) || string.IsNullOrEmpty(signed.Signature) || signed.Payload.Length > 4096 || signed.Signature.Length > 2048) return null;
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            var bytes = Convert.FromBase64String(signed.Payload);
            if (!rsa.VerifyData(bytes, Convert.FromBase64String(signed.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pss)) return null;
            var grant = JsonSerializer.Deserialize<ActivationGrant>(bytes);
            return grant is not null && grant.AccountId != Guid.Empty && grant.InstallationId == installationId
                && grant.IssuedUtc <= now && grant.ExpiresUtc > now && grant.ExpiresUtc > grant.IssuedUtc
                && grant.ExpiresUtc - grant.IssuedUtc <= OfflinePeriod ? grant : null;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException or ArgumentException)
        { return null; }
    }
}

public interface IActivationAccess
{
    bool IsActive { get; }
    void Demand();
}

/// <summary>Protects service access and database filters even when a screen has not yet refreshed.</summary>
public sealed class LicensedSessionContext(ISessionContext inner, IActivationAccess activation) : ISessionContext
{
    public SessionInfo? Current
    {
        get
        {
            if (activation.IsActive) return inner.Current;
            inner.Set(null);
            return null;
        }
    }
    public void Set(SessionInfo? session)
    {
        if (session is not null) activation.Demand();
        inner.Set(session);
    }
    public void RecordActivity() { if (activation.IsActive) inner.RecordActivity(); else inner.Set(null); }
    public bool TryUpdate(SessionInfo expected, SessionInfo updated) => activation.IsActive && inner.TryUpdate(expected, updated);
}

public sealed record LicenseAccountSummary(Guid Id, string Username, string CustomerName, string Contact,
    string Status, Guid? InstallationId, DateTimeOffset CreatedUtc, DateTimeOffset? LastCheckedUtc);
public sealed record LicenseHistory(DateTimeOffset AtUtc, string Actor, string Action, string Note);

public interface ILicenseAdministrationStore
{
    Task<IReadOnlyList<LicenseAccountSummary>> ListAsync(string? search, CancellationToken cancellationToken);
    Task<IReadOnlyList<LicenseHistory>> HistoryAsync(Guid accountId, CancellationToken cancellationToken);
    Task ChangeAsync(Guid accountId, string action, string note, string actor, CancellationToken cancellationToken);
}

public sealed class LicenseAdministrationService(ILicenseAdministrationStore store, IAuthorizer authorizer)
{
    public const string ManagePermission = "licensing.manage";
    public Task<IReadOnlyList<LicenseAccountSummary>> ListAsync(string? search = null, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(ManagePermission);
        return store.ListAsync(search, cancellationToken);
    }
    public Task<IReadOnlyList<LicenseHistory>> HistoryAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(ManagePermission);
        return store.HistoryAsync(accountId, cancellationToken);
    }
    public Task ChangeAsync(Guid accountId, string action, string note, CancellationToken cancellationToken = default)
    {
        var admin = authorizer.Demand(ManagePermission);
        if (action is not ("Activate" or "Suspend" or "ResetInstallation")) throw new WorkspaceValidationException("Choose a valid action.");
        if (string.IsNullOrWhiteSpace(note) || note.Trim().Length > 500) throw new WorkspaceValidationException("Enter a reason of 1–500 characters.");
        return store.ChangeAsync(accountId, action, note.Trim(), admin.Username, cancellationToken);
    }
}
