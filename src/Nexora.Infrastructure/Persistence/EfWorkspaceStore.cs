using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexora.Application.Abstractions;
using Nexora.Domain.Common;
using Nexora.Domain.Companies;
using Nexora.Domain.Settings;
using Nexora.Domain.Users;
using Nexora.Infrastructure.Persistence.Providers;

namespace Nexora.Infrastructure.Persistence;

/// <summary>
/// Installation-level operations that run before a company session exists or span companies.
/// This is the only class that may use IgnoreQueryFilters or <see cref="NexoraDbContext.SystemScope"/>
/// (enforced by a source-scanning test). Each call uses its own short-lived context.
/// </summary>
public sealed class EfWorkspaceStore(
    IDbContextFactory<NexoraDbContext> contextFactory,
    IDatabaseProvider provider,
    ISessionContext session,
    ILogger<EfWorkspaceStore> logger) : IWorkspaceStore
{
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        await context.Database.MigrateAsync(cancellationToken);
        await provider.AfterMigrateAsync(context, cancellationToken);
        logger.LogInformation("Database ready; provider {Provider}; migrations applied {Count}", provider.Name, pending.Count);
    }

    public async Task<bool> HasAnyUserAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Users.IgnoreQueryFilters().AnyAsync(cancellationToken);
    }

    public async Task CreateInitialWorkspaceAsync(IReadOnlyCollection<CompanyProvisioning> companies, User administrator,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (await context.Users.IgnoreQueryFilters().AnyAsync(cancellationToken)) throw new WorkspaceAlreadyInitializedException();

        context.SystemScope = true;
        context.Audit.ActorOverride = (administrator.Id, administrator.Username);
        context.Audit.CompanyOverride = companies.First().Company.Id;
        context.Users.Add(administrator);
        foreach (var provisioning in companies) AddProvisioning(context, provisioning);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ProvisionCompanyAsync(CompanyProvisioning provisioning, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.SystemScope = true;
        AddProvisioning(context, provisioning);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<User?> FindUserByNormalizedUsernameAsync(string normalizedUsername, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Users.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(u => u.NormalizedUsername == normalizedUsername, cancellationToken);
    }

    public async Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Users.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
    }

    public async Task UpdateUserAsync(User user, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var stored = await context.Users.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(u => u.Id == user.Id, cancellationToken)
            ?? throw new ConcurrencyConflictException("This user no longer exists.");
        // Attach with the stored values as originals so the audit trail records what actually changed.
        var entry = context.Users.Attach(user);
        entry.OriginalValues.SetValues(stored);
        entry.DetectChanges();
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CompanyAccess>> GetAccessAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await (
                from membership in context.Memberships.IgnoreQueryFilters()
                join company in context.Companies.IgnoreQueryFilters() on membership.CompanyId equals company.Id
                join role in context.Roles.IgnoreQueryFilters() on membership.RoleId equals role.Id
                where membership.UserId == userId && membership.IsActive && role.CompanyId == company.Id
                select new { company, role, membership })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return rows.Select(r => new CompanyAccess(r.company, r.role, r.membership)).ToList();
    }

    public async Task DeleteCompanyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        // Foreign keys cascade from Companies to every company-owned table, including its audit trail.
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Companies.IgnoreQueryFilters().Where(c => c.Id == companyId).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Company>> ListAllCompaniesAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Companies.IgnoreQueryFilters().AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListAllUserIdsAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Users.IgnoreQueryFilters().Select(u => u.Id).ToListAsync(cancellationToken);
    }

    public async Task<bool> CompanyHasAsync(Guid companyId, Type entityType, CancellationToken cancellationToken)
    {
        if (!typeof(ICompanyOwned).IsAssignableFrom(entityType)) throw new ArgumentException("Type must be company-owned.", nameof(entityType));
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var method = typeof(EfWorkspaceStore).GetMethod(nameof(CompanyHasCoreAsync), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(entityType);
        return await (Task<bool>)method.Invoke(null, [context, companyId, cancellationToken])!;
    }

    public async Task AddToCompanyAsync(Guid companyId, IReadOnlyList<object> entities, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.SystemScope = true;
        context.Audit.ActorOverride = (null, "system upgrade");
        context.Audit.CompanyOverride = companyId;
        foreach (var entity in entities) context.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Company upgraded; company {CompanyId}; rows added {Count}", companyId, entities.Count);
    }

    private static Task<bool> CompanyHasCoreAsync<T>(NexoraDbContext context, Guid companyId, CancellationToken cancellationToken)
        where T : class, ICompanyOwned =>
        context.Set<T>().IgnoreQueryFilters().AnyAsync(e => e.CompanyId == companyId, cancellationToken);

    public async Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Settings.AsNoTracking()
            .Where(s => s.Key == key).Select(s => s.Value).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task SetSettingsAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var keys = values.Keys.ToList();
        var existing = await context.Settings.Where(s => keys.Contains(s.Key)).ToDictionaryAsync(s => s.Key, cancellationToken);
        foreach (var (key, value) in values)
        {
            if (existing.TryGetValue(key, out var setting)) setting.Update(value);
            else context.Settings.Add(new AppSetting(key, value));
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private void AddProvisioning(NexoraDbContext context, CompanyProvisioning provisioning)
    {
        if (session.Current is null && context.Audit.ActorOverride is null)
            throw new InvalidOperationException("Provisioning requires a signed-in user or a setup actor.");
        context.Companies.Add(provisioning.Company);
        foreach (var entity in provisioning.Entities) context.Add(entity);
    }
}
