namespace Nexora.Domain.Common;

/// <summary>
/// Marks a business table row that belongs to exactly one company. Infrastructure applies a global
/// query filter on <see cref="CompanyId"/> and refuses writes for any other company.
/// </summary>
public interface ICompanyOwned
{
    Guid CompanyId { get; }
}

/// <summary>Changes to entities with this marker are written to the audit trail automatically.</summary>
public interface IAudited;
