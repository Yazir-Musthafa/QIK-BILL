using Nexora.Domain.Common;

namespace Nexora.Domain.Users;

/// <summary>
/// An installation-wide user account. Roles, permissions and per-company access arrive in Phase 2;
/// in Phase 1 every active user is an administrator of all companies.
/// </summary>
public sealed class User
{
    public const int DisplayNameMaxLength = 80;
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private User()
    {
        Username = string.Empty;
        NormalizedUsername = string.Empty;
        DisplayName = string.Empty;
        PasswordHash = string.Empty;
    }

    public User(Guid id, string username, string displayName, string passwordHash, DateTime createdUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("User id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new ArgumentException("Password hash is required.", nameof(passwordHash));
        Id = id;
        Username = Users.Username.Validate(username);
        NormalizedUsername = Users.Username.Normalize(Username);
        DisplayName = ValidateDisplayName(displayName);
        PasswordHash = passwordHash;
        IsActive = true;
        CreatedUtc = DateTime.SpecifyKind(createdUtc, DateTimeKind.Utc);
    }

    public Guid Id { get; private set; }

    public string Username { get; private set; }

    public string NormalizedUsername { get; private set; }

    public string DisplayName { get; private set; }

    public string PasswordHash { get; private set; }

    public bool IsActive { get; private set; }

    public int FailedSignInCount { get; private set; }

    public DateTime? LockoutEndUtc { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    public DateTime? LastSignInUtc { get; private set; }

    public bool IsLockedOut(DateTime nowUtc) => LockoutEndUtc is { } end && end > nowUtc;

    /// <summary>Records a failed attempt; locks the account after <see cref="MaxFailedAttempts"/>.</summary>
    public void RecordFailedSignIn(DateTime nowUtc)
    {
        if (LockoutEndUtc is { } end && end <= nowUtc)
        {
            // A previous lockout has expired; start counting afresh.
            LockoutEndUtc = null;
            FailedSignInCount = 0;
        }

        FailedSignInCount++;
        if (FailedSignInCount >= MaxFailedAttempts)
        {
            LockoutEndUtc = nowUtc + LockoutDuration;
        }
    }

    public void RecordSuccessfulSignIn(DateTime nowUtc)
    {
        FailedSignInCount = 0;
        LockoutEndUtc = null;
        LastSignInUtc = nowUtc;
    }

    /// <summary>Replaces the hash, for example when the stored hash used weaker parameters.</summary>
    public void ReplacePasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new ArgumentException("Password hash is required.", nameof(passwordHash));
        PasswordHash = passwordHash;
    }

    private static string ValidateDisplayName(string? displayName)
    {
        var trimmed = (displayName ?? string.Empty).Trim();
        if (trimmed.Length == 0) throw new DomainException("Enter your full name.");
        if (trimmed.Length > DisplayNameMaxLength)
            throw new DomainException($"Name must be {DisplayNameMaxLength} characters or fewer.");
        return trimmed;
    }
}
