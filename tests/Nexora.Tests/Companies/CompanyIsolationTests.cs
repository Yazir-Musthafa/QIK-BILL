using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexora.Application;
using Nexora.Application.Abstractions;
using Nexora.Application.Audit;
using Nexora.Application.Companies;
using Nexora.Application.Users;
using Nexora.Domain.Audit;
using Nexora.Domain.Common;
using Nexora.Domain.Companies;
using Nexora.Domain.Numbering;
using Nexora.Domain.Security;
using Nexora.Domain.Users;
using Nexora.Infrastructure.Persistence;
using Nexora.Tests.Infrastructure;
using Nexora.Tests.Workspace;

namespace Nexora.Tests.Companies;

/// <summary>
/// Proves that one company cannot read or write another company's data: through the services,
/// through the unit of work directly, and in the database itself.
/// </summary>
public sealed class CompanyIsolationTests : IAsyncLifetime
{
    private readonly SqliteTestDatabase _db = new();
    private Guid _companyA;
    private Guid _companyB;
    private Guid _branchA;
    private Guid _branchB;
    private Guid _bOnlyUserId;

    private IWorkspaceService Workspace => _db.Workspace;

    private CompanyService Companies => _db.Get<CompanyService>();

    private IUnitOfWorkFactory Units => _db.Get<IUnitOfWorkFactory>();

    public async Task InitializeAsync()
    {
        await Workspace.InitializeAsync();
        _companyA = (await Workspace.CompleteSetupAsync(WorkspaceServiceTests.ValidRequest())).CompanyId;
        _branchA = await Companies.SaveBranchAsync(new BranchInput(null, "HO", "Head office A", "27", ""));
        _companyB = (await Companies.CreateCompanyAsync(new NewCompanyRequest("Beta Stores", "29", 2026))).Id;

        await Workspace.SwitchCompanyAsync(_companyB);
        _branchB = await Companies.SaveBranchAsync(new BranchInput(null, "HO", "Head office B", "29", ""));
        var readOnly = (await _db.Get<RoleService>().ListAsync()).Single(r => r.Name == "Read-only");
        _bOnlyUserId = await _db.Get<UserService>().CreateAsync(new NewUserRequest("bina", "Bina Shah", "temporary pass 1", readOnly.Id));
        await Companies.UpdateProfileAsync(new CompanyProfile("Beta Stores", "", "29", "", "", "Bengaluru", "", "", "", "", ""));

        await Workspace.SwitchCompanyAsync(_companyA);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Services_only_return_the_signed_in_companys_rows()
    {
        var branches = await Companies.ListBranchesAsync();
        Assert.Equal([_branchA], branches.Select(b => b.Id));

        var users = await _db.Get<UserService>().ListAsync();
        Assert.DoesNotContain(users, u => u.UserId == _bOnlyUserId);

        var details = await Companies.GetDetailsAsync();
        Assert.Equal(_companyA, details.Id);

        var audit = await _db.Get<AuditService>().QueryAsync(new AuditQuery());
        Assert.DoesNotContain(audit, e => e.Summary.Contains("Head office B", StringComparison.Ordinal));
        Assert.DoesNotContain(audit, e => e.Summary.Contains("bina", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unit_of_work_cannot_find_rows_of_another_company_even_by_id()
    {
        await using var unit = await Units.BeginAsync(readOnly: true);

        Assert.Null(await unit.FindAsync<Branch>(_branchB));
        Assert.Null(await unit.FindAsync<Company>(_companyB));
        Assert.Null(await unit.FindAsync<User>(_bOnlyUserId));
        Assert.NotNull(await unit.FindAsync<Branch>(_branchA));
        Assert.All(await unit.ListAsync<Role>(), r => Assert.Equal(_companyA, r.CompanyId));
        Assert.All(await unit.ListAsync<NumberingRule>(), r => Assert.Equal(_companyA, r.CompanyId));
        Assert.All(await unit.ListAsync<CompanyMembership>(), m => Assert.Equal(_companyA, m.CompanyId));
        Assert.All(await unit.ListAsync<AuditEntry>(), e => Assert.Equal(_companyA, e.CompanyId));
        Assert.False(await unit.AnyAsync<Branch>(b => b.CompanyId == _companyB));
    }

    [Fact]
    public async Task Services_treat_ids_from_another_company_as_not_found()
    {
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Companies.SetBranchActiveAsync(_branchB, false));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() =>
            Companies.SaveWarehouseAsync(new WarehouseInput(null, "W1", "Main", _branchB)));

        await Workspace.SwitchCompanyAsync(_companyB);
        Assert.True((await Companies.ListBranchesAsync()).Single().IsActive);
    }

    [Fact]
    public async Task Writing_a_row_for_another_company_is_blocked()
    {
        await using var unit = await Units.BeginAsync();
        unit.Add(new Branch(Guid.NewGuid(), _companyB, "X1", "Intruder", "29", ""));

        await Assert.ThrowsAsync<CompanyIsolationException>(() => unit.CommitAsync());
    }

    [Fact]
    public async Task Moving_a_row_to_another_company_is_blocked()
    {
        await using var unit = await Units.BeginAsync();
        var rule = (await unit.ListAsync<NumberingRule>()).First();
        typeof(NumberingRule).GetProperty(nameof(NumberingRule.CompanyId))!.SetValue(rule, _companyB);

        await Assert.ThrowsAsync<CompanyIsolationException>(() => unit.CommitAsync());
    }

    [Fact]
    public async Task Moving_a_row_that_is_part_of_a_composite_company_key_is_also_refused()
    {
        await using var unit = await Units.BeginAsync();
        var branch = await unit.FindAsync<Branch>(_branchA);
        typeof(Branch).GetProperty(nameof(Branch.CompanyId))!.SetValue(branch, _companyB);

        // EF refuses to change a key column before the isolation guard even runs.
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => unit.CommitAsync());
    }

    [Fact]
    public async Task Nothing_is_visible_without_a_session()
    {
        Workspace.SignOut();
        await using var unit = await Units.BeginAsync(readOnly: true);

        Assert.Empty(await unit.ListAsync<Branch>());
        Assert.Empty(await unit.ListAsync<Company>());
        Assert.Empty(await unit.ListAsync<User>());
        Assert.Empty(await unit.ListAsync<AuditEntry>());
    }

    [Fact]
    public async Task Database_rejects_a_warehouse_that_points_to_another_companys_branch()
    {
        using var connection = _db.OpenRawConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Warehouses (Id, CompanyId, Code, Name, BranchId, IsActive) VALUES ($id, $company, 'W9', 'Cross', $branch, 1)";
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("$company", _companyA.ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("$branch", _branchB.ToString().ToUpperInvariant());

        var error = Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
        Assert.Contains("FOREIGN KEY", error.Message, StringComparison.OrdinalIgnoreCase);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Database_rejects_a_membership_with_another_companys_role()
    {
        string roleOfB;
        using var connection = _db.OpenRawConnection();
        using (var query = connection.CreateCommand())
        {
            query.CommandText = "SELECT Id FROM Roles WHERE CompanyId = $company LIMIT 1";
            query.Parameters.AddWithValue("$company", _companyB.ToString().ToUpperInvariant());
            roleOfB = (string)query.ExecuteScalar()!;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO CompanyMemberships (Id, CompanyId, UserId, RoleId, IsActive, CreatedUtc) " +
                              "VALUES ($id, $company, $user, $role, 1, '2026-10-10 00:00:00')";
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("$company", _companyA.ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("$user", _bOnlyUserId.ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("$role", roleOfB);

        Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
        await Task.CompletedTask;
    }

    /// <summary>Guards against adding a business table and forgetting isolation.</summary>
    [Fact]
    public async Task Every_company_owned_table_is_filtered_and_keyed_to_companies()
    {
        var factory = _db.Get<IDbContextFactory<NexoraDbContext>>();
        await using var context = await factory.CreateDbContextAsync();
        var owned = context.Model.GetEntityTypes().Where(t => typeof(ICompanyOwned).IsAssignableFrom(t.ClrType)).ToList();
        Assert.True(owned.Count >= 7);

        var setMethod = typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!;
        foreach (var entityType in owned)
        {
            var set = (IQueryable<object>)setMethod.MakeGenericMethod(entityType.ClrType).Invoke(context, null)!;
            var sql = set.ToQueryString();
            Assert.Matches(@"WHERE[\s\S]*""CompanyId""\s*=\s*@ef_filter__CurrentCompanyId", sql);
            Assert.Contains(entityType.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(Company)
                && fk.Properties.Single().Name == nameof(ICompanyOwned.CompanyId));
        }
    }

    /// <summary>Only the installation-level store and the audit recorder may bypass the company filter.</summary>
    [Fact]
    public void Filter_bypasses_are_confined_to_the_workspace_store()
    {
        var root = Path.Combine(FindRepositoryRoot(), "src");
        string[] allowed = ["EfWorkspaceStore.cs", "AuditRecorder.cs", "NexoraDbContext.cs"];
        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => !allowed.Contains(Path.GetFileName(f)))
            .Where(f =>
            {
                var text = File.ReadAllText(f);
                return text.Contains("IgnoreQueryFilters", StringComparison.Ordinal) || text.Contains("SystemScope", StringComparison.Ordinal);
            })
            .Select(Path.GetFileName)
            .ToList();
        Assert.Empty(offenders);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Nexora.sln"))) directory = directory.Parent;
        return directory!.FullName;
    }
}
