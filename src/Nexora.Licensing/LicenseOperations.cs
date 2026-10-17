using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Nexora.Application;
using Nexora.Application.Licensing;
using Nexora.Application.Security;
using Nexora.Domain.Users;
using Nexora.Licensing.Persistence;

namespace Nexora.Licensing;

public sealed class LicenseSigner(string pem)
{
    public SignedActivation Sign(ActivationGrant grant)
    {
        using var key = RSA.Create();
        key.ImportFromPem(pem);
        return ActivationSignature.Sign(grant, key);
    }
}

public sealed class LicenseOperations(LicenseDb db, IPasswordHasher hasher, LicenseSigner signer, TimeProvider time)
{
    // Single server instance with SQLite; serialises login/activation changes and their audit writes.
    internal static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly string DummyHash = new Pbkdf2PasswordHasher().Hash(Guid.NewGuid().ToString());

    public async Task<ActivationReply> RegisterAsync(ActivationRegistration request, CancellationToken ct = default)
    {
        try { Username.Validate(request.Username); }
        catch (Nexora.Domain.Common.DomainException ex) { throw new WorkspaceValidationException(ex.Message); }
        if (PasswordPolicy.Check(request.Password, request.Username) is { } error) throw new WorkspaceValidationException(error);
        if (string.IsNullOrWhiteSpace(request.CustomerName) || request.CustomerName.Trim().Length > 120
            || string.IsNullOrWhiteSpace(request.Contact) || request.Contact.Trim().Length > 160 || request.InstallationId == Guid.Empty)
            throw new WorkspaceValidationException("Enter your business name, contact details and a valid installation.");
        await Gate.WaitAsync(ct);
        try
        {
            var username = Username.Normalize(request.Username);
            if (await db.Accounts.AnyAsync(x => x.Username == username, ct))
                throw new WorkspaceValidationException("Registration was not completed. Try another username or sign in to your existing account.");
            var token = NewToken();
            var account = new LicenseAccount { Id = Guid.NewGuid(), Username = username, PasswordHash = hasher.Hash(request.Password),
                CustomerName = request.CustomerName.Trim(), Contact = request.Contact.Trim(), InstallationId = request.InstallationId,
                CreatedUtc = time.GetUtcNow().UtcDateTime, TokenHash = HashToken(token) };
            db.Accounts.Add(account);
            db.Audit.Add(new LicenseAudit { AccountId = account.Id, Actor = username, Action = "Registered", Note = "Awaiting administrator activation.", AtUtc = account.CreatedUtc });
            await db.SaveChangesAsync(ct);
            return Reply(account, token);
        }
        finally { Gate.Release(); }
    }

    public async Task<ActivationReply> LoginAsync(ActivationLogin request, CancellationToken ct = default)
    {
        if (request.Password is null || request.Password.Length > 128 || string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > 32)
            throw new WorkspaceValidationException("The username or password is incorrect.");
        await Gate.WaitAsync(ct);
        try
        {
            var username = Username.Normalize(request.Username);
            var account = await db.Accounts.SingleOrDefaultAsync(x => x.Username == username, ct);
            var now = time.GetUtcNow().UtcDateTime;
            var verification = hasher.Verify(request.Password, account?.PasswordHash ?? DummyHash);
            if (account is null || account.LockedUntilUtc > now) throw new WorkspaceValidationException("The username or password is incorrect, or the account is temporarily locked.");
            if (verification == PasswordVerification.Failed)
            {
                account.FailedAttempts++;
                if (account.FailedAttempts >= 5) { account.LockedUntilUtc = now.AddMinutes(15); account.FailedAttempts = 0; }
                await db.SaveChangesAsync(ct);
                throw new WorkspaceValidationException("The username or password is incorrect, or the account is temporarily locked.");
            }
            if (request.InstallationId == Guid.Empty || (account.InstallationId.HasValue && account.InstallationId != request.InstallationId))
                throw new WorkspaceValidationException("This account belongs to another installation. Ask your admin to reset the installation before moving computers.");
            account.InstallationId = request.InstallationId;
            account.FailedAttempts = 0;
            account.LockedUntilUtc = null;
            if (verification == PasswordVerification.SucceededRehashNeeded) account.PasswordHash = hasher.Hash(request.Password);
            var token = NewToken();
            account.TokenHash = HashToken(token);
            account.LastCheckedUtc = now;
            await db.SaveChangesAsync(ct);
            return Reply(account, token);
        }
        finally { Gate.Release(); }
    }

    public async Task<ActivationReply> CheckAsync(ActivationCheck request, CancellationToken ct = default)
    {
        if (request.Token is null || request.Token.Length != 64) return SignInRequired();
        await Gate.WaitAsync(ct);
        try
        {
            var hash = HashToken(request.Token);
            var account = await db.Accounts.SingleOrDefaultAsync(x => x.TokenHash == hash && x.InstallationId == request.InstallationId, ct);
            if (account is null) return SignInRequired();
            account.LastCheckedUtc = time.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
            return Reply(account);
        }
        finally { Gate.Release(); }
    }

    private ActivationReply Reply(LicenseAccount account, string? token = null)
    {
        if (account.Status != "Active") return new(account.Status, account.Status == "Pending"
            ? "Your account needs activation. Contact the admin to activate QIK Suite."
            : "Your account is suspended. Contact the admin to restore access.", token);
        var now = time.GetUtcNow();
        return new("Active", "Activated. You can use QIK Suite offline for 30 days.", token,
            signer.Sign(new ActivationGrant(account.Id, account.InstallationId!.Value, now, now + ActivationSignature.OfflinePeriod)));
    }
    private static ActivationReply SignInRequired() => new("SignInRequired", "Sign in to your activation account again.");
    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
