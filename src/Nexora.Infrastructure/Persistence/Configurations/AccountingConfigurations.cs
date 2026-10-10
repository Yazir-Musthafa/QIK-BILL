using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Nexora.Domain.Accounting;

namespace Nexora.Infrastructure.Persistence.Configurations;

/// <summary>Amounts are stored as whole paise (64-bit integers): exact, sortable and summable in any provider.</summary>
internal static class PaiseConverter
{
    public static readonly ValueConverter<decimal, long> Instance = new(v => Money.ToPaise(v), v => Money.FromPaise(v));
}

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("Accounts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.HasAlternateKey(a => new { a.CompanyId, a.Id });
        builder.Property(a => a.Code).HasMaxLength(12).IsRequired();
        builder.Property(a => a.Name).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Nature).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.SystemKey).HasMaxLength(40);
        builder.Ignore(a => a.IsGroup);
        builder.Ignore(a => a.IsSystem);
        builder.Ignore(a => a.IsCashOrBank);
        builder.HasIndex(a => new { a.CompanyId, a.Code }).IsUnique();
        builder.HasIndex(a => new { a.CompanyId, a.Name }).IsUnique();
        builder.HasIndex(a => new { a.CompanyId, a.SystemKey }).IsUnique();
        builder.HasOne<Account>().WithMany()
            .HasForeignKey(a => new { a.CompanyId, a.ParentId })
            .HasPrincipalKey(a => new { a.CompanyId, a.Id })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class FinancialYearPeriodConfiguration : IEntityTypeConfiguration<FinancialYearPeriod>
{
    public void Configure(EntityTypeBuilder<FinancialYearPeriod> builder)
    {
        builder.ToTable("FinancialYears");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.ClosedUtc).HasConversion(UtcConverter.Nullable);
        builder.Ignore(p => p.Year);
        builder.HasIndex(p => new { p.CompanyId, p.StartYear }).IsUnique();
    }
}

internal sealed class VoucherConfiguration : IEntityTypeConfiguration<Voucher>
{
    public void Configure(EntityTypeBuilder<Voucher> builder)
    {
        builder.ToTable("Vouchers");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();
        builder.HasAlternateKey(v => new { v.CompanyId, v.Id });
        builder.Property(v => v.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(v => v.Number).HasMaxLength(40);
        builder.Property(v => v.Narration).HasMaxLength(500).IsRequired();
        builder.Property(v => v.SourceType).HasMaxLength(20);
        builder.Property(v => v.CreatedUtc).HasConversion(UtcConverter.Instance);
        builder.Property(v => v.PostedUtc).HasConversion(UtcConverter.Nullable);
        builder.Ignore(v => v.IsPosted);
        builder.Ignore(v => v.TotalDebit);
        builder.Ignore(v => v.TotalCredit);
        builder.HasIndex(v => new { v.CompanyId, v.Type, v.Number }).IsUnique();
        builder.HasIndex(v => new { v.CompanyId, v.Date });
        // At most one reversal per voucher. (SQLite treats NULLs as distinct; SQL Server would need a filtered index.)
        builder.HasIndex(v => v.ReversesVoucherId).IsUnique();
        builder.HasOne<Voucher>().WithMany()
            .HasForeignKey(v => new { v.CompanyId, v.ReversesVoucherId })
            .HasPrincipalKey(v => new { v.CompanyId, v.Id })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasMany(v => v.Lines).WithOne()
            .HasForeignKey(l => new { l.CompanyId, l.VoucherId })
            .HasPrincipalKey(v => new { v.CompanyId, v.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(v => v.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class VoucherLineConfiguration : IEntityTypeConfiguration<VoucherLine>
{
    public void Configure(EntityTypeBuilder<VoucherLine> builder)
    {
        builder.ToTable("VoucherLines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.Debit).HasConversion(PaiseConverter.Instance).HasColumnName("DebitPaise");
        builder.Property(l => l.Credit).HasConversion(PaiseConverter.Instance).HasColumnName("CreditPaise");
        builder.Property(l => l.Narration).HasMaxLength(200);
        builder.HasIndex(l => new { l.CompanyId, l.VoucherId, l.LineNumber }).IsUnique();
        builder.HasOne<Account>().WithMany()
            .HasForeignKey(l => new { l.CompanyId, l.AccountId })
            .HasPrincipalKey(a => new { a.CompanyId, a.Id })
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("LedgerEntries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Debit).HasConversion(PaiseConverter.Instance).HasColumnName("DebitPaise");
        builder.Property(e => e.Credit).HasConversion(PaiseConverter.Instance).HasColumnName("CreditPaise");
        builder.HasIndex(e => new { e.CompanyId, e.AccountId, e.Date });
        builder.HasIndex(e => new { e.CompanyId, e.Date });
        builder.HasIndex(e => new { e.CompanyId, e.VoucherId, e.LineNumber }).IsUnique();
        builder.HasOne<Voucher>().WithMany()
            .HasForeignKey(e => new { e.CompanyId, e.VoucherId })
            .HasPrincipalKey(v => new { v.CompanyId, v.Id })
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Account>().WithMany()
            .HasForeignKey(e => new { e.CompanyId, e.AccountId })
            .HasPrincipalKey(a => new { a.CompanyId, a.Id })
            .OnDelete(DeleteBehavior.NoAction);
    }
}
