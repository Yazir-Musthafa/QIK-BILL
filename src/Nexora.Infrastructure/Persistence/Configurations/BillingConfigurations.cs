using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexora.Domain.Accounting;
using Nexora.Domain.Billing;
using Nexora.Domain.Companies;
using Nexora.Domain.Inventory;

namespace Nexora.Infrastructure.Persistence.Configurations;

internal sealed class PartyConfiguration : IEntityTypeConfiguration<Party>
{
    public void Configure(EntityTypeBuilder<Party> builder)
    {
        builder.ToTable("Parties");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.HasAlternateKey(p => new { p.CompanyId, p.Id });
        builder.Property(p => p.Kind).HasConversion<string>().HasMaxLength(10);
        builder.Property(p => p.Name).HasMaxLength(120).IsRequired();
        builder.Property(p => p.Phone).HasMaxLength(20);
        builder.Property(p => p.Email).HasMaxLength(120);
        builder.Property(p => p.Gstin).HasMaxLength(15);
        builder.Property(p => p.StateCode).HasMaxLength(2);
        builder.Property(p => p.Address).HasMaxLength(300);
        builder.Property(p => p.City).HasMaxLength(60);
        builder.Property(p => p.Pincode).HasMaxLength(6);
        builder.Property(p => p.CreditLimit).HasConversion(PaiseConverter.Instance).HasColumnName("CreditLimitPaise");
        builder.Ignore(p => p.IsRegistered);
        builder.HasIndex(p => new { p.CompanyId, p.Kind, p.Name }).IsUnique();
        builder.HasIndex(p => p.AccountId).IsUnique();
        CompanyKeys.References<Party, Account>(builder, p => new { p.CompanyId, p.AccountId }, required: true);
    }
}

internal sealed class BillingDocumentConfiguration : IEntityTypeConfiguration<BillingDocument>
{
    public void Configure(EntityTypeBuilder<BillingDocument> builder)
    {
        builder.ToTable("BillingDocuments");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.HasAlternateKey(d => new { d.CompanyId, d.Id });
        builder.Property(d => d.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(d => d.Mode).HasConversion<string>().HasMaxLength(10);
        builder.Property(d => d.Number).HasMaxLength(40);
        builder.Property(d => d.PartyName).HasMaxLength(120);
        builder.Property(d => d.PartyGstin).HasMaxLength(15);
        builder.Property(d => d.PlaceOfSupply).HasMaxLength(2);
        builder.Property(d => d.Reference).HasMaxLength(40);
        builder.Property(d => d.Narration).HasMaxLength(300);
        foreach (var name in new[] { nameof(BillingDocument.GrossAmount), nameof(BillingDocument.DiscountAmount), nameof(BillingDocument.TaxableAmount),
                     nameof(BillingDocument.Cgst), nameof(BillingDocument.Sgst), nameof(BillingDocument.Igst), nameof(BillingDocument.RoundOff),
                     nameof(BillingDocument.Total), nameof(BillingDocument.CostOfGoods) })
        {
            builder.Property<decimal>(name).HasConversion(PaiseConverter.Instance).HasColumnName(name + "Paise");
        }

        builder.Property(d => d.CreatedUtc).HasConversion(UtcConverter.Instance);
        builder.Property(d => d.PostedUtc).HasConversion(UtcConverter.Nullable);
        builder.Ignore(d => d.Tax);
        // Numbers are unique per kind (sales invoices share invoice.tax numbering). NULL draft numbers are distinct.
        builder.HasIndex(d => new { d.CompanyId, d.Kind, d.Number }).IsUnique();
        builder.HasIndex(d => new { d.CompanyId, d.Kind, d.Date });
        builder.HasIndex(d => new { d.CompanyId, d.PartyId });
        builder.HasIndex(d => new { d.CompanyId, d.OriginalDocumentId });
        CompanyKeys.References<BillingDocument, Party>(builder, d => new { d.CompanyId, d.PartyId }, required: false);
        CompanyKeys.References<BillingDocument, Account>(builder, d => new { d.CompanyId, d.CashAccountId }, required: false);
        CompanyKeys.References<BillingDocument, Warehouse>(builder, d => new { d.CompanyId, d.WarehouseId }, required: true);
        CompanyKeys.References<BillingDocument, BillingDocument>(builder, d => new { d.CompanyId, d.OriginalDocumentId }, required: false);
        CompanyKeys.References<BillingDocument, Voucher>(builder, d => new { d.CompanyId, d.VoucherId }, required: false);
        CompanyKeys.References<BillingDocument, StockDocument>(builder, d => new { d.CompanyId, d.StockDocumentId }, required: false);
    }
}

internal sealed class BillingLineConfiguration : IEntityTypeConfiguration<BillingLine>
{
    public void Configure(EntityTypeBuilder<BillingLine> builder)
    {
        builder.ToTable("BillingLines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.HasAlternateKey(l => new { l.CompanyId, l.Id });
        builder.Property(l => l.Description).HasMaxLength(200).IsRequired();
        builder.Property(l => l.HsnCode).HasMaxLength(8);
        builder.Property(l => l.BatchNumber).HasMaxLength(40);
        builder.Property(l => l.SerialNumber).HasMaxLength(60);
        builder.Property(l => l.Quantity).HasConversion(QuantityConverter.Instance).HasColumnName("QuantityMilli");
        builder.Property(l => l.BaseQuantity).HasConversion(QuantityConverter.Instance).HasColumnName("BaseQuantityMilli");
        builder.Property(l => l.DiscountPercent).HasPrecision(5, 2);
        builder.Property(l => l.TaxRatePercent).HasPrecision(6, 3);
        foreach (var name in new[] { nameof(BillingLine.Rate), nameof(BillingLine.GrossAmount), nameof(BillingLine.DiscountAmount),
                     nameof(BillingLine.TaxableAmount), nameof(BillingLine.Cgst), nameof(BillingLine.Sgst), nameof(BillingLine.Igst), nameof(BillingLine.CostValue) })
        {
            builder.Property<decimal>(name).HasConversion(PaiseConverter.Instance).HasColumnName(name + "Paise");
        }

        builder.Ignore(l => l.Amounts);
        builder.Ignore(l => l.Total);
        builder.HasIndex(l => new { l.CompanyId, l.DocumentId, l.LineNumber }).IsUnique();
        builder.HasIndex(l => new { l.CompanyId, l.OriginalLineId });
        CompanyKeys.References<BillingLine, BillingDocument>(builder, l => new { l.CompanyId, l.DocumentId }, required: true);
        CompanyKeys.References<BillingLine, Product>(builder, l => new { l.CompanyId, l.ProductId }, required: true);
        CompanyKeys.References<BillingLine, Unit>(builder, l => new { l.CompanyId, l.UnitId }, required: true);
        CompanyKeys.References<BillingLine, TaxCategory>(builder, l => new { l.CompanyId, l.TaxCategoryId }, required: false);
        CompanyKeys.References<BillingLine, Batch>(builder, l => new { l.CompanyId, l.BatchId }, required: false);
        CompanyKeys.References<BillingLine, BillingLine>(builder, l => new { l.CompanyId, l.OriginalLineId }, required: false);
    }
}

internal sealed class BillingCancellationConfiguration : IEntityTypeConfiguration<BillingCancellation>
{
    public void Configure(EntityTypeBuilder<BillingCancellation> builder)
    {
        builder.ToTable("BillingCancellations");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Reason).HasMaxLength(300).IsRequired();
        builder.Property(c => c.CancelledUtc).HasConversion(UtcConverter.Instance);
        builder.HasIndex(c => new { c.CompanyId, c.DocumentId }).IsUnique();
        CompanyKeys.References<BillingCancellation, BillingDocument>(builder, c => new { c.CompanyId, c.DocumentId }, required: true);
        CompanyKeys.References<BillingCancellation, Voucher>(builder, c => new { c.CompanyId, c.ReversalVoucherId }, required: false);
        CompanyKeys.References<BillingCancellation, StockDocument>(builder, c => new { c.CompanyId, c.StockReversalId }, required: false);
    }
}

internal sealed class SettlementConfiguration : IEntityTypeConfiguration<Settlement>
{
    public void Configure(EntityTypeBuilder<Settlement> builder)
    {
        builder.ToTable("Settlements");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.HasAlternateKey(s => new { s.CompanyId, s.Id });
        builder.Property(s => s.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Method).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Number).HasMaxLength(40).IsRequired();
        builder.Property(s => s.Reference).HasMaxLength(60);
        builder.Property(s => s.Narration).HasMaxLength(300);
        builder.Property(s => s.Amount).HasConversion(PaiseConverter.Instance).HasColumnName("AmountPaise");
        builder.Property(s => s.PostedUtc).HasConversion(UtcConverter.Instance);
        builder.HasIndex(s => new { s.CompanyId, s.Kind, s.Number }).IsUnique();
        builder.HasIndex(s => new { s.CompanyId, s.PartyId });
        CompanyKeys.References<Settlement, Party>(builder, s => new { s.CompanyId, s.PartyId }, required: true);
        CompanyKeys.References<Settlement, Account>(builder, s => new { s.CompanyId, s.AccountId }, required: true);
        CompanyKeys.References<Settlement, Voucher>(builder, s => new { s.CompanyId, s.VoucherId }, required: true);
    }
}

internal sealed class SettlementReversalConfiguration : IEntityTypeConfiguration<SettlementReversal>
{
    public void Configure(EntityTypeBuilder<SettlementReversal> builder)
    {
        builder.ToTable("SettlementReversals");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Reason).HasMaxLength(300).IsRequired();
        builder.Property(r => r.ReversedUtc).HasConversion(UtcConverter.Instance);
        builder.HasIndex(r => new { r.CompanyId, r.SettlementId }).IsUnique();
        CompanyKeys.References<SettlementReversal, Settlement>(builder, r => new { r.CompanyId, r.SettlementId }, required: true);
        CompanyKeys.References<SettlementReversal, Voucher>(builder, r => new { r.CompanyId, r.ReversalVoucherId }, required: true);
    }
}

internal sealed class AllocationConfiguration : IEntityTypeConfiguration<Allocation>
{
    public void Configure(EntityTypeBuilder<Allocation> builder)
    {
        builder.ToTable("Allocations");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.ChargeSource).HasConversion<string>().HasMaxLength(10);
        builder.Property(a => a.CreditSource).HasConversion<string>().HasMaxLength(10);
        builder.Property(a => a.Amount).HasConversion(PaiseConverter.Instance).HasColumnName("AmountPaise");
        builder.Property(a => a.CreatedUtc).HasConversion(UtcConverter.Instance);
        builder.HasIndex(a => new { a.CompanyId, a.PartyId });
        builder.HasIndex(a => new { a.CompanyId, a.ChargeId });
        builder.HasIndex(a => new { a.CompanyId, a.CreditId });
        CompanyKeys.References<Allocation, Party>(builder, a => new { a.CompanyId, a.PartyId }, required: true);
    }
}

internal sealed class BillingSettingsConfiguration : IEntityTypeConfiguration<BillingSettings>
{
    public void Configure(EntityTypeBuilder<BillingSettings> builder)
    {
        builder.ToTable("BillingSettings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.HasIndex(s => s.CompanyId).IsUnique();
    }
}
