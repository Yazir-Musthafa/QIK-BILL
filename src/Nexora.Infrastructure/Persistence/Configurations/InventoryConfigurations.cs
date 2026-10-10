using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Nexora.Domain.Accounting;
using Nexora.Domain.Companies;
using Nexora.Domain.Inventory;

namespace Nexora.Infrastructure.Persistence.Configurations;

/// <summary>Quantities are stored as whole thousandths (64-bit integers): exact and summable.</summary>
internal static class QuantityConverter
{
    public static readonly ValueConverter<decimal, long> Instance = new(v => ToMilli(v), v => v / 1000m);

    public static readonly ValueConverter<decimal?, long?> Nullable = new(v => v.HasValue ? ToMilli(v.Value) : null, v => v.HasValue ? v.Value / 1000m : null);

    private static long ToMilli(decimal value) =>
        decimal.Round(value, 3) == value ? (long)(value * 1000m) : throw new InvalidOperationException("Quantities can have at most 3 decimal places.");
}

internal static class CompanyKeys
{
    /// <summary>Reference from one company-owned row to another, through (CompanyId, Id), so the database rejects cross-company links.</summary>
    public static void References<TDependent, TPrincipal>(EntityTypeBuilder<TDependent> builder,
        System.Linq.Expressions.Expression<Func<TDependent, object?>> foreignKey, bool required)
        where TDependent : class where TPrincipal : class =>
        builder.HasOne<TPrincipal>().WithMany()
            .HasForeignKey(foreignKey)
            .HasPrincipalKey("CompanyId", "Id")
            .IsRequired(required)
            .OnDelete(DeleteBehavior.NoAction);
}

internal sealed class UnitConfiguration : IEntityTypeConfiguration<Unit>
{
    public void Configure(EntityTypeBuilder<Unit> builder)
    {
        builder.ToTable("Units");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.HasAlternateKey(u => new { u.CompanyId, u.Id });
        builder.Property(u => u.Code).HasMaxLength(10).IsRequired();
        builder.Property(u => u.Name).HasMaxLength(40).IsRequired();
        builder.HasIndex(u => new { u.CompanyId, u.Code }).IsUnique();
    }
}

internal sealed class TaxCategoryConfiguration : IEntityTypeConfiguration<TaxCategory>
{
    public void Configure(EntityTypeBuilder<TaxCategory> builder)
    {
        builder.ToTable("TaxCategories");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.HasAlternateKey(t => new { t.CompanyId, t.Id });
        builder.Property(t => t.Name).HasMaxLength(60).IsRequired();
        builder.Property(t => t.ReferenceRatePercent).HasPrecision(6, 3);
        builder.HasIndex(t => new { t.CompanyId, t.Name }).IsUnique();
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.HasAlternateKey(p => new { p.CompanyId, p.Id });
        builder.Property(p => p.Sku).HasMaxLength(30).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(120).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(1000);
        builder.Property(p => p.Category).HasMaxLength(60);
        builder.Property(p => p.Brand).HasMaxLength(60);
        builder.Property(p => p.Barcode).HasMaxLength(48);
        builder.Property(p => p.QrCode).HasMaxLength(120);
        builder.Property(p => p.HsnCode).HasMaxLength(8);
        builder.Property(p => p.VariantSize).HasMaxLength(30);
        builder.Property(p => p.VariantColour).HasMaxLength(30);
        builder.Property(p => p.VariantModel).HasMaxLength(30);
        builder.Property(p => p.Tracking).HasConversion<string>().HasMaxLength(10);
        builder.Property(p => p.PurchasePrice).HasConversion(PaiseConverter.Instance).HasColumnName("PurchasePricePaise");
        builder.Property(p => p.SellingPrice).HasConversion(PaiseConverter.Instance).HasColumnName("SellingPricePaise");
        builder.Property(p => p.Mrp).HasConversion(PaiseConverter.Instance).HasColumnName("MrpPaise");
        builder.Property(p => p.ReorderLevel).HasConversion(QuantityConverter.Instance).HasColumnName("ReorderLevelMilli");
        builder.Ignore(p => p.DisplayName);
        // Duplicate-code detection in the database as well as the service (NULLs are distinct in SQLite).
        builder.HasIndex(p => new { p.CompanyId, p.Sku }).IsUnique();
        builder.HasIndex(p => new { p.CompanyId, p.Barcode }).IsUnique();
        builder.HasIndex(p => new { p.CompanyId, p.QrCode }).IsUnique();
        CompanyKeys.References<Product, Unit>(builder, p => new { p.CompanyId, p.UnitId }, required: true);
        CompanyKeys.References<Product, TaxCategory>(builder, p => new { p.CompanyId, p.TaxCategoryId }, required: false);
        CompanyKeys.References<Product, Product>(builder, p => new { p.CompanyId, p.ParentProductId }, required: false);
    }
}

internal sealed class UnitConversionConfiguration : IEntityTypeConfiguration<UnitConversion>
{
    public void Configure(EntityTypeBuilder<UnitConversion> builder)
    {
        builder.ToTable("UnitConversions");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Factor).HasPrecision(18, 6);
        builder.HasIndex(c => new { c.CompanyId, c.ProductId, c.UnitId }).IsUnique();
        CompanyKeys.References<UnitConversion, Product>(builder, c => new { c.CompanyId, c.ProductId }, required: true);
        CompanyKeys.References<UnitConversion, Unit>(builder, c => new { c.CompanyId, c.UnitId }, required: true);
    }
}

internal sealed class BatchConfiguration : IEntityTypeConfiguration<Batch>
{
    public void Configure(EntityTypeBuilder<Batch> builder)
    {
        builder.ToTable("Batches");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.HasAlternateKey(b => new { b.CompanyId, b.Id });
        builder.Property(b => b.Number).HasMaxLength(40).IsRequired();
        builder.HasIndex(b => new { b.CompanyId, b.ProductId, b.Number }).IsUnique();
        CompanyKeys.References<Batch, Product>(builder, b => new { b.CompanyId, b.ProductId }, required: true);
    }
}

internal sealed class InventorySettingsConfiguration : IEntityTypeConfiguration<InventorySettings>
{
    public void Configure(EntityTypeBuilder<InventorySettings> builder)
    {
        builder.ToTable("InventorySettings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.CostingMethod).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(s => s.CompanyId).IsUnique();
    }
}

internal sealed class StockDocumentConfiguration : IEntityTypeConfiguration<StockDocument>
{
    public void Configure(EntityTypeBuilder<StockDocument> builder)
    {
        builder.ToTable("StockDocuments");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.HasAlternateKey(d => new { d.CompanyId, d.Id });
        builder.Property(d => d.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Number).HasMaxLength(40).IsRequired();
        builder.Property(d => d.Narration).HasMaxLength(300).IsRequired();
        builder.Property(d => d.PostedUtc).HasConversion(UtcConverter.Instance);
        builder.HasIndex(d => new { d.CompanyId, d.Number }).IsUnique();
        builder.HasIndex(d => d.ReversesDocumentId).IsUnique();
        CompanyKeys.References<StockDocument, Warehouse>(builder, d => new { d.CompanyId, d.WarehouseId }, required: true);
        CompanyKeys.References<StockDocument, Warehouse>(builder, d => new { d.CompanyId, d.ToWarehouseId }, required: false);
        CompanyKeys.References<StockDocument, Account>(builder, d => new { d.CompanyId, d.CounterAccountId }, required: false);
        CompanyKeys.References<StockDocument, Voucher>(builder, d => new { d.CompanyId, d.VoucherId }, required: false);
        CompanyKeys.References<StockDocument, StockDocument>(builder, d => new { d.CompanyId, d.ReversesDocumentId }, required: false);
    }
}

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.State).HasConversion<string>().HasMaxLength(10);
        builder.Property(m => m.SerialNumber).HasMaxLength(60);
        builder.Property(m => m.Quantity).HasConversion(QuantityConverter.Instance).HasColumnName("QuantityMilli");
        builder.Property(m => m.EnteredQuantity).HasConversion(QuantityConverter.Nullable).HasColumnName("EnteredQuantityMilli");
        builder.Property(m => m.Value).HasConversion(PaiseConverter.Instance).HasColumnName("ValuePaise");
        builder.HasIndex(m => new { m.CompanyId, m.Sequence }).IsUnique();
        builder.HasIndex(m => new { m.CompanyId, m.ProductId, m.Sequence });
        builder.HasIndex(m => new { m.CompanyId, m.Date });
        CompanyKeys.References<StockMovement, StockDocument>(builder, m => new { m.CompanyId, m.DocumentId }, required: true);
        CompanyKeys.References<StockMovement, Product>(builder, m => new { m.CompanyId, m.ProductId }, required: true);
        CompanyKeys.References<StockMovement, Warehouse>(builder, m => new { m.CompanyId, m.WarehouseId }, required: true);
        CompanyKeys.References<StockMovement, Batch>(builder, m => new { m.CompanyId, m.BatchId }, required: false);
        CompanyKeys.References<StockMovement, Unit>(builder, m => new { m.CompanyId, m.EnteredUnitId }, required: false);
    }
}
