namespace Nexora.Application.Security;

public enum PasswordVerification
{
    Failed,
    Succeeded,

    /// <summary>The password matched but the stored hash uses outdated parameters.</summary>
    SucceededRehashNeeded,
}

public interface IPasswordHasher
{
    string Hash(string password);

    PasswordVerification Verify(string password, string storedHash);
}
