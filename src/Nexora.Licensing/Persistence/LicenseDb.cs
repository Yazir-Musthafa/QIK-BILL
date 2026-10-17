using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Nexora.Application;
using Nexora.Application.Licensing;

namespace Nexora.Licensing.Persistence;

// Global licensing identities in a separate server database, like the desktop's global User identities.
// These are not company business rows and never reference or contain customer accounting data.
public sealed class LicenseAccount
{
    public Guid Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string Contact { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public Guid? InstallationId { get; set; }
    public string? TokenHash { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? LastCheckedUtc { get; set; }
    public int FailedAttempts { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
}

public sealed class LicenseAudit
{
    public long Id { get; set; }
    public Guid AccountId { get; set; }
    public DateTime AtUtc { get; set; }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string Note { get; set; } = "";
}

public sealed class LicenseDb(DbContextOptions<LicenseDb> options) : DbContext(options)
{
    public DbSet<LicenseAccount> Accounts => Set<LicenseAccount>();
    public DbSet<LicenseAudit> Audit => Set<LicenseAudit>();
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardAudit();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardAudit();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
    private void GuardAudit()
    {
        if (ChangeTracker.Entries<LicenseAudit>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new WorkspaceValidationException("Licensing history is append-only.");
    }
    protected override void OnModelCreating(ModelBuilder model)
    {
        var account = model.Entity<LicenseAccount>();
        account.HasKey(x => x.Id);
        account.HasIndex(x => x.Username).IsUnique();
        account.HasIndex(x => x.TokenHash).IsUnique();
        account.Property(x => x.Username).HasMaxLength(32);
        account.Property(x => x.CustomerName).HasMaxLength(120);
        account.Property(x => x.Contact).HasMaxLength(160);
        var audit = model.Entity<LicenseAudit>();
        audit.HasKey(x => x.Id);
        audit.HasIndex(x => new { x.AccountId, x.AtUtc });
        audit.HasOne<LicenseAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class LicenseDbDesignFactory : IDesignTimeDbContextFactory<LicenseDb>
{
    public LicenseDb CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<LicenseDb>().UseSqlite("Data Source=licensing-design.db").Options);
}

public sealed class LicenseAdministrationStore(LicenseDb db, TimeProvider time) : ILicenseAdministrationStore
{
    public async Task<IReadOnlyList<LicenseAccountSummary>> ListAsync(string? search, CancellationToken cancellationToken)
    {
        var query = db.Accounts.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.Username.Contains(search) || x.CustomerName.Contains(search) || x.Contact.Contains(search));
        return (await query.OrderByDescending(x => x.CreatedUtc).Take(500).ToListAsync(cancellationToken))
            .Select(x => new LicenseAccountSummary(x.Id, x.Username, x.CustomerName, x.Contact, x.Status, x.InstallationId,
                new DateTimeOffset(DateTime.SpecifyKind(x.CreatedUtc, DateTimeKind.Utc)),
                x.LastCheckedUtc is { } last ? new DateTimeOffset(DateTime.SpecifyKind(last, DateTimeKind.Utc)) : null)).ToList();
    }
    public async Task<IReadOnlyList<LicenseHistory>> HistoryAsync(Guid accountId, CancellationToken cancellationToken) =>
        (await db.Audit.AsNoTracking().Where(x => x.AccountId == accountId).OrderByDescending(x => x.Id).Take(100).ToListAsync(cancellationToken))
        .Select(x => new LicenseHistory(new DateTimeOffset(DateTime.SpecifyKind(x.AtUtc, DateTimeKind.Utc)), x.Actor, x.Action, x.Note)).ToList();

    public async Task ChangeAsync(Guid accountId, string action, string note, string actor, CancellationToken cancellationToken)
    {
        await LicenseOperations.Gate.WaitAsync(cancellationToken);
        try
        {
            var account = await db.Accounts.SingleOrDefaultAsync(x => x.Id == accountId, cancellationToken)
                ?? throw new WorkspaceValidationException("Account not found.");
            var old = account.Status;
            if (action == "ResetInstallation") { account.InstallationId = null; account.TokenHash = null; }
            else account.Status = action == "Activate" ? "Active" : "Suspended";
            db.Audit.Add(new LicenseAudit { AccountId = account.Id, Actor = actor, Action = action,
                Note = $"{old} → {account.Status}. {note}", AtUtc = time.GetUtcNow().UtcDateTime });
            await db.SaveChangesAsync(cancellationToken);
        }
        finally { LicenseOperations.Gate.Release(); }
    }
}
