using Nexora.Application.Security;
using Nexora.Domain.Common;
using Nexora.Domain.Companies;
using Nexora.Domain.Users;

namespace Nexora.Application;

/// <summary>
/// Field-level validation for the first-run wizard. The wizard uses it per step for inline messages;
/// <see cref="WorkspaceService"/> re-validates the whole request before anything is stored.
/// </summary>
public static class SetupValidator
{
    public const string CompanyName = nameof(SetupRequest.CompanyName);
    public const string StateCode = nameof(SetupRequest.StateCode);
    public const string FinancialYear = nameof(SetupRequest.FinancialYearStartYear);
    public const string DisplayName = nameof(SetupRequest.DisplayName);
    public const string Username = nameof(SetupRequest.Username);
    public const string Password = nameof(SetupRequest.Password);
    public const string PasswordConfirmation = "PasswordConfirmation";

    public static IReadOnlyDictionary<string, string> ValidateCompany(string? companyName, string? stateCode, int financialYearStartYear)
    {
        var errors = new Dictionary<string, string>();
        var name = companyName?.Trim() ?? string.Empty;
        if (name.Length == 0) errors[CompanyName] = "Enter the company name.";
        else if (name.Length > Company.NameMaxLength) errors[CompanyName] = $"Use {Company.NameMaxLength} characters or fewer.";
        if (!IndianState.IsValidCode(stateCode)) errors[StateCode] = "Choose the state of the principal place of business.";
        if (financialYearStartYear is < Domain.Companies.FinancialYear.MinimumStartYear or > Domain.Companies.FinancialYear.MaximumStartYear)
            errors[FinancialYear] = "Choose the first financial year.";
        return errors;
    }

    public static IReadOnlyDictionary<string, string> ValidateAdministrator(string? displayName, string? username, string? password, string? confirmation)
    {
        var errors = new Dictionary<string, string>();
        var name = displayName?.Trim() ?? string.Empty;
        if (name.Length == 0) errors[DisplayName] = "Enter your full name.";
        else if (name.Length > User.DisplayNameMaxLength) errors[DisplayName] = $"Use {User.DisplayNameMaxLength} characters or fewer.";

        try
        {
            Domain.Users.Username.Validate(username);
        }
        catch (DomainException exception)
        {
            errors[Username] = exception.Message;
        }

        if (PasswordPolicy.Check(password, username) is { } problem) errors[Password] = problem;
        else if (!string.Equals(password, confirmation, StringComparison.Ordinal)) errors[PasswordConfirmation] = "The passwords do not match.";
        return errors;
    }
}
