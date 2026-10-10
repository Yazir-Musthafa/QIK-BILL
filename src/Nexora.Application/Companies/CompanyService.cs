using Microsoft.Extensions.Logging;
using Nexora.Application.Abstractions;
using Nexora.Application.Security;
using Nexora.Domain.Companies;
using Nexora.Domain.Numbering;
using Nexora.Domain.Security;

namespace Nexora.Application.Companies;

public sealed record CompanyDetails(Guid Id, CompanyProfile Profile, bool IsDemo, int FinancialYearStartYear, bool HasLogo);

public sealed record BranchInput(Guid? Id, string Code, string Name, string StateCode, string Address);

public sealed record BranchRow(Guid Id, string Code, string Name, string StateCode, string Address, bool IsActive);

public sealed record WarehouseInput(Guid? Id, string Code, string Name, Guid? BranchId);

public sealed record WarehouseRow(Guid Id, string Code, string Name, Guid? BranchId, string BranchName, bool IsActive);

public sealed record NumberingRow(Guid Id, string DocumentType, string Name, string Prefix, int Padding, bool ResetEachFinancialYear, string Example);

public sealed record NewCompanyRequest(string Name, string StateCode, int FinancialYearStartYear);

/// <summary>Profile, branches, warehouses and numbering of the signed-in company, and creating new companies.</summary>
public sealed class CompanyService(
    IUnitOfWorkFactory units,
    IWorkspaceStore store,
    CompanyProvisioner provisioner,
    IAuthorizer authorizer,
    IClock clock,
    ILogger<CompanyService> logger)
{
    public async Task<CompanyDetails> GetDetailsAsync(CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.CompanyView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var company = await RequireCompanyAsync(unit, session, cancellationToken);
        return new CompanyDetails(company.Id, company.Profile, company.IsDemo, company.FinancialYearStartYear, company.Logo is not null);
    }

    public async Task UpdateProfileAsync(CompanyProfile profile, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.CompanyManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var company = await RequireCompanyAsync(unit, session, cancellationToken);
        company.UpdateProfile(profile);
        await unit.CommitAsync(cancellationToken);
    }

    public async Task SetLogoAsync(byte[]? logo, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.CompanyManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var company = await RequireCompanyAsync(unit, session, cancellationToken);
        company.SetLogo(logo);
        await unit.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BranchRow>> ListBranchesAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.CompanyView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var branches = await unit.ListAsync<Branch>(cancellationToken: cancellationToken);
        return branches.OrderBy(b => b.Code).Select(b => new BranchRow(b.Id, b.Code, b.Name, b.StateCode, b.Address, b.IsActive)).ToList();
    }

    public async Task<Guid> SaveBranchAsync(BranchInput input, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.CompanyManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var code = (input.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (await unit.AnyAsync<Branch>(b => b.Code == code && b.Id != (input.Id ?? Guid.Empty), cancellationToken))
            throw new WorkspaceValidationException($"Another branch already uses the code {code}.");

        Branch branch;
        if (input.Id is { } id)
        {
            branch = await unit.FindAsync<Branch>(id, cancellationToken) ?? throw NotFound("branch");
            branch.Update(input.Code!, input.Name, input.StateCode, input.Address);
        }
        else
        {
            branch = new Branch(Guid.NewGuid(), session.CompanyId, input.Code!, input.Name, input.StateCode, input.Address);
            unit.Add(branch);
        }

        await unit.CommitAsync(cancellationToken);
        return branch.Id;
    }

    public async Task SetBranchActiveAsync(Guid branchId, bool active, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.CompanyManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var branch = await unit.FindAsync<Branch>(branchId, cancellationToken) ?? throw NotFound("branch");
        branch.SetActive(active);
        await unit.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WarehouseRow>> ListWarehousesAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.CompanyView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var branches = (await unit.ListAsync<Branch>(cancellationToken: cancellationToken)).ToDictionary(b => b.Id, b => b.Name);
        var warehouses = await unit.ListAsync<Warehouse>(cancellationToken: cancellationToken);
        return warehouses.OrderBy(w => w.Code)
            .Select(w => new WarehouseRow(w.Id, w.Code, w.Name, w.BranchId,
                w.BranchId is { } b && branches.TryGetValue(b, out var name) ? name : string.Empty, w.IsActive))
            .ToList();
    }

    public async Task<Guid> SaveWarehouseAsync(WarehouseInput input, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.CompanyManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var code = (input.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (await unit.AnyAsync<Warehouse>(w => w.Code == code && w.Id != (input.Id ?? Guid.Empty), cancellationToken))
            throw new WorkspaceValidationException($"Another warehouse already uses the code {code}.");
        // The branch lookup is company-filtered, so a branch id from another company is "not found" here,
        // and the composite foreign key would reject it in the database as well.
        if (input.BranchId is { } branchId && await unit.FindAsync<Branch>(branchId, cancellationToken) is null) throw NotFound("branch");

        Warehouse warehouse;
        if (input.Id is { } id)
        {
            warehouse = await unit.FindAsync<Warehouse>(id, cancellationToken) ?? throw NotFound("warehouse");
            warehouse.Update(input.Code!, input.Name, input.BranchId);
        }
        else
        {
            warehouse = new Warehouse(Guid.NewGuid(), session.CompanyId, input.Code!, input.Name, input.BranchId);
            unit.Add(warehouse);
        }

        await unit.CommitAsync(cancellationToken);
        return warehouse.Id;
    }

    public async Task SetWarehouseActiveAsync(Guid warehouseId, bool active, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.CompanyManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var warehouse = await unit.FindAsync<Warehouse>(warehouseId, cancellationToken) ?? throw NotFound("warehouse");
        warehouse.SetActive(active);
        await unit.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<NumberingRow>> ListNumberingAsync(CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.CompanyView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var rules = await unit.ListAsync<NumberingRule>(cancellationToken: cancellationToken);
        var year = new FinancialYear(session.FinancialYearStartYear);
        return rules
            .Select(r => new NumberingRow(r.Id, r.DocumentType, DocumentTypes.Find(r.DocumentType)?.Name ?? r.DocumentType,
                r.Prefix, r.Padding, r.ResetEachFinancialYear, r.Format(1, year)))
            .OrderBy(r => r.Name)
            .ToList();
    }

    public async Task UpdateNumberingAsync(Guid ruleId, string prefix, int padding, bool resetEachFinancialYear,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.CompanyManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var rule = await unit.FindAsync<NumberingRule>(ruleId, cancellationToken) ?? throw NotFound("numbering rule");
        rule.Update(prefix, padding, resetEachFinancialYear);
        await unit.CommitAsync(cancellationToken);
    }

    /// <summary>Creates a company; the signed-in user becomes its administrator.</summary>
    public async Task<CompanySummary> CreateCompanyAsync(NewCompanyRequest request, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.CompanyCreate);
        var errors = SetupValidator.ValidateCompany(request.Name, request.StateCode, request.FinancialYearStartYear);
        if (errors.Count > 0) throw new WorkspaceValidationException(errors.First().Value);

        var now = clock.UtcNow;
        var company = new Company(Guid.NewGuid(), request.Name, request.StateCode, new FinancialYear(request.FinancialYearStartYear), false, now);
        await store.ProvisionCompanyAsync(provisioner.Create(company, session.UserId, now), cancellationToken);
        logger.LogInformation("Company created; user {UserId}; company {CompanyId}", session.UserId, company.Id);
        return new CompanySummary(company.Id, company.Name, false, company.FinancialYearStartYear, "Administrator");
    }

    private static async Task<Company> RequireCompanyAsync(IUnitOfWork unit, SessionInfo session, CancellationToken cancellationToken) =>
        await unit.FindAsync<Company>(session.CompanyId, cancellationToken)
        ?? throw new WorkspaceValidationException("The company could not be found. Sign in again.");

    private static WorkspaceValidationException NotFound(string what) =>
        new($"That {what} no longer exists. Refresh the list and try again.");
}
