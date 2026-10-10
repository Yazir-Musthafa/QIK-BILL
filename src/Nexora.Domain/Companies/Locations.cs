using System.Text.RegularExpressions;
using Nexora.Domain.Common;

namespace Nexora.Domain.Companies;

internal static partial class LocationRules
{
    public static string Code(string? code)
    {
        var value = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (!CodePattern().IsMatch(value)) throw new DomainException("Code must be 1 to 10 letters or digits.");
        return value;
    }

    public static string Name(string? name, string what)
    {
        var value = (name ?? string.Empty).Trim();
        if (value.Length == 0) throw new DomainException($"Enter the {what} name.");
        if (value.Length > 80) throw new DomainException($"The {what} name must be 80 characters or fewer.");
        return value;
    }

    [GeneratedRegex("^[A-Z0-9]{1,10}$")]
    private static partial Regex CodePattern();
}

public sealed class Branch : ICompanyOwned, IAudited
{
    private Branch()
    {
        Code = Name = StateCode = Address = string.Empty;
    }

    public Branch(Guid id, Guid companyId, string code, string name, string stateCode, string address)
    {
        if (id == Guid.Empty || companyId == Guid.Empty) throw new ArgumentException("Ids are required.");
        Id = id;
        CompanyId = companyId;
        IsActive = true;
        Code = string.Empty;
        Name = string.Empty;
        StateCode = string.Empty;
        Address = string.Empty;
        Update(code, name, stateCode, address);
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Code { get; private set; }

    public string Name { get; private set; }

    /// <summary>State of the branch; a branch in another state may need its own GSTIN (Phase 5).</summary>
    public string StateCode { get; private set; }

    public string Address { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(string code, string name, string stateCode, string address)
    {
        if (!IndianState.IsValidCode(stateCode)) throw new DomainException("Choose the branch state.");
        var trimmedAddress = (address ?? string.Empty).Trim();
        if (trimmedAddress.Length > 240) throw new DomainException("Address must be 240 characters or fewer.");
        Code = LocationRules.Code(code);
        Name = LocationRules.Name(name, "branch");
        StateCode = stateCode;
        Address = trimmedAddress;
    }

    public void SetActive(bool active) => IsActive = active;
}

public sealed class Warehouse : ICompanyOwned, IAudited
{
    private Warehouse()
    {
        Code = Name = string.Empty;
    }

    public Warehouse(Guid id, Guid companyId, string code, string name, Guid? branchId)
    {
        if (id == Guid.Empty || companyId == Guid.Empty) throw new ArgumentException("Ids are required.");
        Id = id;
        CompanyId = companyId;
        IsActive = true;
        Code = string.Empty;
        Name = string.Empty;
        Update(code, name, branchId);
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Code { get; private set; }

    public string Name { get; private set; }

    /// <summary>Optional branch the warehouse belongs to. The database enforces that it is in the same company.</summary>
    public Guid? BranchId { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(string code, string name, Guid? branchId)
    {
        Code = LocationRules.Code(code);
        Name = LocationRules.Name(name, "warehouse");
        BranchId = branchId == Guid.Empty ? null : branchId;
    }

    public void SetActive(bool active) => IsActive = active;
}
