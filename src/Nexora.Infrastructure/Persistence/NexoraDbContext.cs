using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Nexora.Application;
using Nexora.Application.Abstractions;
using Nexora.Domain.Accounting;
using Nexora.Domain.Audit;
using Nexora.Domain.Billing;
using Nexora.Domain.Common;
using Nexora.Domain.Crm;
using Nexora.Domain.Expenses;
using Nexora.Domain.Service;
using Nexora.Domain.Companies;
using Nexora.Domain.Inventory;
using Nexora.Domain.Numbering;
using Nexora.Domain.Printing;
using Nexora.Domain.Security;
using Nexora.Domain.Settings;
using Nexora.Domain.Users;

namespace Nexora.Infrastructure.Persistence;

/// <summary>
/// Provider-neutral model. The database provider is chosen by an <see cref="Providers.IDatabaseProvider"/>;
/// nothing here may use provider-specific column types or SQL.
///
/// Company isolation is enforced here for every query and write:
/// <list type="bullet">
/// <item>every <see cref="ICompanyOwned"/> table gets a global filter on the signed-in company;</item>
/// <item>companies and users are filtered to the signed-in company and its members;</item>
/// <item>saving a row for any other company throws <see cref="CompanyIsolationException"/>.</item>
/// </list>
/// Only <see cref="EfWorkspaceStore"/> may bypass this, through <see cref="SystemScope"/> and IgnoreQueryFilters.
/// </summary>
public sealed class NexoraDbContext : DbContext
{
    private static readonly MethodInfo ApplyCompanyFilterMethod =
        typeof(NexoraDbContext).GetMethod(nameof(ApplyCompanyFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private readonly ISessionContext _session;
    private readonly SessionInfo? _openedSession;
    private readonly AuditRecorder _audit;

    public NexoraDbContext(DbContextOptions<NexoraDbContext> options, ISessionContext session, IClock clock) : base(options)
    {
        _session = session;
        _openedSession = session.Current;
        _audit = new AuditRecorder(this, session, clock);
    }

    public DbSet<Company> Companies => Set<Company>();

    public DbSet<User> Users => Set<User>();

    public DbSet<AppSetting> Settings => Set<AppSetting>();

    public DbSet<Branch> Branches => Set<Branch>();

    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<CompanyMembership> Memberships => Set<CompanyMembership>();

    public DbSet<NumberingRule> NumberingRules => Set<NumberingRule>();

    public DbSet<NumberCounter> NumberCounters => Set<NumberCounter>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<FinancialYearPeriod> FinancialYears => Set<FinancialYearPeriod>();

    public DbSet<Voucher> Vouchers => Set<Voucher>();

    public DbSet<VoucherLine> VoucherLines => Set<VoucherLine>();

    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<StockDocument> StockDocuments => Set<StockDocument>();

    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    /// <summary>The company every query and write is limited to. Empty when nobody is signed in, which matches nothing.</summary>
    public Guid CurrentCompanyId => ReferenceEquals(_openedSession, _session.Current) ? _openedSession?.CompanyId ?? Guid.Empty : Guid.Empty;

    /// <summary>Allows writes for companies other than the signed-in one. Set only by <see cref="EfWorkspaceStore"/>.</summary>
    internal bool SystemScope { get; set; }

    internal AuditRecorder Audit => _audit;

    internal void QueueEvent(AuditEvent auditEvent) => _audit.Queue(auditEvent);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NexoraDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(t => typeof(ICompanyOwned).IsAssignableFrom(t.ClrType)).ToList())
        {
            ApplyCompanyFilterMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
        }

        modelBuilder.Entity<Company>().HasQueryFilter(c => c.Id == CurrentCompanyId);
        // A user is visible only through a membership of the signed-in company (memberships are themselves filtered).
        modelBuilder.Entity<User>().HasQueryFilter(u => Memberships.Any(m => m.UserId == u.Id));
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new NotSupportedException("Use SaveChangesAsync so company isolation and auditing run.");

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        EnforceCompanyIsolation();
        EnforceImmutability();

        var ownTransaction = Database.CurrentTransaction is null ? await Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            await _audit.CaptureAsync(cancellationToken);
            var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            if (ownTransaction is not null) await ownTransaction.CommitAsync(cancellationToken);
            return result;
        }
        finally
        {
            if (ownTransaction is not null) await ownTransaction.DisposeAsync();
        }
    }

    private void ApplyCompanyFilter<T>(ModelBuilder modelBuilder) where T : class, ICompanyOwned
    {
        modelBuilder.Entity<T>().HasQueryFilter(e => e.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<T>().HasOne<Company>().WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<T>().HasIndex(e => e.CompanyId);
    }

    private void EnforceCompanyIsolation()
    {
        if (SystemScope) return;
        var current = CurrentCompanyId;
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            switch (entry.Entity)
            {
                case ICompanyOwned owned:
                    if (owned.CompanyId != current || (entry.State == EntityState.Modified && entry.Property(nameof(ICompanyOwned.CompanyId)).IsModified))
                        throw new CompanyIsolationException(entry.Metadata.ClrType.Name);
                    break;
                case Company company:
                    if (company.Id != current || entry.State != EntityState.Modified) throw new CompanyIsolationException(nameof(Company));
                    break;
            }
        }
    }

    private void EnforceImmutability()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted && ImmutabilityRules.IsProtected(entry))
                throw new ImmutableRecordException(entry.Metadata.ClrType.Name);
        }
    }
}

/// <summary>A write targeted a company other than the signed-in one. This indicates a defect, not a user error.</summary>
public sealed class CompanyIsolationException(string entity)
    : InvalidOperationException($"Blocked a write to {entity} outside the signed-in company.");

/// <summary>An attempt to change or delete a record that may only be appended or reversed.</summary>
public sealed class ImmutableRecordException(string entity)
    : InvalidOperationException($"{entity} records cannot be changed or deleted once stored.");

/// <summary>Which stored rows may never be modified or deleted through the context.</summary>
internal static class ImmutabilityRules
{
    public static bool IsProtected(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry) => entry.Entity switch
    {
        AuditEntry => true,
        LedgerEntry => true,
        StockMovement => true,
        StockDocument => true,
        // Original values decide: posting a draft (Draft -> Posted, unlocked -> locked) is allowed, anything after is not.
        Voucher => entry.OriginalValues.GetValue<VoucherStatus>(nameof(Voucher.Status)) == VoucherStatus.Posted,
        VoucherLine => entry.OriginalValues.GetValue<bool>(nameof(VoucherLine.IsLocked)),
        // Billing follows the same rules: drafts are editable, posted documents and their lines are not, and
        // settlements, allocations, cancellations and reversals are append-only.
        BillingDocument => entry.OriginalValues.GetValue<BillingStatus>(nameof(BillingDocument.Status)) == BillingStatus.Posted,
        BillingLine => entry.OriginalValues.GetValue<bool>(nameof(BillingLine.IsLocked)),
        BillingCancellation => true,
        Settlement => true,
        SettlementReversal => true,
        Allocation => true,
        // Print history is evidence of who printed what; it is never edited.
        PrintRecord => true,
        // Phase 7: posted expenses and their lines are fixed; reversals, timeline entries and part movements are append-only;
        // delivered or cancelled service jobs are closed.
        Expense => entry.OriginalValues.GetValue<ExpenseStatus>(nameof(Expense.Status)) == ExpenseStatus.Posted,
        ExpenseLine => entry.OriginalValues.GetValue<bool>(nameof(ExpenseLine.IsLocked)),
        ExpenseAttachment => entry.State == EntityState.Modified,
        ExpenseReversal => true,
        Activity => true,
        ServicePart => true,
        ServicePartReturn => true,
        ServiceJob => entry.OriginalValues.GetValue<ServiceStatus>(nameof(ServiceJob.Status)) is ServiceStatus.Delivered or ServiceStatus.Cancelled,
        _ => false,
    };
}
