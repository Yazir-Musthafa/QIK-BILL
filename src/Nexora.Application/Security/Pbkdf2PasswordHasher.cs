using System.Globalization;
using System.Security.Cryptography;

namespace Nexora.Application.Security;

/// <summary>
/// PBKDF2-HMAC-SHA256 with a unique 16-byte salt per password. Stored format:
/// <c>PBKDF2-SHA256$iterations$saltBase64$hashBase64</c>. The iteration count follows the
/// OWASP Password Storage Cheat Sheet recommendation for PBKDF2-HMAC-SHA256.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    public const int DefaultIterations = 600_000;
    private const string Prefix = "PBKDF2-SHA256";
    private const int SaltSize = 16;
    private const int HashSize = 32;

    private readonly int _iterations;

    public Pbkdf2PasswordHasher() : this(DefaultIterations)
    {
    }

    /// <summary>Allows a different work factor, for example a lower one in unit tests.</summary>
    public Pbkdf2PasswordHasher(int iterations)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);
        _iterations = iterations;
    }

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, _iterations, HashAlgorithmName.SHA256, HashSize);
        return string.Join('$', Prefix, _iterations.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public PasswordVerification Verify(string password, string storedHash)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (string.IsNullOrEmpty(storedHash)) return PasswordVerification.Failed;

        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix) return PasswordVerification.Failed;
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations) || iterations < 1)
            return PasswordVerification.Failed;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return PasswordVerification.Failed;
        }

        if (expected.Length == 0) return PasswordVerification.Failed;

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected)) return PasswordVerification.Failed;

        return iterations < _iterations || salt.Length < SaltSize
            ? PasswordVerification.SucceededRehashNeeded
            : PasswordVerification.Succeeded;
    }
}
