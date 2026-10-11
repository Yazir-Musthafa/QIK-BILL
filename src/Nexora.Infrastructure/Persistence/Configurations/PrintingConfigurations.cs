using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexora.Domain.Printing;

namespace Nexora.Infrastructure.Persistence.Configurations;

internal sealed class PrinterProfileConfiguration : IEntityTypeConfiguration<PrinterProfile>
{
    public void Configure(EntityTypeBuilder<PrinterProfile> builder)
    {
        builder.ToTable("PrinterProfiles");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Purpose).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Paper).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Orientation).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.PrinterName).HasMaxLength(200);
        foreach (var name in new[] { nameof(PrinterProfile.MarginTopMm), nameof(PrinterProfile.MarginRightMm), nameof(PrinterProfile.MarginBottomMm),
                     nameof(PrinterProfile.MarginLeftMm), nameof(PrinterProfile.ScalePercent), nameof(PrinterProfile.FontSize) })
            builder.Property<decimal>(name).HasPrecision(7, 2);
        builder.HasIndex(p => new { p.CompanyId, p.Purpose }).IsUnique();
    }
}

internal sealed class PrintSettingsConfiguration : IEntityTypeConfiguration<PrintSettings>
{
    public void Configure(EntityTypeBuilder<PrintSettings> builder)
    {
        builder.ToTable("PrintSettings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.LabelQrFormat).HasMaxLength(200);
        builder.Property(s => s.InvoiceQrFormat).HasMaxLength(200);
        builder.Property(s => s.FooterNote).HasMaxLength(300);
        builder.HasIndex(s => s.CompanyId).IsUnique();
    }
}

internal sealed class LabelLayoutConfiguration : IEntityTypeConfiguration<LabelLayout>
{
    public void Configure(EntityTypeBuilder<LabelLayout> builder)
    {
        builder.ToTable("LabelLayouts");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.HasAlternateKey(l => new { l.CompanyId, l.Id });
        builder.Property(l => l.Name).HasMaxLength(60).IsRequired();
        builder.Property(l => l.Code).HasConversion<string>().HasMaxLength(10);
        foreach (var name in new[] { nameof(LabelLayout.PageWidthMm), nameof(LabelLayout.PageHeightMm), nameof(LabelLayout.LabelWidthMm), nameof(LabelLayout.LabelHeightMm),
                     nameof(LabelLayout.MarginLeftMm), nameof(LabelLayout.MarginTopMm), nameof(LabelLayout.HorizontalGapMm), nameof(LabelLayout.VerticalGapMm) })
            builder.Property<decimal>(name).HasPrecision(7, 2);
        builder.HasIndex(l => new { l.CompanyId, l.Name }).IsUnique();
    }
}

internal sealed class PrintRecordConfiguration : IEntityTypeConfiguration<PrintRecord>
{
    public void Configure(EntityTypeBuilder<PrintRecord> builder)
    {
        builder.ToTable("PrintRecords");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.SourceType).HasMaxLength(20).IsRequired();
        builder.Property(r => r.DocumentNumber).HasMaxLength(120);
        builder.Property(r => r.PrinterName).HasMaxLength(200);
        builder.Property(r => r.Purpose).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Output).HasConversion<string>().HasMaxLength(10);
        builder.Property(r => r.PrintedUtc).HasConversion(UtcConverter.Instance);
        builder.HasIndex(r => new { r.CompanyId, r.SourceType, r.SourceId });
        builder.HasIndex(r => new { r.CompanyId, r.PrintedUtc });
    }
}

internal sealed class PrinterTestConfiguration : IEntityTypeConfiguration<PrinterTest>
{
    public void Configure(EntityTypeBuilder<PrinterTest> builder)
    {
        builder.ToTable("PrinterTests");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.PrinterName).HasMaxLength(200);
        builder.Property(t => t.Paper).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Outcome).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Detail).HasMaxLength(500);
        builder.Property(t => t.Notes).HasMaxLength(300);
        builder.Property(t => t.RequestedUtc).HasConversion(UtcConverter.Instance);
        builder.Property(t => t.ConfirmedUtc).HasConversion(UtcConverter.Nullable);
        builder.HasIndex(t => new { t.CompanyId, t.RequestedUtc });
    }
}
