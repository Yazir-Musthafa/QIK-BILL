using System.Security.Cryptography;
using System.Text;
using Nexora.Application;

namespace Nexora.Infrastructure.Backup;

public interface ISecretProtector
{
    string Protect(string secret);
    string Unprotect(string protectedSecret);
}

/// <summary>DPAPI binds saved passwords to the current Windows account. There is no plaintext fallback.</summary>
public sealed class WindowsSecretProtector : ISecretProtector
{
    private static readonly byte[] Purpose = Encoding.UTF8.GetBytes("QIKSuite.BackupPassword.v1");

    public string Protect(string secret)
    {
        if (!OperatingSystem.IsWindows()) throw new WorkspaceValidationException("Saving backup passwords requires Windows.");
        var bytes = Encoding.UTF8.GetBytes(secret);
        try { return Convert.ToBase64String(ProtectedData.Protect(bytes, Purpose, DataProtectionScope.CurrentUser)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public string Unprotect(string protectedSecret)
    {
        if (!OperatingSystem.IsWindows()) throw new WorkspaceValidationException("Opening saved backup passwords requires Windows.");
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedSecret), Purpose, DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(bytes); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            throw new WorkspaceValidationException("The saved backup password cannot be opened by this Windows account. Enter the password again in Backup settings.");
        }
    }
}
