using Nexora.Application.Abstractions;
using Nexora.Application.Billing;
using Nexora.Application.Crm;
using Nexora.Application.Inventory;
using Nexora.Application.Security;
using Nexora.Domain.Accounting;
using Nexora.Domain.Audit;
using Nexora.Domain.Billing;
using Nexora.Domain.Common;
using Nexora.Domain.Companies;
using Nexora.Domain.Crm;
using Nexora.Domain.Inventory;
using Nexora.Domain.Security;
using Nexora.Domain.Service;
using Nexora.Domain.Users;

namespace Nexora.Application.Service;

public sealed record ServiceJobInput(Guid? Id, DateOnly ReceivedOn, Guid CustomerId, string ItemDescription, string Fault, Guid WarehouseId, Guid? ProductId = null,
    string SerialNumber = "", string Accessories = "", string Diagnosis = "", Guid? TechnicianUserId = null, DateOnly? EstimatedCompletion = null);

public sealed record ServiceJobRow(Guid Id, string Number, DateOnly ReceivedOn, Guid CustomerId, string Customer, string Item, string SerialNumber, ServiceStatus Status,
    ServiceCharge Charge, string Technician, DateOnly? EstimatedCompletion, DateOnly? WarrantyUntil, string Invoice, bool IsOverdue)
{
    public string StatusLabel => Status.Label();
}

public sealed record ServicePartRow(Guid Id, Guid ProductId, string Sku, string Description, decimal Quantity, string Unit, string Batch, string? SerialNumber,
    decimal? Cost, ServiceCharge Charge, DateOnly Date, bool IsReturned, bool IsBilled);

public sealed record ServiceLabourRow(Guid Id, string Description, string SacCode, decimal Quantity, decimal Rate, Guid? TaxCategoryId, decimal Amount);

public sealed record ServiceJobView(Guid Id, string Number, DateOnly ReceivedOn, Guid CustomerId, string Customer, string CustomerPhone, Guid? ProductId,
    string Item, string SerialNumber, string Fault, string Accessories, string Diagnosis, ServiceCharge Charge, DateOnly? WarrantyUntil, string WarrantySource,
    Guid? TechnicianUserId, string Technician, DateOnly? EstimatedCompletion, Guid WarehouseId, string Warehouse, ServiceStatus Status, string CancelReason,
    Guid? InvoiceId, string? InvoiceNumber, BillingStatus? InvoiceState, bool InvoiceCancelled, DateOnly? DeliveredOn, IReadOnlyList<ServiceStatus> NextStatuses,
    IReadOnlyList<ServicePartRow> Parts, IReadOnlyList<ServiceLabourRow> Labour, decimal? PartsCost)
{
    public string StatusLabel => Status.Label();
}

public sealed record ServicePartInput(Guid ProductId, decimal Quantity, DateOnly Date, Guid? UnitId = null, Guid? BatchId = null, string? SerialNumber = null,
    string? BatchNumber = null);

public sealed record ServiceLabourInput(string Description, decimal Quantity, decimal Rate, string SacCode = "", Guid? TaxCategoryId = null);

public sealed record MaintenanceInput(Guid? Id, Guid CustomerId, string ItemDescription, int IntervalMonths, DateOnly NextDueDate, Guid? ProductId = null,
    string SerialNumber = "", Guid? AssigneeUserId = null, string Notes = "");

public sealed record MaintenanceRow(Guid Id, Guid CustomerId, string Customer, string Item, string SerialNumber, int IntervalMonths, DateOnly NextDueDate,
    Guid? AssigneeUserId, string Assignee, string Notes, bool IsActive, Guid? ProductId);

/// <summary>Warranty of a serial number sold by this company: the sale, the product's warranty months and the end date.</summary>
public sealed record WarrantyInfo(string SerialNumber, Guid? ProductId, string? InvoiceNumber, DateOnly? SoldOn, int WarrantyMonths, DateOnly? WarrantyUntil)
{
    public bool IsCovered(DateOnly on) => WarrantyUntil is { } until && on <= until;
}

/// <summary>
/// Service tickets and repair job cards. Spare parts are issued through <see cref="StockService"/> (so stock rules,
/// serials, batches and costing are the inventory engine's) with a voucher in the same transaction: Cost of Goods Sold
/// for chargeable jobs, Warranty and Service Costs for warranty jobs. Chargeable jobs are invoiced through
/// <see cref="BillingService"/>, with parts as issued parts (no second stock movement) and labour as service lines.
/// service.view to read, service.manage to change. See docs/ACCOUNTING_MAP.md, "Service".
/// </summary>
public sealed class ServiceJobService(IUnitOfWorkFactory units, IAuthorizer authorizer, ILedgerPoster ledger, StockService stock, BillingService billing, IClock clock)
{
    internal const string SourceType = "service";

    // ----- Jobs -----

    public async Task<IReadOnlyList<ServiceJobRow>> ListAsync(bool includeClosed = false, string? search = null, Guid? customerId = null, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.ServiceView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var jobs = await unit.ListAsync<ServiceJob>(j => (includeClosed || (j.Status != ServiceStatus.Delivered && j.Status != ServiceStatus.Cancelled))
            && (customerId == null || j.CustomerId == customerId), cancellationToken);
        var customers = (await unit.ListAsync<Party>(p => p.Kind == PartyKind.Customer, cancellationToken)).ToDictionary(p => p.Id, p => p.Name);
        var users = await NamesAsync(unit, cancellationToken);
        var invoices = (await unit.ListAsync<BillingDocument>(d => d.Kind == BillingDocumentKind.SalesInvoice, cancellationToken)).ToDictionary(d => d.Id, d => d.Number ?? "(draft)");
        var today = Today();
        var text = (search ?? string.Empty).Trim();
        return jobs
            .Select(j => new ServiceJobRow(j.Id, j.Number, j.ReceivedOn, j.CustomerId, customers.GetValueOrDefault(j.CustomerId, "?"), j.ItemDescription, j.SerialNumber,
                j.Status, j.Charge, j.TechnicianUserId is { } t ? users.GetValueOrDefault(t, "?") : string.Empty, j.EstimatedCompletion, j.WarrantyUntil,
                j.InvoiceId is { } i ? invoices.GetValueOrDefault(i, string.Empty) : string.Empty,
                !j.Status.IsClosed() && j.EstimatedCompletion is { } eta && eta < today))
            .Where(r => text.Length == 0 || r.Number.Contains(text, StringComparison.OrdinalIgnoreCase) || r.Customer.Contains(text, StringComparison.OrdinalIgnoreCase)
                || r.SerialNumber.Contains(text, StringComparison.OrdinalIgnoreCase) || r.Item.Contains(text, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.ReceivedOn).ThenByDescending(r => r.Number, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Jobs for one customer or one serial number, newest first (service history).</summary>
    public async Task<IReadOnlyList<ServiceHistoryRow>> HistoryAsync(Guid? customerId, string? serialNumber, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.ServiceView);
        var serial = (serialNumber ?? string.Empty).Trim().ToUpperInvariant();
        if (customerId is null && serial.Length == 0) throw new WorkspaceValidationException("Choose a customer or enter a serial number.");
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var jobs = await unit.ListAsync<ServiceJob>(j => (customerId == null || j.CustomerId == customerId) && (serial.Length == 0 || j.SerialNumber == serial), cancellationToken);
        var customers = (await unit.ListAsync<Party>(p => p.Kind == PartyKind.Customer, cancellationToken)).ToDictionary(p => p.Id, p => p.Name);
        return jobs.OrderByDescending(j => j.ReceivedOn).ThenByDescending(j => j.Number, StringComparer.Ordinal)
            .Select(j => new ServiceHistoryRow(j.Id, j.Number, j.ReceivedOn, customers.GetValueOrDefault(j.CustomerId, "?"), j.ItemDescription, j.SerialNumber, j.Fault,
                j.Status, j.Charge, j.DeliveredOn))
            .ToList();
    }

    /// <summary>
    /// Whether a serial number was sold by this company: the latest posted, uncancelled sales invoice line with that serial,
    /// and the product's warranty months from then. Null when the serial was not sold here.
    /// </summary>
    public async Task<WarrantyInfo?> WarrantyAsync(string serialNumber, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.ServiceView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        return await WarrantyAsync(unit, serialNumber, cancellationToken);
    }

    public async Task<Guid> CreateAsync(ServiceJobInput input, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.ServiceManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        await CustomerAsync(unit, input.CustomerId, cancellationToken);
        var number = await NumberAllocator.NextAsync(unit, session.CompanyId, ServiceDocumentTypes.Job, FinancialYear.Containing(input.ReceivedOn),
            candidate => unit.AnyAsync<ServiceJob>(j => j.Number == candidate, cancellationToken), cancellationToken);
        var job = new ServiceJob(Guid.NewGuid(), session.CompanyId, number, input.ReceivedOn, input.CustomerId, clock.UtcNow, session.UserId);
        unit.Add(job);
        var warranty = await ApplyAsync(unit, job, input, cancellationToken);
        // A job starts as a warranty job when the serial was sold here and is still covered on the day it was received.
        Guard(() => job.SetCharge(warranty?.IsCovered(input.ReceivedOn) == true ? ServiceCharge.Warranty : ServiceCharge.Chargeable, partsIssued: false));
        unit.Record(new AuditEvent("Created", nameof(ServiceJob), job.Id.ToString(), $"Received job {number} for {input.ItemDescription.Trim()}"));
        await unit.CommitAsync(cancellationToken);
        return job.Id;
    }

    public async Task UpdateAsync(ServiceJobInput input, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.ServiceManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var job = await JobAsync(unit, input.Id ?? Guid.Empty, cancellationToken);
        if (job.CustomerId != input.CustomerId) throw new WorkspaceValidationException("The customer of a job cannot change. Cancel it and create a new job.");
        await ApplyAsync(unit, job, input, cancellationToken);
        await unit.CommitAsync(cancellationToken);
    }

    public async Task SetChargeAsync(Guid jobId, ServiceCharge charge, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.ServiceManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var job = await JobAsync(unit, jobId, cancellationToken);
        var live = await LivePartsAsync(unit, jobId, cancellationToken);
        Guard(() => job.SetCharge(charge, live.Count > 0));
        unit.Record(new AuditEvent("ChargeChanged", nameof(ServiceJob), jobId.ToString(), $"Job {job.Number} is now {charge.ToString().ToLowerInvariant()}"));
        await unit.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Moves the job along the workflow. Cancelling needs every issued part returned and no posted invoice. Delivering a
    /// chargeable job with charges needs its invoice posted.
    /// </summary>
    public async Task MoveAsync(Guid jobId, ServiceStatus status, string reason = "", DateOnly? date = null, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.ServiceManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var job = await JobAsync(unit, jobId, cancellationToken);
        if (status == ServiceStatus.Cancelled)
        {
            if ((await LivePartsAsync(unit, jobId, cancellationToken)).Count > 0) throw new WorkspaceValidationException("Return the issued parts to stock before cancelling the job.");
            if (await LiveInvoiceAsync(unit, job, cancellationToken) is { Status: BillingStatus.Posted } posted)
                throw new WorkspaceValidationException($"Invoice {posted.Number} has been posted for this job. Cancel the invoice first.");
        }

        if (status == ServiceStatus.Delivered)
        {
            if (job.Charge == ServiceCharge.Chargeable && await HasChargesAsync(unit, jobId, cancellationToken)
                && await LiveInvoiceAsync(unit, job, cancellationToken) is not { Status: BillingStatus.Posted })
                throw new WorkspaceValidationException("Raise and post the invoice for this chargeable job before delivering it.");
            Guard(() => job.MarkDelivered(date ?? Today()));
        }
        else
        {
            Guard(() => job.MoveTo(status, reason));
        }

        unit.Record(new AuditEvent("StatusChanged", nameof(ServiceJob), jobId.ToString(), $"Job {job.Number} moved to {status.Label().ToLowerInvariant()}"));
        await unit.CommitAsync(cancellationToken);
    }

    public async Task<ServiceJobView> GetAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.ServiceView);
        var showCost = authorizer.Has(Permissions.InventoryViewCost);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var job = await unit.FindAsync<ServiceJob>(jobId, cancellationToken) ?? throw new WorkspaceValidationException("That job no longer exists.");
        var customer = await unit.FindAsync<Party>(job.CustomerId, cancellationToken);
        var users = await NamesAsync(unit, cancellationToken);
        var warehouse = await unit.FindAsync<Warehouse>(job.WarehouseId, cancellationToken);
        var parts = await unit.ListAsync<ServicePart>(p => p.JobId == jobId, cancellationToken);
        var partIds = parts.Select(p => p.Id).ToList();
        var returned = (await unit.ListAsync<ServicePartReturn>(r => partIds.Contains(r.PartId), cancellationToken)).Select(r => r.PartId).ToHashSet();
        var billed = await BilledPartsAsync(unit, partIds, cancellationToken);
        var productIds = parts.Select(p => p.ProductId).ToList();
        var products = (await unit.ListAsync<Product>(p => productIds.Contains(p.Id), cancellationToken)).ToDictionary(p => p.Id);
        var unitNames = (await unit.ListAsync<Unit>(cancellationToken: cancellationToken)).ToDictionary(u => u.Id, u => u.Code);
        var labour = await unit.ListAsync<ServiceLabour>(l => l.JobId == jobId, cancellationToken);
        var invoice = job.InvoiceId is { } invoiceId ? await unit.FindAsync<BillingDocument>(invoiceId, cancellationToken) : null;
        var invoiceCancelled = invoice is not null && await unit.AnyAsync<BillingCancellation>(c => c.DocumentId == invoice.Id, cancellationToken);
        var warrantySource = job.WarrantyUntil is null ? "Not sold by this company (or no serial)" : $"Sold by this company; warranty until {job.WarrantyUntil:dd-MM-yyyy}";
        return new ServiceJobView(job.Id, job.Number, job.ReceivedOn, job.CustomerId, customer?.Name ?? "?", customer?.Phone ?? string.Empty, job.ProductId, job.ItemDescription,
            job.SerialNumber, job.Fault, job.Accessories, job.Diagnosis, job.Charge, job.WarrantyUntil, warrantySource, job.TechnicianUserId,
            job.TechnicianUserId is { } t ? users.GetValueOrDefault(t, "?") : string.Empty, job.EstimatedCompletion, job.WarehouseId, warehouse?.Name ?? "?", job.Status,
            job.CancelReason, job.InvoiceId, invoice?.Number, invoice?.Status, invoiceCancelled, job.DeliveredOn, ServiceWorkflow.Next(job.Status),
            parts.OrderBy(p => p.IssuedUtc).Select(p => new ServicePartRow(p.Id, p.ProductId, products.TryGetValue(p.ProductId, out var pr) ? pr.Sku : "?", p.Description,
                p.Quantity, products.TryGetValue(p.ProductId, out var pu) ? unitNames.GetValueOrDefault(pu.UnitId, "?") : "?", p.BatchNumber, p.SerialNumber,
                showCost ? p.Cost : null, p.Charge, p.Date, returned.Contains(p.Id), billed.Contains(p.Id))).ToList(),
            labour.Select(l => new ServiceLabourRow(l.Id, l.Description, l.SacCode, l.Quantity, l.Rate, l.TaxCategoryId, Money.Round(l.Quantity * l.Rate))).ToList(),
            showCost ? parts.Where(p => !returned.Contains(p.Id)).Sum(p => p.Cost) : null);
    }

    // ----- Spare parts -----

    /// <summary>
    /// Issues a spare part from the job's warehouse through the stock service, in one transaction with its voucher:
    /// Cost of Goods Sold (chargeable) or Warranty and Service Costs (warranty) debited, Inventory credited.
    /// Negative-stock, batch and serial rules apply as for any issue.
    /// </summary>
    public async Task<Guid> IssuePartAsync(Guid jobId, ServicePartInput input, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.ServiceManage);
        await ledger.EnsureAccountAsync(LedgerAccountRole.WarrantyExpense, cancellationToken);
        using var lockHandle = await ledger.AcquireAsync(cancellationToken);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var job = await JobAsync(unit, jobId, cancellationToken);
        if (job.Status == ServiceStatus.Ready) throw new WorkspaceValidationException("The job is ready for delivery. Move it back to In repair to issue more parts.");
        if (input.Date < job.ReceivedOn) throw new WorkspaceValidationException("Parts cannot be issued before the job was received.");
        var product = await unit.FindAsync<Product>(input.ProductId, cancellationToken) ?? throw new WorkspaceValidationException("Choose the part.");
        var issued = (await unit.ListAsync<ServicePart>(p => p.JobId == jobId, cancellationToken)).Count;
        var number = $"{job.Number}/P{issued + 1}";
        var baseQuantity = await BaseQuantityAsync(unit, product, input.Quantity, input.UnitId, cancellationToken);
        var posted = await stock.PostCoreAsync(unit, session, new StockDocumentInput(StockDocumentType.ServiceIssue, input.Date, job.WarehouseId,
            $"Parts for job {job.Number}", [new StockLineInput(product.Id, input.Quantity, input.UnitId, BatchId: input.BatchId, BatchNumber: input.BatchNumber, SerialNumber: input.SerialNumber)]),
            number, cancellationToken);
        var line = posted.Lines[0];
        var cost = -line.StockValue;
        var account = job.Charge == ServiceCharge.Warranty ? LedgerAccountRole.WarrantyExpense : LedgerAccountRole.CostOfGoodsSold;
        Guid? voucherId = null;
        if (cost != 0)
        {
            voucherId = await ledger.PostAsync(unit, new LedgerPosting(input.Date, $"Parts for service job {job.Number} ({job.Charge.ToString().ToLowerInvariant()})", SourceType,
                job.Id, [new(await ledger.AccountAsync(unit, account, cancellationToken), cost, 0, product.Sku), new(await ledger.AccountAsync(unit, LedgerAccountRole.Inventory, cancellationToken), 0, cost)]),
                cancellationToken);
            posted.Document.AttachVoucher(voucherId.Value);
        }

        var batch = line.BatchId is { } b ? await unit.FindAsync<Batch>(b, cancellationToken) : null;
        var part = new ServicePart(Guid.NewGuid(), session.CompanyId, jobId, product.Id, product.DisplayName, baseQuantity, job.WarehouseId, line.BatchId,
            batch?.Number ?? string.Empty, string.IsNullOrWhiteSpace(input.SerialNumber) ? null : input.SerialNumber.Trim().ToUpperInvariant(), cost, job.Charge,
            posted.Document.Id, voucherId, input.Date, clock.UtcNow, session.UserId);
        unit.Add(part);
        unit.Record(new AuditEvent("PartIssued", nameof(ServiceJob), jobId.ToString(), $"Issued {baseQuantity:0.###} × {product.Sku} for job {job.Number}"));
        await unit.CommitAsync(cancellationToken);
        return part.Id;
    }

    /// <summary>Takes an unbilled part back into stock at the cost it left with, reversing its cost posting.</summary>
    public async Task ReturnPartAsync(Guid partId, DateOnly date, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.ServiceManage);
        await ledger.EnsureAccountAsync(LedgerAccountRole.WarrantyExpense, cancellationToken);
        using var lockHandle = await ledger.AcquireAsync(cancellationToken);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var part = await unit.FindAsync<ServicePart>(partId, cancellationToken) ?? throw new WorkspaceValidationException("That part no longer exists.");
        var job = await JobAsync(unit, part.JobId, cancellationToken);
        if (await unit.AnyAsync<ServicePartReturn>(r => r.PartId == partId, cancellationToken)) throw new WorkspaceValidationException("That part has already been returned.");
        if ((await BilledPartsAsync(unit, [partId], cancellationToken)).Count > 0)
            throw new WorkspaceValidationException("The part is on a posted invoice. Make a credit note for it instead.");
        if (date < part.Date) throw new WorkspaceValidationException("A part cannot be returned before it was issued.");
        var jobPartIds = (await unit.ListAsync<ServicePart>(p => p.JobId == job.Id, cancellationToken)).Select(p => p.Id).ToList();
        var returnsSoFar = (await unit.ListAsync<ServicePartReturn>(r => jobPartIds.Contains(r.PartId), cancellationToken)).Count;
        var number = $"{job.Number}/R{returnsSoFar + 1}";
        var posted = await stock.PostCoreAsync(unit, session, new StockDocumentInput(StockDocumentType.ServiceReturn, date, part.WarehouseId, $"Parts returned from job {job.Number}",
            [new StockLineInput(part.ProductId, part.Quantity, BatchId: part.BatchId, SerialNumber: part.SerialNumber, LineValue: part.Cost)]), number, cancellationToken);
        var value = posted.Lines[0];
        Guid? voucherId = null;
        if (value.EnteredValue != 0 || value.StockValue != 0)
        {
            var costAccount = await ledger.AccountAsync(unit, part.Charge == ServiceCharge.Warranty ? LedgerAccountRole.WarrantyExpense : LedgerAccountRole.CostOfGoodsSold, cancellationToken);
            var lines = new List<LedgerPostingLine> { new(await ledger.AccountAsync(unit, LedgerAccountRole.Inventory, cancellationToken), value.StockValue, 0), new(costAccount, 0, value.EnteredValue) };
            var settlement = value.EnteredValue - value.StockValue;
            if (settlement != 0)
            {
                var adjustment = await ledger.AccountAsync(unit, LedgerAccountRole.InventoryAdjustment, cancellationToken);
                lines.Add(settlement > 0 ? new(adjustment, settlement, 0) : new(adjustment, 0, -settlement));
            }

            voucherId = await ledger.PostAsync(unit, new LedgerPosting(date, $"Parts returned from service job {job.Number}", SourceType, job.Id, lines.Where(l => l.Debit + l.Credit != 0).ToList()),
                cancellationToken);
            posted.Document.AttachVoucher(voucherId.Value);
        }

        unit.Add(new ServicePartReturn(Guid.NewGuid(), session.CompanyId, partId, posted.Document.Id, voucherId, date, clock.UtcNow, session.UserId));
        unit.Record(new AuditEvent("PartReturned", nameof(ServiceJob), job.Id.ToString(), $"Returned {part.Quantity:0.###} × {part.Description} from job {job.Number}"));
        await unit.CommitAsync(cancellationToken);
    }

    // ----- Labour and invoicing -----

    public async Task<Guid> AddLabourAsync(Guid jobId, ServiceLabourInput input, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.ServiceManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var job = await JobAsync(unit, jobId, cancellationToken);
        await EnsureNotInvoicedAsync(unit, job, cancellationToken);
        if (input.TaxCategoryId is { } tax && await unit.FindAsync<TaxCategory>(tax, cancellationToken) is null) throw new WorkspaceValidationException("That tax category no longer exists.");
        var settings = (await unit.ListAsync<ServiceSettings>(cancellationToken: cancellationToken)).SingleOrDefault();
        var sac = string.IsNullOrWhiteSpace(input.SacCode) ? settings?.DefaultLabourSac ?? string.Empty : input.SacCode;
        var labour = Guard(() => new ServiceLabour(Guid.NewGuid(), session.CompanyId, jobId, input.Description, sac, input.Quantity, input.Rate, input.TaxCategoryId));
        unit.Add(labour);
        await unit.CommitAsync(cancellationToken);
        return labour.Id;
    }

    public async Task RemoveLabourAsync(Guid labourId, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.ServiceManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var labour = await unit.FindAsync<ServiceLabour>(labourId, cancellationToken) ?? throw new WorkspaceValidationException("That labour line no longer exists.");
        var job = await JobAsync(unit, labour.JobId, cancellationToken);
        await EnsureNotInvoicedAsync(unit, job, cancellationToken);
        unit.Remove(labour);
        await unit.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Creates the sales invoice draft for a chargeable job through the billing flow: each part still issued (at the
    /// customer's or the product's selling price, billed without moving stock again) and each labour line as a service
    /// line. The draft is posted from Sales invoices like any other (credit limit, numbering and GST apply there).
    /// </summary>
    public async Task<Guid> CreateInvoiceAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.ServiceManage);
        authorizer.Demand(Permissions.SalesCreate);
        BillingDraftInput draft;
        await using (var unit = await units.BeginAsync(readOnly: true, cancellationToken))
        {
            var job = await unit.FindAsync<ServiceJob>(jobId, cancellationToken) ?? throw new WorkspaceValidationException("That job no longer exists.");
            Guard(job.EnsureOpen);
            if (job.Charge == ServiceCharge.Warranty) throw new WorkspaceValidationException("This is a warranty job: there is nothing to invoice. Its parts cost was posted to Warranty and Service Costs.");
            if (await LiveInvoiceAsync(unit, job, cancellationToken) is { } existing)
                throw new WorkspaceValidationException($"Invoice {existing.Number ?? "(draft)"} already exists for this job. Open it under Sales invoices.");
            var parts = await LivePartsAsync(unit, jobId, cancellationToken);
            var labour = await unit.ListAsync<ServiceLabour>(l => l.JobId == jobId, cancellationToken);
            if (parts.Count == 0 && labour.Count == 0) throw new WorkspaceValidationException("Add parts or labour to the job before invoicing it.");
            var lines = parts.Select(p => new BillingLineInput(p.ProductId, p.Quantity, LineType: BillingLineType.IssuedPart, SourceId: p.Id, SerialNumber: p.SerialNumber,
                    BatchId: p.BatchId))
                .Concat(labour.Select(l => new BillingLineInput(Guid.Empty, l.Quantity, Rate: l.Rate, Description: l.Description, LineType: BillingLineType.Service,
                    TaxCategoryId: l.TaxCategoryId, HsnCode: l.SacCode)))
                .ToList();
            draft = new BillingDraftInput(null, BillingDocumentKind.SalesInvoice, Today() < job.ReceivedOn ? job.ReceivedOn : Today(), job.CustomerId, job.WarehouseId,
                SettlementMode.Credit, lines, Reference: job.Number, Narration: $"Service job {job.Number}: {job.ItemDescription}");
        }

        var invoiceId = await billing.SaveDraftAsync(draft, cancellationToken);
        await using (var unit = await units.BeginAsync(cancellationToken: cancellationToken))
        {
            var job = await JobAsync(unit, jobId, cancellationToken);
            job.LinkInvoice(invoiceId);
            unit.Record(new AuditEvent("Invoiced", nameof(ServiceJob), jobId.ToString(), $"Invoice draft created for job {job.Number}"));
            await unit.CommitAsync(cancellationToken);
        }

        return invoiceId;
    }

    // ----- Settings -----

    public async Task<(string DefaultLabourSac, int ReminderLeadDays)> SettingsAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.ServiceView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var s = (await unit.ListAsync<ServiceSettings>(cancellationToken: cancellationToken)).SingleOrDefault()
            ?? throw new WorkspaceValidationException("Service settings are missing. Sign out and in again to finish the upgrade.");
        return (s.DefaultLabourSac, s.ReminderLeadDays);
    }

    public async Task SaveSettingsAsync(string defaultLabourSac, int reminderLeadDays, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.ServiceManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var s = (await unit.ListAsync<ServiceSettings>(cancellationToken: cancellationToken)).SingleOrDefault()
            ?? throw new WorkspaceValidationException("Service settings are missing. Sign out and in again to finish the upgrade.");
        Guard(() => s.Update(defaultLabourSac, reminderLeadDays));
        await unit.CommitAsync(cancellationToken);
    }

    // ----- Maintenance schedules -----

    public async Task<IReadOnlyList<MaintenanceRow>> SchedulesAsync(Guid? customerId = null, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.ServiceView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var customers = (await unit.ListAsync<Party>(p => p.Kind == PartyKind.Customer, cancellationToken)).ToDictionary(p => p.Id, p => p.Name);
        var users = await NamesAsync(unit, cancellationToken);
        return (await unit.ListAsync<MaintenanceSchedule>(m => customerId == null || m.CustomerId == customerId, cancellationToken)).OrderBy(m => m.NextDueDate)
            .Select(m => new MaintenanceRow(m.Id, m.CustomerId, customers.GetValueOrDefault(m.CustomerId, "?"), m.ItemDescription, m.SerialNumber, m.IntervalMonths, m.NextDueDate,
                m.AssigneeUserId, m.AssigneeUserId is { } a ? users.GetValueOrDefault(a, "?") : string.Empty, m.Notes, m.IsActive, m.ProductId))
            .ToList();
    }

    public async Task<Guid> SaveScheduleAsync(MaintenanceInput input, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.ServiceManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        await CustomerAsync(unit, input.CustomerId, cancellationToken);
        if (input.AssigneeUserId is { } a && await unit.FindAsync<User>(a, cancellationToken) is null) throw new WorkspaceValidationException("That person is not a user of this company.");
        MaintenanceSchedule schedule;
        if (input.Id is { } id)
        {
            schedule = await unit.FindAsync<MaintenanceSchedule>(id, cancellationToken) ?? throw new WorkspaceValidationException("That schedule no longer exists.");
            if (schedule.CustomerId != input.CustomerId) throw new WorkspaceValidationException("A schedule cannot move to another customer.");
        }
        else
        {
            schedule = new MaintenanceSchedule(Guid.NewGuid(), session.CompanyId, input.CustomerId);
            unit.Add(schedule);
        }

        Guard(() => schedule.Update(input.ProductId, input.ItemDescription, input.SerialNumber, input.IntervalMonths, input.NextDueDate, input.AssigneeUserId, input.Notes));
        await unit.CommitAsync(cancellationToken);
        return schedule.Id;
    }

    public async Task SetScheduleActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.ServiceManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var schedule = await unit.FindAsync<MaintenanceSchedule>(id, cancellationToken) ?? throw new WorkspaceValidationException("That schedule no longer exists.");
        schedule.SetActive(active);
        await unit.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Creates a follow-up task for every maintenance visit due within the company's reminder lead time of
    /// <paramref name="today"/>, once per schedule and due date, and moves each schedule's next due date on.
    /// </summary>
    public async Task<int> GenerateRemindersAsync(DateOnly today, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.ServiceManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var settings = (await unit.ListAsync<ServiceSettings>(cancellationToken: cancellationToken)).SingleOrDefault();
        var until = today.AddDays(settings?.ReminderLeadDays ?? 7);
        var created = 0;
        foreach (var schedule in await unit.ListAsync<MaintenanceSchedule>(m => m.IsActive && m.NextDueDate <= until, cancellationToken))
        {
            var scheduleId = schedule.Id;
            foreach (var due in schedule.TakeDueDates(until))
            {
                var dueDate = due;
                if (await unit.AnyAsync<FollowUp>(f => f.ScheduleId == scheduleId && f.DueDate == dueDate, cancellationToken)) continue;
                var task = new FollowUp(Guid.NewGuid(), session.CompanyId, clock.UtcNow, session.UserId);
                task.Update($"Maintenance due: {schedule.ItemDescription}" + (schedule.SerialNumber.Length > 0 ? $" ({schedule.SerialNumber})" : string.Empty),
                    schedule.Notes, due, schedule.AssigneeUserId, null, schedule.CustomerId, null, null);
                task.LinkSchedule(scheduleId);
                unit.Add(task);
                created++;
            }
        }

        if (created > 0) unit.Record(new AuditEvent("Generated", nameof(MaintenanceSchedule), string.Empty, $"Created {created} maintenance reminder(s) due by {until:dd-MM-yyyy}"));
        await unit.CommitAsync(cancellationToken);
        return created;
    }

    // ----- Internals -----

    private async Task<WarrantyInfo?> ApplyAsync(IUnitOfWork unit, ServiceJob job, ServiceJobInput input, CancellationToken cancellationToken)
    {
        var warehouse = await unit.FindAsync<Warehouse>(input.WarehouseId, cancellationToken) ?? throw new WorkspaceValidationException("Choose the warehouse parts are issued from.");
        if (!warehouse.IsActive) throw new WorkspaceValidationException($"Warehouse {warehouse.Name} is inactive.");
        if (input.ProductId is { } p && await unit.FindAsync<Product>(p, cancellationToken) is null) throw new WorkspaceValidationException("That product no longer exists.");
        if (input.TechnicianUserId is { } t && await unit.FindAsync<User>(t, cancellationToken) is null) throw new WorkspaceValidationException("That technician is not a user of this company.");
        var warranty = string.IsNullOrWhiteSpace(input.SerialNumber) ? null : await WarrantyAsync(unit, input.SerialNumber, cancellationToken);
        Guard(() => job.Update(input.ProductId ?? warranty?.ProductId, input.ItemDescription, input.SerialNumber, input.Fault, input.Accessories, input.Diagnosis,
            input.TechnicianUserId, input.EstimatedCompletion, warehouse.Id, warranty?.WarrantyUntil));
        return warranty;
    }

    private static async Task<WarrantyInfo?> WarrantyAsync(IUnitOfWork unit, string serialNumber, CancellationToken cancellationToken)
    {
        var serial = serialNumber.Trim().ToUpperInvariant();
        if (serial.Length == 0) return null;
        var lines = await unit.ListAsync<BillingLine>(l => l.SerialNumber == serial && l.OriginalLineId == null, cancellationToken);
        if (lines.Count == 0) return null;
        var documentIds = lines.Select(l => l.DocumentId).ToList();
        var invoices = (await unit.ListAsync<BillingDocument>(d => documentIds.Contains(d.Id) && d.Kind == BillingDocumentKind.SalesInvoice && d.Status == BillingStatus.Posted, cancellationToken))
            .ToDictionary(d => d.Id);
        var cancelled = (await unit.ListAsync<BillingCancellation>(c => documentIds.Contains(c.DocumentId), cancellationToken)).Select(c => c.DocumentId).ToHashSet();
        var sale = lines.Where(l => invoices.ContainsKey(l.DocumentId) && !cancelled.Contains(l.DocumentId)).OrderByDescending(l => invoices[l.DocumentId].Date).FirstOrDefault();
        if (sale is null) return null;
        var invoice = invoices[sale.DocumentId];
        var product = sale.ProductId is { } pid ? await unit.FindAsync<Product>(pid, cancellationToken) : null;
        var months = product?.WarrantyMonths ?? 0;
        return new WarrantyInfo(serial, sale.ProductId, invoice.Number, invoice.Date, months, months > 0 ? invoice.Date.AddMonths(months) : null);
    }

    /// <summary>The quantity in the product's base unit (the stock service validates it again when posting).</summary>
    private static async Task<decimal> BaseQuantityAsync(IUnitOfWork unit, Product product, decimal quantity, Guid? unitId, CancellationToken cancellationToken)
    {
        if (unitId is null || unitId == product.UnitId) return quantity;
        var productId = product.Id;
        var target = unitId.Value;
        var conversion = (await unit.ListAsync<UnitConversion>(c => c.ProductId == productId && c.UnitId == target, cancellationToken)).SingleOrDefault()
            ?? throw new WorkspaceValidationException($"{product.Sku} has no conversion for that unit.");
        return quantity * conversion.Factor;
    }

    private static async Task<List<ServicePart>> LivePartsAsync(IUnitOfWork unit, Guid jobId, CancellationToken cancellationToken)
    {
        var parts = await unit.ListAsync<ServicePart>(p => p.JobId == jobId, cancellationToken);
        var ids = parts.Select(p => p.Id).ToList();
        var returned = (await unit.ListAsync<ServicePartReturn>(r => ids.Contains(r.PartId), cancellationToken)).Select(r => r.PartId).ToHashSet();
        return parts.Where(p => !returned.Contains(p.Id)).OrderBy(p => p.IssuedUtc).ToList();
    }

    /// <summary>Parts that appear on a posted, uncancelled invoice line.</summary>
    private static async Task<HashSet<Guid>> BilledPartsAsync(IUnitOfWork unit, List<Guid> partIds, CancellationToken cancellationToken)
    {
        var lines = await unit.ListAsync<BillingLine>(l => l.SourceId != null && partIds.Contains(l.SourceId.Value) && l.OriginalLineId == null, cancellationToken);
        if (lines.Count == 0) return [];
        var docIds = lines.Select(l => l.DocumentId).ToList();
        var posted = (await unit.ListAsync<BillingDocument>(d => docIds.Contains(d.Id) && d.Status == BillingStatus.Posted, cancellationToken)).Select(d => d.Id).ToHashSet();
        var cancelled = (await unit.ListAsync<BillingCancellation>(c => docIds.Contains(c.DocumentId), cancellationToken)).Select(c => c.DocumentId).ToHashSet();
        return lines.Where(l => posted.Contains(l.DocumentId) && !cancelled.Contains(l.DocumentId)).Select(l => l.SourceId!.Value).ToHashSet();
    }

    /// <summary>The job's invoice unless it was deleted (draft removed) or cancelled.</summary>
    private static async Task<BillingDocument?> LiveInvoiceAsync(IUnitOfWork unit, ServiceJob job, CancellationToken cancellationToken)
    {
        if (job.InvoiceId is not { } id) return null;
        var invoice = await unit.FindAsync<BillingDocument>(id, cancellationToken);
        if (invoice is null || await unit.AnyAsync<BillingCancellation>(c => c.DocumentId == id, cancellationToken)) return null;
        return invoice;
    }

    private static async Task EnsureNotInvoicedAsync(IUnitOfWork unit, ServiceJob job, CancellationToken cancellationToken)
    {
        Guard(job.EnsureOpen);
        if (await LiveInvoiceAsync(unit, job, cancellationToken) is { } invoice)
            throw new WorkspaceValidationException($"Invoice {invoice.Number ?? "(draft)"} exists for this job. Delete the draft (or cancel the invoice) to change labour.");
    }

    private static async Task<bool> HasChargesAsync(IUnitOfWork unit, Guid jobId, CancellationToken cancellationToken) =>
        (await LivePartsAsync(unit, jobId, cancellationToken)).Count > 0 || await unit.AnyAsync<ServiceLabour>(l => l.JobId == jobId, cancellationToken);

    private static async Task<ServiceJob> JobAsync(IUnitOfWork unit, Guid id, CancellationToken cancellationToken)
    {
        var job = await unit.FindAsync<ServiceJob>(id, cancellationToken) ?? throw new WorkspaceValidationException("That job no longer exists.");
        Guard(job.EnsureOpen);
        return job;
    }

    private static async Task<Party> CustomerAsync(IUnitOfWork unit, Guid id, CancellationToken cancellationToken)
    {
        var customer = await unit.FindAsync<Party>(id, cancellationToken);
        if (customer is not { Kind: PartyKind.Customer }) throw new WorkspaceValidationException("Choose the customer.");
        return customer;
    }

    private static async Task<Dictionary<Guid, string>> NamesAsync(IUnitOfWork unit, CancellationToken cancellationToken) =>
        (await unit.ListAsync<User>(cancellationToken: cancellationToken)).ToDictionary(u => u.Id, u => u.DisplayName);

    private DateOnly Today() => DateOnly.FromDateTime(clock.UtcNow.ToLocalTime());

    private static void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (DomainException ex)
        {
            throw new WorkspaceValidationException(ex.Message);
        }
    }

    private static T Guard<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (DomainException ex)
        {
            throw new WorkspaceValidationException(ex.Message);
        }
    }
}
