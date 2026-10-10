using Nexora.Domain.Common;
using Nexora.Domain.Companies;
using Nexora.Domain.Users;

namespace Nexora.Tests.Domain;

public sealed class FinancialYearTests
{
    [Fact]
    public void Runs_from_first_April_to_thirty_first_March()
    {
        var year = new FinancialYear(2026);

        Assert.Equal(new DateOnly(2026, 4, 1), year.StartDate);
        Assert.Equal(new DateOnly(2027, 3, 31), year.EndDate);
        Assert.Equal("2026-27", year.Label);
    }

    [Theory]
    [InlineData(2026, 3, 31, 2025)]
    [InlineData(2026, 4, 1, 2026)]
    [InlineData(2027, 1, 15, 2026)]
    [InlineData(2099, 12, 31, 2099)]
    public void Containing_picks_the_year_that_includes_the_date(int y, int m, int d, int expectedStart)
    {
        var date = new DateOnly(y, m, d);
        var year = FinancialYear.Containing(date);

        Assert.Equal(expectedStart, year.StartYear);
        Assert.True(year.Contains(date));
    }

    [Fact]
    public void Label_handles_century_rollover() => Assert.Equal("2099-00", new FinancialYear(2099).Label);

    [Theory]
    [InlineData(1999)]
    [InlineData(2101)]
    public void Rejects_out_of_range_years(int startYear) =>
        Assert.Throws<DomainException>(() => new FinancialYear(startYear));
}

public sealed class IndianStateTests
{
    [Fact]
    public void Codes_are_unique_two_digit_strings()
    {
        Assert.All(IndianState.All, s => Assert.Matches("^[0-9]{2}$", s.Code));
        Assert.Equal(IndianState.All.Count, IndianState.All.Select(s => s.Code).Distinct().Count());
    }

    [Theory]
    [InlineData("27", true)]
    [InlineData("32", true)]
    [InlineData("25", false)] // merged into 26 in 2020
    [InlineData("99", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Validates_codes(string? code, bool expected) => Assert.Equal(expected, IndianState.IsValidCode(code));
}

public sealed class CompanyTests
{
    [Fact]
    public void Trims_name_and_keeps_utc()
    {
        var company = new Company(Guid.NewGuid(), "  Acme Traders  ", "27", new FinancialYear(2026), false, new DateTime(2026, 10, 10, 9, 0, 0));

        Assert.Equal("Acme Traders", company.Name);
        Assert.Equal(DateTimeKind.Utc, company.CreatedUtc.Kind);
        Assert.Equal(2026, company.FirstFinancialYear.StartYear);
    }

    [Theory]
    [InlineData("", "27")]
    [InlineData("   ", "27")]
    [InlineData("Acme", "00")]
    public void Rejects_invalid_input(string name, string state) =>
        Assert.Throws<DomainException>(() => new Company(Guid.NewGuid(), name, state, new FinancialYear(2026), false, DateTime.UtcNow));

    [Fact]
    public void Rejects_overlong_name() =>
        Assert.Throws<DomainException>(() =>
            new Company(Guid.NewGuid(), new string('a', Company.NameMaxLength + 1), "27", new FinancialYear(2026), false, DateTime.UtcNow));
}

public sealed class UsernameTests
{
    [Theory]
    [InlineData("admin")]
    [InlineData("a.k-sharma_2")]
    public void Accepts_valid_usernames(string value) => Assert.Equal(value, Username.Validate($" {value} "));

    [Theory]
    [InlineData("ab")]
    [InlineData("has space")]
    [InlineData("émile")]
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_invalid_usernames(string? value) => Assert.Throws<DomainException>(() => Username.Validate(value));

    [Fact]
    public void Normalisation_is_case_insensitive() => Assert.Equal(Username.Normalize("Admin "), Username.Normalize("aDMIN"));
}

public sealed class UserLockoutTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc);

    private static User NewUser() => new(Guid.NewGuid(), "admin", "Admin", "hash", Now);

    [Fact]
    public void Locks_after_maximum_failed_attempts()
    {
        var user = NewUser();
        for (var i = 1; i < User.MaxFailedAttempts; i++)
        {
            user.RecordFailedSignIn(Now);
            Assert.False(user.IsLockedOut(Now));
        }

        user.RecordFailedSignIn(Now);

        Assert.True(user.IsLockedOut(Now));
        Assert.Equal(Now + User.LockoutDuration, user.LockoutEndUtc);
        Assert.False(user.IsLockedOut(Now + User.LockoutDuration));
    }

    [Fact]
    public void Expired_lockout_restarts_the_count()
    {
        var user = NewUser();
        for (var i = 0; i < User.MaxFailedAttempts; i++) user.RecordFailedSignIn(Now);

        var later = Now + User.LockoutDuration + TimeSpan.FromSeconds(1);
        user.RecordFailedSignIn(later);

        Assert.Equal(1, user.FailedSignInCount);
        Assert.False(user.IsLockedOut(later));
    }

    [Fact]
    public void Successful_sign_in_clears_failures()
    {
        var user = NewUser();
        user.RecordFailedSignIn(Now);
        user.RecordFailedSignIn(Now);

        user.RecordSuccessfulSignIn(Now);

        Assert.Equal(0, user.FailedSignInCount);
        Assert.Null(user.LockoutEndUtc);
        Assert.Equal(Now, user.LastSignInUtc);
    }
}
