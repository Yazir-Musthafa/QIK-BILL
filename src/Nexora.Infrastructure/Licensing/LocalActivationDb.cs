using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Nexora.Application;

namespace Nexora.Infrastructure.Licensing;

// Global installation security metadata, separate from company-owned business data.
public sealed class InstallationActivation
{
    public int Id { get; set; } = 1;
    public string Status { get; set; } = "Pending";
    public int FailedAdminAttempts { get; set; }
    public DateTime? AdminLockedUntilUtc { get; set; }
}
public sealed class LocalActivationAudit
{
    public long Id { get; set; }
    public DateTime AtUtc { get; set; }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string Note { get; set; } = "";
}
public sealed class LocalActivationDb(DbContextOptions<LocalActivationDb> options) : DbContext(options)
{
    public DbSet<InstallationActivation> Installations => Set<InstallationActivation>();
    public DbSet<LocalActivationAudit> History => Set<LocalActivationAudit>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<InstallationActivation>().HasKey(x => x.Id);
        model.Entity<InstallationActivation>().ToTable(t => t.HasCheckConstraint("CK_SingleInstallation", "Id = 1"));
        model.Entity<InstallationActivation>().ToTable(t => t.HasCheckConstraint("CK_ActivationStatus", "Status IN ('Pending', 'Active', 'Suspended')"));
        model.Entity<LocalActivationAudit>().HasKey(x => x.Id);
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess) { Guard(); return base.SaveChanges(acceptAllChangesOnSuccess); }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    { Guard(); return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken); }
    private void Guard()
    {
        if (ChangeTracker.Entries<LocalActivationAudit>().Any(x => x.State is EntityState.Deleted or EntityState.Modified))
            throw new WorkspaceValidationException("Activation history is append-only.");
    }
}
public sealed class LocalActivationDesignFactory : IDesignTimeDbContextFactory<LocalActivationDb>
{
    public LocalActivationDb CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<LocalActivationDb>().UseSqlite("Data Source=local-activation-design.db").Options);
}
