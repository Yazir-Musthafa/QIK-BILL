using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nexora.Application;
using Nexora.Application.Abstractions;

namespace Nexora.Infrastructure.SessionPersistence;

/// <summary>
/// DPAPI-protected session file bound to the current Windows user. There is no plaintext fallback and no
/// persistence on platforms without DPAPI.
/// </summary>
public sealed class FileSessionPersistence(string path) : ISessionPersistence
{
    private static readonly byte[] Purpose = Encoding.UTF8.GetBytes("QIKSuite.Session.v1");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public PersistedSession? Load()
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(path)) return null;
        try
        {
            var protectedBytes = File.ReadAllBytes(path);
            var bytes = ProtectedData.Unprotect(protectedBytes, Purpose, DataProtectionScope.CurrentUser);
            try
            {
                var json = Encoding.UTF8.GetString(bytes);
                var record = JsonSerializer.Deserialize<PersistedRecord>(json, JsonOptions);
                if (record is null || string.IsNullOrWhiteSpace(record.Token)) return null;
                return new PersistedSession(
                    Enum.TryParse<SessionKind>(record.Kind, ignoreCase: true, out var kind) ? kind : SessionKind.User,
                    record.UserId,
                    record.CompanyId,
                    record.PasswordChangedUtc,
                    record.IssuedUtc == default ? DateTimeOffset.UtcNow : record.IssuedUtc,
                    record.Token);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        catch
        {
            // A unreadable or tampered file is treated as no session, then removed.
            try { File.Delete(path); } catch { /* best effort */ }
            return null;
        }
    }

    public void Save(PersistedSession session)
    {
        if (!OperatingSystem.IsWindows()) return;
        ArgumentNullException.ThrowIfNull(session);
        var record = new PersistedRecord(session.Kind.ToString(), session.UserId, session.CompanyId, session.PasswordChangedUtc,
            session.IssuedUtc, session.Token);
        var json = JsonSerializer.Serialize(record, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        try
        {
            var protectedBytes = ProtectedData.Protect(bytes, Purpose, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temp = path + ".tmp";
            File.WriteAllBytes(temp, protectedBytes);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public void Clear()
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort */ }
    }

    private sealed record PersistedRecord(string Kind, Guid UserId, Guid CompanyId, DateTime? PasswordChangedUtc,
        DateTimeOffset IssuedUtc, string Token);
}