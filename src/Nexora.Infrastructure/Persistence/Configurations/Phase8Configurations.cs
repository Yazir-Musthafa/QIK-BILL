using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexora.Domain.Accounting;
using Nexora.Domain.Reports;

namespace Nexora.Infrastructure.Persistence.Configurations;

internal sealed class ReportSettingsConfiguration : IEntityTypeConfiguration<ReportSettings>
{
    public void Configure(EntityTypeBuilder<ReportSettings> builder)
    {
        builder.ToTable("ReportSettings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.HasAlternateKey(s => new { s.CompanyId, s.Id });
        builder.Property(s => s.AgeingBuckets).HasMaxLength(60).IsRequired();
        builder.Ignore(s => s.Buckets);
        builder.HasIndex(s => s.CompanyId).IsUnique();
    }
}

internal sealed class CostCentreConfiguration : IEntityTypeConfiguration<CostCentre>
{
    public void Configure(EntityTypeBuilder<CostCentre> builder)
    {
        builder.ToTable("CostCentres");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.HasAlternateKey(c => new { c.CompanyId, c.Id });
        builder.Property(c => c.Code).HasMaxLength(20).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.Property(c => c.Kind).HasConversion<string>().HasMaxLength(10);
        builder.HasIndex(c => new { c.CompanyId, c.Code }).IsUnique();
        builder.HasIndex(c => new { c.CompanyId, c.Name }).IsUnique();
    }
}

internal sealed class BankClearingConfiguration : IEntityTypeConfiguration<BankClearing>
{
    public void Configure(EntityTypeBuilder<BankClearing> builder)
    {
        builder.ToTable("BankClearings");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.HasAlternateKey(c => new { c.CompanyId, c.Id });
        builder.Property(c => c.StatementReference).HasMaxLength(60);
        builder.Property(c => c.ClearedUtc).HasConversion(UtcConverter.Instance);
        builder.HasIndex(c => new { c.CompanyId, c.LedgerEntryId }).IsUnique();
        builder.HasIndex(c => new { c.CompanyId, c.AccountId, c.ClearedOn });
        CompanyKeys.References<BankClearing, LedgerEntry>(builder, c => new { c.CompanyId, c.LedgerEntryId }, required: true);
        CompanyKeys.References<BankClearing, Account>(builder, c => new { c.CompanyId, c.AccountId }, required: true);
    }
}
