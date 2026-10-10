using System.Text.RegularExpressions;
using Nexora.Domain.Accounting;
using Nexora.Domain.Common;
using Nexora.Domain.Companies;

namespace Nexora.Domain.Billing;

public enum PartyKind
{
    Customer,
    Supplier,
}

public sealed record PartyDetails(string Name, string Phone = "", string Email = "", string Gstin = "", string StateCode = "",
    string Address = "", string City = "", string Pincode = "", decimal CreditLimit = 0);

/// <summary>
/// A customer or supplier. Each party has its own ledger account under Sundry Debtors (customers) or Sundry Creditors
/// (suppliers), created with the party, so its balance is always read from the general ledger.
/// The state decides place of supply: the GSTIN's state when a GSTIN is given.
/// </summary>
public sealed partial class Party : ICompanyOwned, IAudited
{
    private Party()
    {
        Name = Phone = Email = Gstin = StateCode = Address = City = Pincode = string.Empty;
    }

    public Party(Guid id, Guid companyId, PartyKind kind, Guid accountId, PartyDetails details) : this()
    {
        Id = id;
        CompanyId = companyId;
        Kind = kind;
        AccountId = accountId;
        IsActive = true;
        Update(details);
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public PartyKind Kind { get; private set; }

    public string Name { get; private set; }

    public string Phone { get; private set; }

    public string Email { get; private set; }

    /// <summary>Empty for unregistered parties.</summary>
    public string Gstin { get; private set; }

    /// <summary>Two-digit GST state code; empty when unknown (treated as the company's own state).</summary>
    public string StateCode { get; private set; }

    public string Address { get; private set; }

    public string City { get; private set; }

    public string Pincode { get; private set; }

    /// <summary>Informational in Phase 5: shown with the balance, not enforced.</summary>
    public decimal CreditLimit { get; private set; }

    /// <summary>The party's own ledger under Sundry Debtors or Sundry Creditors.</summary>
    public Guid AccountId { get; private set; }

    public bool IsActive { get; private set; }

    public bool IsRegistered => Gstin.Length > 0;

    public void Update(PartyDetails details)
    {
        var name = (details.Name ?? string.Empty).Trim();
        if (name.Length is 0 or > 120) throw new DomainException("Enter a name of up to 120 characters.");
        var phone = (details.Phone ?? string.Empty).Trim();
        if (phone.Length > 20) throw new DomainException("Phone can have up to 20 characters.");
        var email = (details.Email ?? string.Empty).Trim();
        if (email.Length > 120 || (email.Length > 0 && !EmailPattern().IsMatch(email))) throw new DomainException("Enter a valid email address.");
        var gstin = (details.Gstin ?? string.Empty).Trim().ToUpperInvariant();
        if (gstin.Length > 0 && !TaxIdentifiers.IsValidGstin(gstin)) throw new DomainException("The GSTIN is not valid. Check the state code, PAN and check character.");
        var state = (details.StateCode ?? string.Empty).Trim();
        if (gstin.Length > 0)
        {
            var gstinState = TaxIdentifiers.StateCodeOfGstin(gstin);
            if (state.Length > 0 && state != gstinState) throw new DomainException("The state does not match the GSTIN's state code.");
            state = gstinState;
        }
        else if (state.Length > 0 && !IndianState.IsValidCode(state)) throw new DomainException("Choose a valid state.");

        var address = (details.Address ?? string.Empty).Trim();
        if (address.Length > 300) throw new DomainException("Address can have up to 300 characters.");
        var city = (details.City ?? string.Empty).Trim();
        if (city.Length > 60) throw new DomainException("City can have up to 60 characters.");
        var pincode = (details.Pincode ?? string.Empty).Trim();
        if (pincode.Length > 0 && !PincodePattern().IsMatch(pincode)) throw new DomainException("Pincode must be 6 digits.");
        Money.EnsureValid(details.CreditLimit, "Credit limit");

        Name = name;
        Phone = phone;
        Email = email;
        Gstin = gstin;
        StateCode = state;
        Address = address;
        City = city;
        Pincode = pincode;
        CreditLimit = details.CreditLimit;
    }

    public void SetActive(bool active) => IsActive = active;

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex("^[1-9][0-9]{5}$")]
    private static partial Regex PincodePattern();
}
