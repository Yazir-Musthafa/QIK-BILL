using Nexora.Domain.Accounting;
using Nexora.Domain.Common;

namespace Nexora.Domain.Service;

public enum ServiceStatus
{
    Received,
    Diagnosed,
    AwaitingParts,
    InRepair,
    Ready,
    Delivered,
    Cancelled,
}

/// <summary>Who pays: the customer (an invoice is raised) or the warranty (parts cost goes to Warranty and Service Costs).</summary>
public enum ServiceCharge
{
    Chargeable,
    Warranty,
}

public static class ServiceDocumentTypes
{
    public const string Job = "service.job";

    public static IReadOnlyList<Numbering.DocumentTypeInfo> All { get; } = [new(Job, "Service job card", "JOB/{FY}/", 4)];
}

/// <summary>The service workflow. Every status change goes through <see cref="CanMove"/>.</summary>
public static class ServiceWorkflow
{
    private static readonly Dictionary<ServiceStatus, ServiceStatus[]> Allowed = new()
    {
        [ServiceStatus.Received] = [ServiceStatus.Diagnosed, ServiceStatus.Cancelled],
        [ServiceStatus.Diagnosed] = [ServiceStatus.AwaitingParts, ServiceStatus.InRepair, ServiceStatus.Cancelled],
        [ServiceStatus.AwaitingParts] = [ServiceStatus.InRepair, ServiceStatus.Cancelled],
        [ServiceStatus.InRepair] = [ServiceStatus.AwaitingParts, ServiceStatus.Ready, ServiceStatus.Cancelled],
        [ServiceStatus.Ready] = [ServiceStatus.Delivered, ServiceStatus.InRepair],
        [ServiceStatus.Delivered] = [],
        [ServiceStatus.Cancelled] = [],
    };

    public static IReadOnlyList<ServiceStatus> Next(ServiceStatus from) => Allowed[from];

    public static bool CanMove(ServiceStatus from, ServiceStatus to) => Allowed[from].Contains(to);

    public static string Label(this ServiceStatus status) => status switch
    {
        ServiceStatus.AwaitingParts => "Awaiting parts",
        ServiceStatus.InRepair => "In repair",
        _ => status.ToString(),
    };

    public static bool IsClosed(this ServiceStatus status) => status is ServiceStatus.Delivered or ServiceStatus.Cancelled;
}

/// <summary>
/// A service ticket and repair job card in one: the customer's item, the fault, warranty, technician, status,
/// parts issued from stock and labour. Status changes follow <see cref="ServiceWorkflow"/>.
/// </summary>
public sealed class ServiceJob : ICompanyOwned, IAudited
{
    private ServiceJob()
    {
        Number = ItemDescription = SerialNumber = Fault = Accessories = Diagnosis = CancelReason = string.Empty;
    }

    public ServiceJob(Guid id, Guid companyId, string number, DateOnly receivedOn, Guid customerId, DateTime createdUtc, Guid createdBy) : this()
    {
        Id = id;
        CompanyId = companyId;
        Number = number;
        ReceivedOn = receivedOn;
        CustomerId = customerId;
        Status = ServiceStatus.Received;
        CreatedUtc = DateTime.SpecifyKind(createdUtc, DateTimeKind.Utc);
        CreatedBy = createdBy;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Number { get; private set; }

    public DateOnly ReceivedOn { get; private set; }

    public Guid CustomerId { get; private set; }

    /// <summary>The catalogue product being serviced, when it is one.</summary>
    public Guid? ProductId { get; private set; }

    public string ItemDescription { get; private set; }

    public string SerialNumber { get; private set; }

    public string Fault { get; private set; }

    public string Accessories { get; private set; }

    public string Diagnosis { get; private set; }

    public ServiceCharge Charge { get; private set; }

    /// <summary>The date warranty ends, when the serial was sold by this company; null when unknown.</summary>
    public DateOnly? WarrantyUntil { get; private set; }

    public Guid? TechnicianUserId { get; private set; }

    public DateOnly? EstimatedCompletion { get; private set; }

    public Guid WarehouseId { get; private set; }

    public ServiceStatus Status { get; private set; }

    public string CancelReason { get; private set; }

    /// <summary>The sales invoice raised for the job (chargeable jobs).</summary>
    public Guid? InvoiceId { get; private set; }

    public DateOnly? DeliveredOn { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    public Guid CreatedBy { get; private set; }

    public void Update(Guid? productId, string itemDescription, string serialNumber, string fault, string accessories, string diagnosis,
        Guid? technicianUserId, DateOnly? estimatedCompletion, Guid warehouseId, DateOnly? warrantyUntil)
    {
        if (Status.IsClosed()) throw new DomainException($"Job {Number} is {Status.Label().ToLowerInvariant()} and cannot be changed.");
        var item = (itemDescription ?? string.Empty).Trim();
        if (item.Length is 0 or > 120) throw new DomainException("Describe the item (up to 120 characters).");
        var problem = (fault ?? string.Empty).Trim();
        if (problem.Length is 0 or > 1000) throw new DomainException("Describe the fault (up to 1000 characters).");
        var serial = (serialNumber ?? string.Empty).Trim().ToUpperInvariant();
        if (serial.Length > 60) throw new DomainException("The serial number can have up to 60 characters.");
        var extras = (accessories ?? string.Empty).Trim();
        var notes = (diagnosis ?? string.Empty).Trim();
        if (extras.Length > 300 || notes.Length > 2000) throw new DomainException("Accessories can have up to 300 characters and diagnosis up to 2000.");
        if (estimatedCompletion is { } eta && eta < ReceivedOn) throw new DomainException("The estimated completion cannot be before the job was received.");
        (ProductId, ItemDescription, SerialNumber, Fault, Accessories, Diagnosis) = (productId, item, serial, problem, extras, notes);
        (TechnicianUserId, EstimatedCompletion, WarehouseId, WarrantyUntil) = (technicianUserId, estimatedCompletion, warehouseId, warrantyUntil);
    }

    /// <summary>Changing who pays is allowed only while no parts have been issued (their cost was posted for the old choice).</summary>
    public void SetCharge(ServiceCharge charge, bool partsIssued)
    {
        if (Status.IsClosed()) throw new DomainException($"Job {Number} is closed.");
        if (charge != Charge && partsIssued) throw new DomainException("Parts have been issued for this job. Return them before changing between chargeable and warranty.");
        if (charge != Charge && InvoiceId is not null) throw new DomainException("An invoice has been raised for this job.");
        Charge = charge;
    }

    public void MoveTo(ServiceStatus status, string reason)
    {
        if (!ServiceWorkflow.CanMove(Status, status))
            throw new DomainException($"A job that is {Status.Label().ToLowerInvariant()} cannot move to {status.Label().ToLowerInvariant()}.");
        if (status == ServiceStatus.Cancelled)
        {
            var text = (reason ?? string.Empty).Trim();
            if (text.Length is 0 or > 300) throw new DomainException("Enter why the job is cancelled.");
            CancelReason = text;
        }

        Status = status;
    }

    public void MarkDelivered(DateOnly date)
    {
        MoveTo(ServiceStatus.Delivered, string.Empty);
        DeliveredOn = date;
    }

    public void LinkInvoice(Guid? invoiceId) => InvoiceId = invoiceId;

    public void EnsureOpen()
    {
        if (Status.IsClosed()) throw new DomainException($"Job {Number} is {Status.Label().ToLowerInvariant()}.");
    }
}

/// <summary>
/// A spare part issued from stock for a job. The issue is a stock document posted through the stock service; its cost
/// goes to Cost of Goods Sold (chargeable jobs, billed later without moving stock again) or to Warranty and Service
/// Costs (warranty jobs). A returned part is reversed with a return document at the same cost. Append-only.
/// </summary>
public sealed class ServicePart : ICompanyOwned
{
    private ServicePart()
    {
        Description = BatchNumber = string.Empty;
    }

    public ServicePart(Guid id, Guid companyId, Guid jobId, Guid productId, string description, decimal quantity, Guid warehouseId, Guid? batchId,
        string batchNumber, string? serialNumber, decimal cost, ServiceCharge charge, Guid stockDocumentId, Guid? voucherId, DateOnly date,
        DateTime issuedUtc, Guid issuedBy) : this()
    {
        if (quantity <= 0) throw new DomainException("Issue a quantity more than zero.");
        Money.EnsureValid(cost, "Cost");
        Id = id;
        CompanyId = companyId;
        JobId = jobId;
        ProductId = productId;
        Description = description;
        Quantity = quantity;
        WarehouseId = warehouseId;
        BatchId = batchId;
        BatchNumber = batchNumber;
        SerialNumber = serialNumber;
        Cost = cost;
        Charge = charge;
        StockDocumentId = stockDocumentId;
        VoucherId = voucherId;
        Date = date;
        IssuedUtc = DateTime.SpecifyKind(issuedUtc, DateTimeKind.Utc);
        IssuedBy = issuedBy;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid JobId { get; private set; }

    public Guid ProductId { get; private set; }

    public string Description { get; private set; }

    /// <summary>In the product's base unit.</summary>
    public decimal Quantity { get; private set; }

    public Guid WarehouseId { get; private set; }

    public Guid? BatchId { get; private set; }

    public string BatchNumber { get; private set; }

    public string? SerialNumber { get; private set; }

    /// <summary>Stock value of the issue (positive).</summary>
    public decimal Cost { get; private set; }

    /// <summary>Who paid for it when issued; decides the account its cost went to.</summary>
    public ServiceCharge Charge { get; private set; }

    public Guid StockDocumentId { get; private set; }

    public Guid? VoucherId { get; private set; }

    public DateOnly Date { get; private set; }

    public DateTime IssuedUtc { get; private set; }

    public Guid IssuedBy { get; private set; }
}

/// <summary>A part taken back into stock from a job (wrongly issued or not used). Append-only; one per part.</summary>
public sealed class ServicePartReturn : ICompanyOwned
{
    private ServicePartReturn()
    {
    }

    public ServicePartReturn(Guid id, Guid companyId, Guid partId, Guid stockDocumentId, Guid? voucherId, DateOnly date, DateTime returnedUtc, Guid returnedBy)
    {
        Id = id;
        CompanyId = companyId;
        PartId = partId;
        StockDocumentId = stockDocumentId;
        VoucherId = voucherId;
        Date = date;
        ReturnedUtc = DateTime.SpecifyKind(returnedUtc, DateTimeKind.Utc);
        ReturnedBy = returnedBy;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid PartId { get; private set; }

    public Guid StockDocumentId { get; private set; }

    public Guid? VoucherId { get; private set; }

    public DateOnly Date { get; private set; }

    public DateTime ReturnedUtc { get; private set; }

    public Guid ReturnedBy { get; private set; }
}

/// <summary>A labour or service charge on a job, billed as a service line. Editable until the job is invoiced.</summary>
public sealed class ServiceLabour : ICompanyOwned
{
    private ServiceLabour()
    {
        Description = SacCode = string.Empty;
    }

    public ServiceLabour(Guid id, Guid companyId, Guid jobId, string description, string sacCode, decimal quantity, decimal rate, Guid? taxCategoryId) : this()
    {
        var text = (description ?? string.Empty).Trim();
        if (text.Length is 0 or > 200) throw new DomainException("Describe the work (up to 200 characters).");
        if (quantity <= 0 || decimal.Round(quantity, 3) != quantity) throw new DomainException("Hours or quantity must be more than zero, with at most 3 decimals.");
        Money.EnsureValid(rate, "Rate");
        var sac = (sacCode ?? string.Empty).Trim();
        if (sac.Length > 8) throw new DomainException("The SAC code can have up to 8 characters.");
        Id = id;
        CompanyId = companyId;
        JobId = jobId;
        Description = text;
        SacCode = sac;
        Quantity = quantity;
        Rate = rate;
        TaxCategoryId = taxCategoryId;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid JobId { get; private set; }

    public string Description { get; private set; }

    public string SacCode { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal Rate { get; private set; }

    public Guid? TaxCategoryId { get; private set; }
}

/// <summary>A recurring service reminder for a customer's item (for example an AMC visit every 6 months).</summary>
public sealed class MaintenanceSchedule : ICompanyOwned, IAudited
{
    private MaintenanceSchedule()
    {
        ItemDescription = SerialNumber = Notes = string.Empty;
    }

    public MaintenanceSchedule(Guid id, Guid companyId, Guid customerId) : this()
    {
        Id = id;
        CompanyId = companyId;
        CustomerId = customerId;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid? ProductId { get; private set; }

    public string ItemDescription { get; private set; }

    public string SerialNumber { get; private set; }

    public int IntervalMonths { get; private set; }

    public DateOnly NextDueDate { get; private set; }

    public Guid? AssigneeUserId { get; private set; }

    public string Notes { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(Guid? productId, string itemDescription, string serialNumber, int intervalMonths, DateOnly nextDueDate, Guid? assigneeUserId, string notes)
    {
        var item = (itemDescription ?? string.Empty).Trim();
        if (item.Length is 0 or > 120) throw new DomainException("Describe the item (up to 120 characters).");
        if (intervalMonths is < 1 or > 60) throw new DomainException("The interval must be 1 to 60 months.");
        var serial = (serialNumber ?? string.Empty).Trim().ToUpperInvariant();
        var text = (notes ?? string.Empty).Trim();
        if (serial.Length > 60 || text.Length > 300) throw new DomainException("Serial number up to 60 characters, notes up to 300.");
        (ProductId, ItemDescription, SerialNumber, IntervalMonths, NextDueDate, AssigneeUserId, Notes) = (productId, item, serial, intervalMonths, nextDueDate, assigneeUserId, text);
    }

    public void SetActive(bool active) => IsActive = active;

    /// <summary>Due dates on or before <paramref name="until"/> (at most 24), advancing <see cref="NextDueDate"/>.</summary>
    public IReadOnlyList<DateOnly> TakeDueDates(DateOnly until)
    {
        var dates = new List<DateOnly>();
        while (IsActive && NextDueDate <= until && dates.Count < 24)
        {
            dates.Add(NextDueDate);
            NextDueDate = NextDueDate.AddMonths(IntervalMonths);
        }

        return dates;
    }
}

/// <summary>Service options for a company. Also the seed marker for job numbering on upgrade.</summary>
public sealed class ServiceSettings : ICompanyOwned, IAudited
{
    private ServiceSettings()
    {
        DefaultLabourSac = string.Empty;
    }

    public ServiceSettings(Guid id, Guid companyId)
    {
        Id = id;
        CompanyId = companyId;
        DefaultLabourSac = string.Empty;
        ReminderLeadDays = 7;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>SAC code suggested for labour lines. Empty by default: enter the code that applies to your services.</summary>
    public string DefaultLabourSac { get; private set; }

    /// <summary>How many days before a maintenance due date its follow-up task is created.</summary>
    public int ReminderLeadDays { get; private set; }

    public void Update(string defaultLabourSac, int reminderLeadDays)
    {
        var sac = (defaultLabourSac ?? string.Empty).Trim();
        if (sac.Length > 8) throw new DomainException("The SAC code can have up to 8 characters.");
        if (reminderLeadDays is < 0 or > 90) throw new DomainException("Reminder lead time must be 0 to 90 days.");
        (DefaultLabourSac, ReminderLeadDays) = (sac, reminderLeadDays);
    }
}
