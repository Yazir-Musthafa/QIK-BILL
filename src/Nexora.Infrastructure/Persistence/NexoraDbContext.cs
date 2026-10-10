using Microsoft.EntityFrameworkCore;
using Nexora.Domain.Companies;
using Nexora.Domain.Settings;
using Nexora.Domain.Users;

namespace Nexora.Infrastructure.Persistence;

/// <summary>
/// Provider-neutral model. The database provider is chosen by an <see cref="Providers.IDatabaseProvider"/>;
/// nothing here may use provider-specific column types or SQL.
/// </summary>
public sealed class NexoraDbContext(DbContextOptions<NexoraDbContext> options) : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();

    public DbSet<User> Users => Set<User>();

    public DbSet<AppSetting> Settings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NexoraDbContext).Assembly);
}
