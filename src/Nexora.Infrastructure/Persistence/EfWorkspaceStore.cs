using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexora.Application.Abstractions;
using Nexora.Domain.Companies;
using Nexora.Domain.Settings;
using Nexora.Domain.Users;
using Nexora.Infrastructure.Persistence.Providers;

namespace Nexora.Infrastructure.Persistence;

/// <summary>Each call uses its own short-lived context, so the store is safe as a singleton.</summary>
public sealed class EfWorkspaceStore(
    IDbContextFactory<NexoraDbContext> contextFactory,
    IDatabaseProvider provider,
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
        return await context.Users.AnyAsync(cancellationToken);
    }

    public async Task CreateInitialWorkspaceAsync(IReadOnlyCollection<Company> companies, User administrator, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (await context.Users.AnyAsync(cancellationToken)) throw new WorkspaceAlreadyInitializedException();

        context.Companies.AddRange(companies);
        context.Users.Add(administrator);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<User?> FindUserByNormalizedUsernameAsync(string normalizedUsername, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Users.AsNoTracking()
            .SingleOrDefaultAsync(u => u.NormalizedUsername == normalizedUsername, cancellationToken);
    }

    public async Task UpdateUserAsync(User user, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.Users.Update(user);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Company>> GetCompaniesAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Companies.AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<Company?> FindCompanyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Companies.AsNoTracking().SingleOrDefaultAsync(c => c.Id == companyId, cancellationToken);
    }

    public async Task DeleteCompanyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Companies.Where(c => c.Id == companyId).ExecuteDeleteAsync(cancellationToken);
    }

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
}
