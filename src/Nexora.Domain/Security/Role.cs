using Nexora.Domain.Common;

namespace Nexora.Domain.Security;

/// <summary>A named set of permissions inside one company. System roles are created for every company.</summary>
public sealed class Role : ICompanyOwned, IAudited
{
    public const string AdministratorKey = "administrator";

    private HashSet<string> _permissions = new(StringComparer.Ordinal);

    private Role()
    {
        Name = Description = string.Empty;
    }

    public Role(Guid id, Guid companyId, string name, string description, string? systemKey,
        IEnumerable<string> permissions, decimal maxDiscountPercent)
    {
        if (id == Guid.Empty || companyId == Guid.Empty) throw new ArgumentException("Ids are required.");
        Id = id;
        CompanyId = companyId;
        SystemKey = systemKey;
        Name = string.Empty;
        Description = string.Empty;
        Rename(name, description);
        ApplyPermissions(permissions);
        SetMaxDiscountPercent(maxDiscountPercent);
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Name { get; private set; }

    public string Description { get; private set; }

    /// <summary>Set for the built-in roles; null for roles created by users.</summary>
    public string? SystemKey { get; private set; }

    public bool IsSystem => SystemKey is not null;

    public bool IsAdministrator => SystemKey == AdministratorKey;

    /// <summary>Highest discount (percent of line value) this role may give when it holds sales.discount.</summary>
    public decimal MaxDiscountPercent { get; private set; }

    /// <summary>Stored permissions. The administrator role always has the whole catalogue.</summary>
    public IReadOnlySet<string> Permissions
    {
        get => IsAdministrator ? Security.Permissions.All.Select(p => p.Key).ToHashSet(StringComparer.Ordinal) : _permissions;
        private set => _permissions = new HashSet<string>(value, StringComparer.Ordinal);
    }

    public bool Has(string permission) => Permissions.Contains(permission);

    public void Rename(string name, string description)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0) throw new DomainException("Enter the role name.");
        if (trimmed.Length > 60) throw new DomainException("Role name must be 60 characters or fewer.");
        var text = (description ?? string.Empty).Trim();
        if (text.Length > 200) throw new DomainException("Description must be 200 characters or fewer.");
        if (IsSystem && Name.Length > 0 && trimmed != Name) throw new DomainException("Built-in roles cannot be renamed.");
        Name = trimmed;
        Description = text;
    }

    public void SetPermissions(IEnumerable<string> permissions)
    {
        if (IsAdministrator) throw new DomainException("The Administrator role always has every permission.");
        ApplyPermissions(permissions);
    }

    public void SetMaxDiscountPercent(decimal percent)
    {
        if (percent is < 0 or > 100) throw new DomainException("Discount limit must be between 0 and 100 percent.");
        if (decimal.Round(percent, 2) != percent) throw new DomainException("Discount limit can have at most 2 decimal places.");
        MaxDiscountPercent = IsAdministrator ? 100 : percent;
    }

    private void ApplyPermissions(IEnumerable<string> permissions)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var permission in permissions)
        {
            if (!Security.Permissions.IsKnown(permission)) throw new DomainException($"Unknown permission '{permission}'.");
            set.Add(permission);
        }

        _permissions = set;
    }
}

/// <summary>A user's access to one company, through exactly one role.</summary>
public sealed class CompanyMembership : ICompanyOwned, IAudited
{
    private CompanyMembership()
    {
    }

    public CompanyMembership(Guid id, Guid companyId, Guid userId, Guid roleId, DateTime createdUtc)
    {
        if (id == Guid.Empty || companyId == Guid.Empty || userId == Guid.Empty || roleId == Guid.Empty)
            throw new ArgumentException("Ids are required.");
        Id = id;
        CompanyId = companyId;
        UserId = userId;
        RoleId = roleId;
        IsActive = true;
        CreatedUtc = DateTime.SpecifyKind(createdUtc, DateTimeKind.Utc);
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid UserId { get; private set; }

    public Guid RoleId { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    public void ChangeRole(Guid roleId)
    {
        if (roleId == Guid.Empty) throw new ArgumentException("Role is required.", nameof(roleId));
        RoleId = roleId;
    }

    public void SetActive(bool active) => IsActive = active;
}

/// <summary>Built-in role definitions, copied into every new company.</summary>
public static class RoleTemplates
{
    public sealed record Template(string Key, string Name, string Description, IReadOnlyList<string> Permissions, decimal MaxDiscountPercent);

    public static IReadOnlyList<Template> All { get; } =
    [
        new(Role.AdministratorKey, "Administrator", "Full access, including users, roles and company settings.",
            Security.Permissions.All.Select(p => p.Key).ToList(), 100m),
        new("accountant", "Accountant", "Accounts, vouchers, financial years, reports and the audit trail.",
        [
            Security.Permissions.CompanyView, Security.Permissions.AuditView,
            Security.Permissions.AccountsView, Security.Permissions.AccountsManage,
            Security.Permissions.VouchersView, Security.Permissions.VouchersCreate, Security.Permissions.VouchersPost, Security.Permissions.VouchersReverse,
            Security.Permissions.FinancialYearsManage, Security.Permissions.ReportsView, Security.Permissions.DataExport,
            Security.Permissions.SalesView, Security.Permissions.PurchasesView, Security.Permissions.InventoryView, Security.Permissions.InventoryViewCost,
            Security.Permissions.ExpensesView, Security.Permissions.ExpensesManage, Security.Permissions.ExpensesApprove,
        ], 0m),
        new("billing-operator", "Billing Operator", "Sales entry with limited discounts. No cost prices.",
        [
            Security.Permissions.CompanyView, Security.Permissions.SalesView, Security.Permissions.SalesCreate, Security.Permissions.SalesDiscount,
            Security.Permissions.InventoryView, Security.Permissions.CrmView,
        ], 5m),
        new("inventory-manager", "Inventory Manager", "Products, stock, purchases and stock adjustments.",
        [
            Security.Permissions.CompanyView, Security.Permissions.InventoryView, Security.Permissions.InventoryManage,
            Security.Permissions.InventoryAdjust, Security.Permissions.InventoryViewCost,
            Security.Permissions.PurchasesView, Security.Permissions.PurchasesCreate, Security.Permissions.ReportsView,
        ], 0m),
        new("service-staff", "Service Staff", "Service jobs and customer contacts.",
        [
            Security.Permissions.CompanyView, Security.Permissions.ServiceView, Security.Permissions.ServiceManage,
            Security.Permissions.CrmView, Security.Permissions.InventoryView,
        ], 0m),
        new("read-only", "Read-only", "Can view everything except costs and the audit trail. Cannot change or export.",
            Security.Permissions.All.Where(p => p.Action == "View" && p.Key != Security.Permissions.AuditView).Select(p => p.Key).ToList(), 0m),
    ];

    public static Role Create(Template template, Guid companyId) =>
        new(Guid.NewGuid(), companyId, template.Name, template.Description, template.Key, template.Permissions, template.MaxDiscountPercent);
}
