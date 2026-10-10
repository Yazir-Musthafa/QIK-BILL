namespace Nexora.Application.Security;

/// <summary>
/// Length-based policy in line with NIST SP 800-63B: no composition rules, a minimum length,
/// and rejection of passwords that are trivially derived from the username.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 10;
    public const int MaxLength = 128;

    /// <summary>Returns a user-facing problem description, or <c>null</c> if the password is acceptable.</summary>
    public static string? Check(string? password, string? username)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
            return $"Use at least {MinLength} characters.";
        if (password.Length > MaxLength)
            return $"Use {MaxLength} characters or fewer.";
        if (password.Distinct().Count() < 4)
            return "Use a less repetitive password.";
        var name = username?.Trim();
        if (!string.IsNullOrEmpty(name) && password.Contains(name, StringComparison.OrdinalIgnoreCase))
            return "The password must not contain the username.";
        return null;
    }
}
