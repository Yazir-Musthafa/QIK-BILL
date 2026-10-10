using System.Text.RegularExpressions;
using Nexora.Domain.Common;

namespace Nexora.Domain.Users;

public static partial class Username
{
    public const int MinLength = 3;
    public const int MaxLength = 32;

    /// <summary>Trims and validates a username; returns the display form.</summary>
    public static string Validate(string? username)
    {
        var trimmed = (username ?? string.Empty).Trim();
        if (trimmed.Length is < MinLength or > MaxLength)
            throw new DomainException($"Username must be {MinLength} to {MaxLength} characters.");
        if (!Allowed().IsMatch(trimmed))
            throw new DomainException("Username can contain letters, digits, dots, hyphens and underscores only.");
        return trimmed;
    }

    /// <summary>Case-insensitive lookup key.</summary>
    public static string Normalize(string? username) => (username ?? string.Empty).Trim().ToUpperInvariant();

    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex Allowed();
}
