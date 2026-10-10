using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Nexora.Domain.Audit;
using Nexora.Domain.Companies;
using Nexora.Domain.Numbering;
using Nexora.Domain.Security;
using Nexora.Domain.Settings;
using Nexora.Domain.Users;

namespace Nexora.Infrastructure.Persistence.Configurations;

// Every ICompanyOwned table also gets, from NexoraDbContext: a CompanyId foreign key to Companies (cascade),
// an index on CompanyId, and the company query filter. References between company-owned rows use composite
// keys (CompanyId, Id), so the database itself rejects a reference to a row of another company.

internal sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.ToTable("Companies");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(Company.NameMaxLength).IsRequired();
        builder.Property(c => c.StateCode).HasMaxLength(2).IsRequired();
        builder.Property(c => c.CreatedUtc).HasConversion(UtcConverter.Instance);
        builder.Property(c => c.LegalName).HasMaxLength(Company.NameMaxLength);
        builder.Property(c => c.AddressLine1).HasMaxLength(Company.AddressMaxLength);
        builder.Property(c => c.AddressLine2).HasMaxLength(Company.AddressMaxLength);
        builder.Property(c => c.City).HasMaxLength(60);
        builder.Property(c => c.Pincode).HasMaxLength(6);
        builder.Property(c => c.Phone).HasMaxLength(20);
        builder.Property(c => c.Email).HasMaxLength(120);
        builder.Property(c => c.Pan).HasMaxLength(10);
        builder.Property(c => c.Gstin).HasMaxLength(15);
        builder.Ignore(c => c.FirstFinancialYear);
        builder.Ignore(c => c.Profile);
    }
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Property(u => u.Username).HasMaxLength(Username.MaxLength).IsRequired();
        builder.Property(u => u.NormalizedUsername).HasMaxLength(Username.MaxLength).IsRequired();
        builder.HasIndex(u => u.NormalizedUsername).IsUnique();
        builder.Property(u => u.DisplayName).HasMaxLength(User.DisplayNameMaxLength).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(256).IsRequired();
        builder.Property(u => u.CreatedUtc).HasConversion(UtcConverter.Instance);
        builder.Property(u => u.PasswordChangedUtc).HasConversion(UtcConverter.Instance);
        builder.Property(u => u.LastSignInUtc).HasConversion(UtcConverter.Nullable);
        builder.Property(u => u.LockoutEndUtc).HasConversion(UtcConverter.Nullable);
    }
}

internal sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ToTable("AppSettings");
        builder.HasKey(s => s.Key);
        builder.Property(s => s.Key).HasMaxLength(100);
        builder.Property(s => s.Value).HasMaxLength(2000).IsRequired();
    }
}

internal sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("Branches");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.HasAlternateKey(b => new { b.CompanyId, b.Id });
        builder.Property(b => b.Code).HasMaxLength(10).IsRequired();
        builder.Property(b => b.Name).HasMaxLength(80).IsRequired();
        builder.Property(b => b.StateCode).HasMaxLength(2).IsRequired();
        builder.Property(b => b.Address).HasMaxLength(240);
        builder.HasIndex(b => new { b.CompanyId, b.Code }).IsUnique();
    }
}

internal sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("Warehouses");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedNever();
        builder.HasAlternateKey(w => new { w.CompanyId, w.Id });
        builder.Property(w => w.Code).HasMaxLength(10).IsRequired();
        builder.Property(w => w.Name).HasMaxLength(80).IsRequired();
        builder.HasIndex(w => new { w.CompanyId, w.Code }).IsUnique();
        builder.HasOne<Branch>().WithMany()
            .HasForeignKey(w => new { w.CompanyId, w.BranchId })
            .HasPrincipalKey(b => new { b.CompanyId, b.Id })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.HasAlternateKey(r => new { r.CompanyId, r.Id });
        builder.Property(r => r.Name).HasMaxLength(60).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(200);
        builder.Property(r => r.SystemKey).HasMaxLength(40);
        builder.Property(r => r.MaxDiscountPercent).HasPrecision(5, 2);
        builder.HasIndex(r => new { r.CompanyId, r.Name }).IsUnique();
        builder.Ignore(r => r.Permissions);
        builder.Ignore(r => r.IsSystem);
        builder.Ignore(r => r.IsAdministrator);
        builder.Property<HashSet<string>>("_permissions")
            .HasColumnName("Permissions")
            .HasMaxLength(4000)
            .HasConversion(PermissionSet.Converter, PermissionSet.Comparer)
            .IsRequired();
    }
}

internal sealed class CompanyMembershipConfiguration : IEntityTypeConfiguration<CompanyMembership>
{
    public void Configure(EntityTypeBuilder<CompanyMembership> builder)
    {
        builder.ToTable("CompanyMemberships");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.CreatedUtc).HasConversion(UtcConverter.Instance);
        builder.HasIndex(m => new { m.CompanyId, m.UserId }).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Role>().WithMany()
            .HasForeignKey(m => new { m.CompanyId, m.RoleId })
            .HasPrincipalKey(r => new { r.CompanyId, r.Id })
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class NumberingRuleConfiguration : IEntityTypeConfiguration<NumberingRule>
{
    public void Configure(EntityTypeBuilder<NumberingRule> builder)
    {
        builder.ToTable("NumberingRules");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.DocumentType).HasMaxLength(40).IsRequired();
        builder.Property(r => r.Prefix).HasMaxLength(20);
        builder.HasIndex(r => new { r.CompanyId, r.DocumentType }).IsUnique();
    }
}

internal sealed class NumberCounterConfiguration : IEntityTypeConfiguration<NumberCounter>
{
    public void Configure(EntityTypeBuilder<NumberCounter> builder)
    {
        builder.ToTable("NumberCounters");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.DocumentType).HasMaxLength(40).IsRequired();
        builder.HasIndex(c => new { c.CompanyId, c.DocumentType, c.Period }).IsUnique();
    }
}

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditEntries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasIndex(e => new { e.CompanyId, e.Sequence }).IsUnique();
        builder.HasIndex(e => new { e.CompanyId, e.OccurredUtc });
        builder.Property(e => e.OccurredUtc).HasConversion(UtcConverter.Instance);
        builder.Property(e => e.UserName).HasMaxLength(80).IsRequired();
        builder.Property(e => e.Action).HasMaxLength(40).IsRequired();
        builder.Property(e => e.EntityType).HasMaxLength(60).IsRequired();
        builder.Property(e => e.EntityId).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Summary).HasMaxLength(400).IsRequired();
        builder.Property(e => e.ChangesJson).IsRequired();
        builder.Property(e => e.PreviousHash).HasMaxLength(64);
        builder.Property(e => e.Hash).HasMaxLength(64).IsRequired();
    }
}

internal static class PermissionSet
{
    public static readonly ValueConverter<HashSet<string>, string> Converter = new(
        set => string.Join(',', set.Order(StringComparer.Ordinal)),
        text => new HashSet<string>(text.Split(',', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal));

    public static readonly ValueComparer<HashSet<string>> Comparer = new(
        (a, b) => a != null && b != null && a.SetEquals(b),
        set => set.Order(StringComparer.Ordinal).Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
        set => new HashSet<string>(set, StringComparer.Ordinal));
}
