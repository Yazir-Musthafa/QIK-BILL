using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexora.Domain.Accounting;
using Nexora.Domain.Billing;
using Nexora.Domain.Companies;
using Nexora.Domain.Crm;
using Nexora.Domain.Expenses;
using Nexora.Domain.Inventory;
using Nexora.Domain.Service;

namespace Nexora.Infrastructure.Persistence.Configurations;

internal sealed class ExpenseCategoryConfiguration : IEntityTypeConfiguration<ExpenseCategory>
{
    public void Configure(EntityTypeBuilder<ExpenseCategory> builder)
    {
        builder.ToTable("ExpenseCategories");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.HasAlternateKey(c => new { c.CompanyId, c.Id });
        builder.Property(c => c.Name).HasMaxLength(60).IsRequired();
        builder.HasIndex(c => new { c.CompanyId, c.Name }).IsUnique();
        CompanyKeys.References<ExpenseCategory, Account>(builder, c => new { c.CompanyId, c.AccountId }, required: true);
        CompanyKeys.References<ExpenseCategory, TaxCategory>(builder, c => new { c.CompanyId, c.DefaultTaxCategoryId }, required: false);
    }
}

internal sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.ToTable("Expenses");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.HasAlternateKey(e => new { e.CompanyId, e.Id });
        builder.Property(e => e.Number).HasMaxLength(40);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Method).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.EmployeeName).HasMaxLength(80);
        builder.Property(e => e.Department).HasMaxLength(60);
        builder.Property(e => e.Reference).HasMaxLength(40);
        builder.Property(e => e.Narration).HasMaxLength(300);
        builder.Property(e => e.RejectionReason).HasMaxLength(300);
        foreach (var name in new[] { nameof(Expense.Taxable), nameof(Expense.Cgst), nameof(Expense.Sgst), nameof(Expense.Igst), nameof(Expense.IneligibleTax), nameof(Expense.Total) })
            builder.Property<decimal>(name).HasConversion(PaiseConverter.Instance).HasColumnName(name + "Paise");
        builder.Property(e => e.CreatedUtc).HasConversion(UtcConverter.Instance);
        builder.Property(e => e.SubmittedUtc).HasConversion(UtcConverter.Nullable);
        builder.Property(e => e.DecidedUtc).HasConversion(UtcConverter.Nullable);
        builder.Property(e => e.PostedUtc).HasConversion(UtcConverter.Nullable);
        builder.Ignore(e => e.IsEditable);
        builder.HasIndex(e => new { e.CompanyId, e.Number }).IsUnique();
        builder.HasIndex(e => new { e.CompanyId, e.Date });
        // One draft per recurring template and due date, so generating twice cannot duplicate (NULLs are distinct).
        builder.HasIndex(e => new { e.CompanyId, e.RecurringExpenseId, e.RecurringDueDate }).IsUnique();
        CompanyKeys.References<Expense, Party>(builder, e => new { e.CompanyId, e.SupplierId }, required: false);
        CompanyKeys.References<Expense, Account>(builder, e => new { e.CompanyId, e.PaidFromAccountId }, required: false);
        CompanyKeys.References<Expense, Branch>(builder, e => new { e.CompanyId, e.BranchId }, required: false);
        CompanyKeys.References<Expense, Voucher>(builder, e => new { e.CompanyId, e.VoucherId }, required: false);
        CompanyKeys.References<Expense, RecurringExpense>(builder, e => new { e.CompanyId, e.RecurringExpenseId }, required: false);
    }
}

internal sealed class ExpenseLineConfiguration : IEntityTypeConfiguration<ExpenseLine>
{
    public void Configure(EntityTypeBuilder<ExpenseLine> builder)
    {
        builder.ToTable("ExpenseLines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.Description).HasMaxLength(200);
        builder.Property(l => l.TaxRatePercent).HasPrecision(6, 3);
        foreach (var name in new[] { nameof(ExpenseLine.Amount), nameof(ExpenseLine.Cgst), nameof(ExpenseLine.Sgst), nameof(ExpenseLine.Igst) })
            builder.Property<decimal>(name).HasConversion(PaiseConverter.Instance).HasColumnName(name + "Paise");
        builder.Ignore(l => l.Tax);
        builder.HasIndex(l => new { l.CompanyId, l.ExpenseId, l.LineNumber }).IsUnique();
        CompanyKeys.References<ExpenseLine, Expense>(builder, l => new { l.CompanyId, l.ExpenseId }, required: true);
        CompanyKeys.References<ExpenseLine, ExpenseCategory>(builder, l => new { l.CompanyId, l.CategoryId }, required: true);
        CompanyKeys.References<ExpenseLine, TaxCategory>(builder, l => new { l.CompanyId, l.TaxCategoryId }, required: false);
    }
}

internal sealed class ExpenseAttachmentConfiguration : IEntityTypeConfiguration<ExpenseAttachment>
{
    public void Configure(EntityTypeBuilder<ExpenseAttachment> builder)
    {
        builder.ToTable("ExpenseAttachments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.FileName).HasMaxLength(120).IsRequired();
        builder.Property(a => a.ContentType).HasMaxLength(40).IsRequired();
        builder.Property(a => a.Sha256).HasMaxLength(64).IsRequired();
        builder.Property(a => a.AddedUtc).HasConversion(UtcConverter.Instance);
        builder.HasIndex(a => new { a.CompanyId, a.ExpenseId });
        CompanyKeys.References<ExpenseAttachment, Expense>(builder, a => new { a.CompanyId, a.ExpenseId }, required: true);
    }
}

internal sealed class ExpenseReversalConfiguration : IEntityTypeConfiguration<ExpenseReversal>
{
    public void Configure(EntityTypeBuilder<ExpenseReversal> builder)
    {
        builder.ToTable("ExpenseReversals");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Reason).HasMaxLength(300).IsRequired();
        builder.Property(r => r.ReversedUtc).HasConversion(UtcConverter.Instance);
        builder.HasIndex(r => new { r.CompanyId, r.ExpenseId }).IsUnique();
        CompanyKeys.References<ExpenseReversal, Expense>(builder, r => new { r.CompanyId, r.ExpenseId }, required: true);
        CompanyKeys.References<ExpenseReversal, Voucher>(builder, r => new { r.CompanyId, r.ReversalVoucherId }, required: true);
    }
}

internal sealed class RecurringExpenseConfiguration : IEntityTypeConfiguration<RecurringExpense>
{
    public void Configure(EntityTypeBuilder<RecurringExpense> builder)
    {
        builder.ToTable("RecurringExpenses");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.HasAlternateKey(r => new { r.CompanyId, r.Id });
        builder.Property(r => r.Name).HasMaxLength(60).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(200);
        builder.Property(r => r.Department).HasMaxLength(60);
        builder.Property(r => r.Frequency).HasConversion<string>().HasMaxLength(12);
        builder.Property(r => r.Method).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Amount).HasConversion(PaiseConverter.Instance).HasColumnName("AmountPaise");
        builder.HasIndex(r => new { r.CompanyId, r.Name }).IsUnique();
        CompanyKeys.References<RecurringExpense, ExpenseCategory>(builder, r => new { r.CompanyId, r.CategoryId }, required: true);
        CompanyKeys.References<RecurringExpense, TaxCategory>(builder, r => new { r.CompanyId, r.TaxCategoryId }, required: false);
        CompanyKeys.References<RecurringExpense, Party>(builder, r => new { r.CompanyId, r.SupplierId }, required: false);
        CompanyKeys.References<RecurringExpense, Account>(builder, r => new { r.CompanyId, r.PaidFromAccountId }, required: false);
        CompanyKeys.References<RecurringExpense, Branch>(builder, r => new { r.CompanyId, r.BranchId }, required: false);
    }
}

internal sealed class ExpenseSettingsConfiguration : IEntityTypeConfiguration<ExpenseSettings>
{
    public void Configure(EntityTypeBuilder<ExpenseSettings> builder)
    {
        builder.ToTable("ExpenseSettings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.HasIndex(s => s.CompanyId).IsUnique();
    }
}

internal sealed class LeadConfiguration : IEntityTypeConfiguration<Lead>
{
    public void Configure(EntityTypeBuilder<Lead> builder)
    {
        builder.ToTable("Leads");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.HasAlternateKey(l => new { l.CompanyId, l.Id });
        builder.Property(l => l.Name).HasMaxLength(120).IsRequired();
        builder.Property(l => l.Organisation).HasMaxLength(120);
        builder.Property(l => l.Phone).HasMaxLength(20);
        builder.Property(l => l.Email).HasMaxLength(120);
        builder.Property(l => l.Notes).HasMaxLength(1000);
        builder.Property(l => l.LostReason).HasMaxLength(300);
        builder.Property(l => l.Source).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.CreatedUtc).HasConversion(UtcConverter.Instance);
        builder.HasIndex(l => new { l.CompanyId, l.Status });
        CompanyKeys.References<Lead, Party>(builder, l => new { l.CompanyId, l.CustomerId }, required: false);
    }
}

internal sealed class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> builder)
    {
        builder.ToTable("Contacts");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(120).IsRequired();
        builder.Property(c => c.Role).HasMaxLength(60);
        builder.Property(c => c.Phone).HasMaxLength(20);
        builder.Property(c => c.Email).HasMaxLength(120);
        builder.HasIndex(c => new { c.CompanyId, c.PartyId });
        CompanyKeys.References<Contact, Party>(builder, c => new { c.CompanyId, c.PartyId }, required: true);
    }
}

internal sealed class EnquiryConfiguration : IEntityTypeConfiguration<Enquiry>
{
    public void Configure(EntityTypeBuilder<Enquiry> builder)
    {
        builder.ToTable("Enquiries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.HasAlternateKey(e => new { e.CompanyId, e.Id });
        builder.Property(e => e.Subject).HasMaxLength(120).IsRequired();
        builder.Property(e => e.Details).HasMaxLength(1000);
        builder.Property(e => e.Channel).HasConversion<string>().HasMaxLength(12);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(12);
        builder.Property(e => e.CreatedUtc).HasConversion(UtcConverter.Instance);
        CompanyKeys.References<Enquiry, Lead>(builder, e => new { e.CompanyId, e.LeadId }, required: false);
        CompanyKeys.References<Enquiry, Party>(builder, e => new { e.CompanyId, e.CustomerId }, required: false);
        CompanyKeys.References<Enquiry, Product>(builder, e => new { e.CompanyId, e.ProductId }, required: false);
    }
}

internal sealed class FollowUpConfiguration : IEntityTypeConfiguration<FollowUp>
{
    public void Configure(EntityTypeBuilder<FollowUp> builder)
    {
        builder.ToTable("FollowUps");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();
        builder.Property(f => f.Title).HasMaxLength(120).IsRequired();
        builder.Property(f => f.Notes).HasMaxLength(1000);
        builder.Property(f => f.CreatedUtc).HasConversion(UtcConverter.Instance);
        builder.Property(f => f.DoneUtc).HasConversion(UtcConverter.Nullable);
        builder.HasIndex(f => new { f.CompanyId, f.DueDate });
        // One reminder per maintenance schedule and due date.
        builder.HasIndex(f => new { f.CompanyId, f.ScheduleId, f.DueDate }).IsUnique();
        CompanyKeys.References<FollowUp, Lead>(builder, f => new { f.CompanyId, f.LeadId }, required: false);
        CompanyKeys.References<FollowUp, Party>(builder, f => new { f.CompanyId, f.CustomerId }, required: false);
        CompanyKeys.References<FollowUp, Enquiry>(builder, f => new { f.CompanyId, f.EnquiryId }, required: false);
        CompanyKeys.References<FollowUp, ServiceJob>(builder, f => new { f.CompanyId, f.ServiceJobId }, required: false);
        CompanyKeys.References<FollowUp, MaintenanceSchedule>(builder, f => new { f.CompanyId, f.ScheduleId }, required: false);
    }
}

internal sealed class ActivityConfiguration : IEntityTypeConfiguration<Activity>
{
    public void Configure(EntityTypeBuilder<Activity> builder)
    {
        builder.ToTable("Activities");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Kind).HasConversion<string>().HasMaxLength(12);
        builder.Property(a => a.Summary).HasMaxLength(2000).IsRequired();
        builder.Property(a => a.OccurredUtc).HasConversion(UtcConverter.Instance);
        builder.Property(a => a.RecordedUtc).HasConversion(UtcConverter.Instance);
        builder.HasIndex(a => new { a.CompanyId, a.CustomerId });
        builder.HasIndex(a => new { a.CompanyId, a.LeadId });
        CompanyKeys.References<Activity, Lead>(builder, a => new { a.CompanyId, a.LeadId }, required: false);
        CompanyKeys.References<Activity, Party>(builder, a => new { a.CompanyId, a.CustomerId }, required: false);
    }
}

internal sealed class ServiceJobConfiguration : IEntityTypeConfiguration<ServiceJob>
{
    public void Configure(EntityTypeBuilder<ServiceJob> builder)
    {
        builder.ToTable("ServiceJobs");
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).ValueGeneratedNever();
        builder.HasAlternateKey(j => new { j.CompanyId, j.Id });
        builder.Property(j => j.Number).HasMaxLength(40).IsRequired();
        builder.Property(j => j.ItemDescription).HasMaxLength(120).IsRequired();
        builder.Property(j => j.SerialNumber).HasMaxLength(60);
        builder.Property(j => j.Fault).HasMaxLength(1000).IsRequired();
        builder.Property(j => j.Accessories).HasMaxLength(300);
        builder.Property(j => j.Diagnosis).HasMaxLength(2000);
        builder.Property(j => j.CancelReason).HasMaxLength(300);
        builder.Property(j => j.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(j => j.Charge).HasConversion<string>().HasMaxLength(12);
        builder.Property(j => j.CreatedUtc).HasConversion(UtcConverter.Instance);
        builder.HasIndex(j => new { j.CompanyId, j.Number }).IsUnique();
        builder.HasIndex(j => new { j.CompanyId, j.SerialNumber });
        builder.HasIndex(j => new { j.CompanyId, j.CustomerId });
        CompanyKeys.References<ServiceJob, Party>(builder, j => new { j.CompanyId, j.CustomerId }, required: true);
        CompanyKeys.References<ServiceJob, Product>(builder, j => new { j.CompanyId, j.ProductId }, required: false);
        CompanyKeys.References<ServiceJob, Warehouse>(builder, j => new { j.CompanyId, j.WarehouseId }, required: true);
        CompanyKeys.References<ServiceJob, BillingDocument>(builder, j => new { j.CompanyId, j.InvoiceId }, required: false);
    }
}

internal sealed class ServicePartConfiguration : IEntityTypeConfiguration<ServicePart>
{
    public void Configure(EntityTypeBuilder<ServicePart> builder)
    {
        builder.ToTable("ServiceParts");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.HasAlternateKey(p => new { p.CompanyId, p.Id });
        builder.Property(p => p.Description).HasMaxLength(200).IsRequired();
        builder.Property(p => p.BatchNumber).HasMaxLength(40);
        builder.Property(p => p.SerialNumber).HasMaxLength(60);
        builder.Property(p => p.Charge).HasConversion<string>().HasMaxLength(12);
        builder.Property(p => p.Quantity).HasConversion(QuantityConverter.Instance).HasColumnName("QuantityMilli");
        builder.Property(p => p.Cost).HasConversion(PaiseConverter.Instance).HasColumnName("CostPaise");
        builder.Property(p => p.IssuedUtc).HasConversion(UtcConverter.Instance);
        builder.HasIndex(p => new { p.CompanyId, p.JobId });
        CompanyKeys.References<ServicePart, ServiceJob>(builder, p => new { p.CompanyId, p.JobId }, required: true);
        CompanyKeys.References<ServicePart, Product>(builder, p => new { p.CompanyId, p.ProductId }, required: true);
        CompanyKeys.References<ServicePart, Warehouse>(builder, p => new { p.CompanyId, p.WarehouseId }, required: true);
        CompanyKeys.References<ServicePart, Batch>(builder, p => new { p.CompanyId, p.BatchId }, required: false);
        CompanyKeys.References<ServicePart, StockDocument>(builder, p => new { p.CompanyId, p.StockDocumentId }, required: true);
        CompanyKeys.References<ServicePart, Voucher>(builder, p => new { p.CompanyId, p.VoucherId }, required: false);
    }
}

internal sealed class ServicePartReturnConfiguration : IEntityTypeConfiguration<ServicePartReturn>
{
    public void Configure(EntityTypeBuilder<ServicePartReturn> builder)
    {
        builder.ToTable("ServicePartReturns");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.ReturnedUtc).HasConversion(UtcConverter.Instance);
        builder.HasIndex(r => new { r.CompanyId, r.PartId }).IsUnique();
        CompanyKeys.References<ServicePartReturn, ServicePart>(builder, r => new { r.CompanyId, r.PartId }, required: true);
        CompanyKeys.References<ServicePartReturn, StockDocument>(builder, r => new { r.CompanyId, r.StockDocumentId }, required: true);
        CompanyKeys.References<ServicePartReturn, Voucher>(builder, r => new { r.CompanyId, r.VoucherId }, required: false);
    }
}

internal sealed class ServiceLabourConfiguration : IEntityTypeConfiguration<ServiceLabour>
{
    public void Configure(EntityTypeBuilder<ServiceLabour> builder)
    {
        builder.ToTable("ServiceLabour");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.Description).HasMaxLength(200).IsRequired();
        builder.Property(l => l.SacCode).HasMaxLength(8);
        builder.Property(l => l.Quantity).HasConversion(QuantityConverter.Instance).HasColumnName("QuantityMilli");
        builder.Property(l => l.Rate).HasConversion(PaiseConverter.Instance).HasColumnName("RatePaise");
        builder.HasIndex(l => new { l.CompanyId, l.JobId });
        CompanyKeys.References<ServiceLabour, ServiceJob>(builder, l => new { l.CompanyId, l.JobId }, required: true);
        CompanyKeys.References<ServiceLabour, TaxCategory>(builder, l => new { l.CompanyId, l.TaxCategoryId }, required: false);
    }
}

internal sealed class MaintenanceScheduleConfiguration : IEntityTypeConfiguration<MaintenanceSchedule>
{
    public void Configure(EntityTypeBuilder<MaintenanceSchedule> builder)
    {
        builder.ToTable("MaintenanceSchedules");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.HasAlternateKey(m => new { m.CompanyId, m.Id });
        builder.Property(m => m.ItemDescription).HasMaxLength(120).IsRequired();
        builder.Property(m => m.SerialNumber).HasMaxLength(60);
        builder.Property(m => m.Notes).HasMaxLength(300);
        builder.HasIndex(m => new { m.CompanyId, m.CustomerId });
        CompanyKeys.References<MaintenanceSchedule, Party>(builder, m => new { m.CompanyId, m.CustomerId }, required: true);
        CompanyKeys.References<MaintenanceSchedule, Product>(builder, m => new { m.CompanyId, m.ProductId }, required: false);
    }
}

internal sealed class ServiceSettingsConfiguration : IEntityTypeConfiguration<ServiceSettings>
{
    public void Configure(EntityTypeBuilder<ServiceSettings> builder)
    {
        builder.ToTable("ServiceSettings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.DefaultLabourSac).HasMaxLength(8);
        builder.HasIndex(s => s.CompanyId).IsUnique();
    }
}
