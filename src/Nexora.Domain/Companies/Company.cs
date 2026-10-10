using System.Net.Mail;
using System.Text.RegularExpressions;
using Nexora.Domain.Common;

namespace Nexora.Domain.Companies;

/// <summary>Editable company details. Empty strings mean "not provided".</summary>
public sealed record CompanyProfile(
    string Name,
    string LegalName,
    string StateCode,
    string AddressLine1,
    string AddressLine2,
    string City,
    string Pincode,
    string Phone,
    string Email,
    string Pan,
    string Gstin);

public sealed partial class Company : IAudited
{
    public const int NameMaxLength = 120;
    public const int AddressMaxLength = 120;
    public const int LogoMaxBytes = 512 * 1024;

    private Company()
    {
        Name = string.Empty;
        StateCode = string.Empty;
    }

    public Company(Guid id, string name, string stateCode, FinancialYear firstFinancialYear, bool isDemo, DateTime createdUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Company id is required.", nameof(id));
        Id = id;
        Name = NormalizeName(name);
        StateCode = ValidateStateCode(stateCode);
        FinancialYearStartYear = firstFinancialYear.StartYear;
        IsDemo = isDemo;
        CreatedUtc = DateTime.SpecifyKind(createdUtc, DateTimeKind.Utc);
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    /// <summary>Two-digit GST state code of the principal place of business.</summary>
    public string StateCode { get; private set; }

    /// <summary>Start year of the first financial year the company keeps books for.</summary>
    public int FinancialYearStartYear { get; private set; }

    /// <summary>Demonstration companies hold sample data only and may be deleted.</summary>
    public bool IsDemo { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    public string LegalName { get; private set; } = string.Empty;

    public string AddressLine1 { get; private set; } = string.Empty;

    public string AddressLine2 { get; private set; } = string.Empty;

    public string City { get; private set; } = string.Empty;

    public string Pincode { get; private set; } = string.Empty;

    public string Phone { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public string Pan { get; private set; } = string.Empty;

    public string Gstin { get; private set; } = string.Empty;

    /// <summary>PNG or JPEG logo for printed documents, at most <see cref="LogoMaxBytes"/>.</summary>
    public byte[]? Logo { get; private set; }

    public FinancialYear FirstFinancialYear => new(FinancialYearStartYear);

    public CompanyProfile Profile => new(Name, LegalName, StateCode, AddressLine1, AddressLine2, City, Pincode, Phone, Email, Pan, Gstin);

    public void Rename(string name) => Name = NormalizeName(name);

    /// <summary>Validates and applies the profile. GSTIN must match the state and the PAN when both are given.</summary>
    public void UpdateProfile(CompanyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var name = NormalizeName(profile.Name);
        var state = ValidateStateCode(profile.StateCode);
        var pan = Clean(profile.Pan).ToUpperInvariant();
        var gstin = Clean(profile.Gstin).ToUpperInvariant();
        var pincode = Clean(profile.Pincode);
        var email = Clean(profile.Email);
        var phone = Clean(profile.Phone);

        if (pan.Length > 0 && !TaxIdentifiers.IsValidPan(pan)) throw new DomainException("PAN must be 10 characters, for example ABCDE1234F.");
        if (gstin.Length > 0)
        {
            if (!TaxIdentifiers.IsValidGstin(gstin)) throw new DomainException("GSTIN is not valid. Check the 15 characters, including the last check character.");
            if (TaxIdentifiers.StateCodeOfGstin(gstin) != state)
                throw new DomainException("The GSTIN state code does not match the company's state.");
            if (pan.Length > 0 && TaxIdentifiers.PanOfGstin(gstin) != pan)
                throw new DomainException("The PAN inside the GSTIN does not match the company PAN.");
        }

        if (pincode.Length > 0 && !PincodePattern().IsMatch(pincode)) throw new DomainException("PIN code must be 6 digits.");
        if (phone.Length > 0 && !PhonePattern().IsMatch(phone)) throw new DomainException("Phone can contain digits, spaces, + and - only.");
        if (email.Length > 0 && !IsEmail(email)) throw new DomainException("Enter a valid email address.");

        Name = name;
        StateCode = state;
        LegalName = Limit(profile.LegalName, NameMaxLength, "Legal name");
        AddressLine1 = Limit(profile.AddressLine1, AddressMaxLength, "Address");
        AddressLine2 = Limit(profile.AddressLine2, AddressMaxLength, "Address");
        City = Limit(profile.City, 60, "City");
        Pincode = pincode;
        Phone = phone;
        Email = email;
        Pan = pan.Length > 0 ? pan : gstin.Length > 0 ? TaxIdentifiers.PanOfGstin(gstin) : string.Empty;
        Gstin = gstin;
    }

    public void SetLogo(byte[]? logo)
    {
        if (logo is { Length: > LogoMaxBytes }) throw new DomainException("The logo must be 512 KB or smaller.");
        if (logo is { Length: > 0 } && !IsPngOrJpeg(logo)) throw new DomainException("The logo must be a PNG or JPEG image.");
        Logo = logo is { Length: > 0 } ? logo : null;
    }

    private static bool IsPngOrJpeg(byte[] data) =>
        (data.Length > 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) ||
        (data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF);

    private static bool IsEmail(string value) =>
        value.Length <= 120 && MailAddress.TryCreate(value, out var address) && address.Address == value && value.Contains('.');

    private static string Clean(string? value) => (value ?? string.Empty).Trim();

    private static string Limit(string? value, int max, string field)
    {
        var trimmed = Clean(value);
        if (trimmed.Length > max) throw new DomainException($"{field} must be {max} characters or fewer.");
        return trimmed;
    }

    private static string NormalizeName(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0) throw new DomainException("Enter the company name.");
        if (trimmed.Length > NameMaxLength)
            throw new DomainException($"Company name must be {NameMaxLength} characters or fewer.");
        return trimmed;
    }

    private static string ValidateStateCode(string? stateCode)
    {
        if (!IndianState.IsValidCode(stateCode)) throw new DomainException("Choose the state of the principal place of business.");
        return stateCode!;
    }

    [GeneratedRegex("^[1-9][0-9]{5}$")]
    private static partial Regex PincodePattern();

    [GeneratedRegex(@"^\+?[0-9][0-9 \-]{5,19}$")]
    private static partial Regex PhonePattern();
}
