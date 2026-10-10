using Nexora.Application.Security;

namespace Nexora.Tests.Security;

public sealed class Pbkdf2PasswordHasherTests
{
    // A reduced work factor keeps the suite fast; the production default is asserted separately.
    private readonly Pbkdf2PasswordHasher _hasher = new(1_000);

    [Fact]
    public void Production_default_meets_owasp_minimum() =>
        Assert.True(Pbkdf2PasswordHasher.DefaultIterations >= 600_000);

    [Fact]
    public void Default_constructor_uses_production_work_factor()
    {
        var hash = new Pbkdf2PasswordHasher().Hash("correct horse battery");
        Assert.Equal("600000", hash.Split('$')[1]);
    }

    [Fact]
    public void Verifies_the_original_password_only()
    {
        var hash = _hasher.Hash("correct horse battery");

        Assert.Equal(PasswordVerification.Succeeded, _hasher.Verify("correct horse battery", hash));
        Assert.Equal(PasswordVerification.Failed, _hasher.Verify("correct horse batterY", hash));
        Assert.Equal(PasswordVerification.Failed, _hasher.Verify("", hash));
    }

    [Fact]
    public void Uses_a_unique_salt_per_hash()
    {
        var first = _hasher.Hash("same password here");
        var second = _hasher.Hash("same password here");

        Assert.NotEqual(first, second);
        Assert.NotEqual(first.Split('$')[2], second.Split('$')[2]);
    }

    [Fact]
    public void Does_not_store_the_password()
    {
        var hash = _hasher.Hash("plaintext-secret-123");
        Assert.DoesNotContain("plaintext-secret-123", hash, StringComparison.Ordinal);
    }

    [Fact]
    public void Requests_rehash_when_stored_work_factor_is_lower()
    {
        var weak = new Pbkdf2PasswordHasher(500).Hash("correct horse battery");
        Assert.Equal(PasswordVerification.SucceededRehashNeeded, _hasher.Verify("correct horse battery", weak));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("PBKDF2-SHA256$abc$AAAA$AAAA")]
    [InlineData("PBKDF2-SHA256$1000$***$AAAA")]
    [InlineData("BCRYPT$1000$AAAA$AAAA")]
    [InlineData("PBKDF2-SHA256$1000$AAAA$")]
    public void Malformed_hashes_fail_without_throwing(string stored) =>
        Assert.Equal(PasswordVerification.Failed, _hasher.Verify("anything at all", stored));
}

public sealed class PasswordPolicyTests
{
    [Theory]
    [InlineData("short1")]
    [InlineData("aaaaaaaaaaaa")]
    [InlineData("xx-admin-xx-1")]
    public void Rejects_weak_passwords(string password) => Assert.NotNull(PasswordPolicy.Check(password, "admin"));

    [Fact]
    public void Rejects_overlong_passwords() =>
        Assert.NotNull(PasswordPolicy.Check(string.Concat(Enumerable.Repeat("abcd", 33)), "admin"));

    [Theory]
    [InlineData("quiet ledger 42")]
    [InlineData("Tr0ub4dor&3xyz")]
    public void Accepts_reasonable_passwords(string password) => Assert.Null(PasswordPolicy.Check(password, "admin"));
}
