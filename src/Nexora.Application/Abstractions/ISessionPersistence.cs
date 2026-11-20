using Nexora.Application;

namespace Nexora.Application.Abstractions;

/// <summary>
/// Securely stores the most recently authenticated session so the desktop can resume it after a restart.
/// The store is bound to the current Windows user; it never contains a password.
/// </summary>
public enum SessionKind
{
    User,
    Administrator
}

public sealed record PersistedSession(SessionKind Kind, Guid UserId, Guid CompanyId, DateTime? PasswordChangedUtc,
    DateTimeOffset IssuedUtc, string Token);

public interface ISessionPersistence
{
    /// <summary>Returns the persisted session, or null when none exists or it cannot be read.</summary>
    PersistedSession? Load();

    /// <summary>Stores or replaces the persisted session.</summary>
    void Save(PersistedSession session);

    /// <summary>Removes any persisted session.</summary>
    void Clear();
}