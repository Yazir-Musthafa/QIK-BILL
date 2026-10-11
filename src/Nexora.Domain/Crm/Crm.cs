using System.Text.RegularExpressions;
using Nexora.Domain.Common;

namespace Nexora.Domain.Crm;

public enum LeadStatus
{
    New,
    Contacted,

    /// <summary>A qualified lead is a prospect.</summary>
    Qualified,
    Proposal,
    Converted,
    Lost,
}

public enum LeadSource
{
    WalkIn,
    Phone,
    Referral,
    Website,
    SocialMedia,
    Exhibition,
    Other,
}

public enum EnquiryStatus
{
    Open,
    Responded,
    Closed,
}

public enum ActivityKind
{
    Call,
    Meeting,
    Email,
    Message,
    Visit,
    Note,
}

public static class LeadStatusExtensions
{
    public static bool IsProspect(this LeadStatus status) => status is LeadStatus.Qualified or LeadStatus.Proposal;

    public static bool IsClosed(this LeadStatus status) => status is LeadStatus.Converted or LeadStatus.Lost;
}

/// <summary>
/// A lead or prospect: someone who may buy. Converting it creates (or links) a real customer and closes the lead;
/// a converted lead cannot be converted again.
/// </summary>
public sealed partial class Lead : ICompanyOwned, IAudited
{
    private Lead()
    {
        Name = Organisation = Phone = Email = Notes = LostReason = string.Empty;
    }

    public Lead(Guid id, Guid companyId, DateTime createdUtc, Guid createdBy) : this()
    {
        Id = id;
        CompanyId = companyId;
        Status = LeadStatus.New;
        CreatedUtc = DateTime.SpecifyKind(createdUtc, DateTimeKind.Utc);
        CreatedBy = createdBy;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Name { get; private set; }

    public string Organisation { get; private set; }

    public string Phone { get; private set; }

    public string Email { get; private set; }

    public LeadSource Source { get; private set; }

    public LeadStatus Status { get; private set; }

    /// <summary>The user responsible for the lead.</summary>
    public Guid? OwnerUserId { get; private set; }

    public string Notes { get; private set; }

    public string LostReason { get; private set; }

    public Guid? CustomerId { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    public Guid CreatedBy { get; private set; }

    public void Update(string name, string organisation, string phone, string email, LeadSource source, Guid? ownerUserId, string notes)
    {
        if (Status.IsClosed()) throw new DomainException("This lead is closed. Reopen it to change it.");
        var clean = (name ?? string.Empty).Trim();
        if (clean.Length is 0 or > 120) throw new DomainException("Enter a name of up to 120 characters.");
        var mail = (email ?? string.Empty).Trim();
        if (mail.Length > 120 || (mail.Length > 0 && !EmailPattern().IsMatch(mail))) throw new DomainException("Enter a valid email address.");
        Name = clean;
        Organisation = Text(organisation, 120, "Organisation");
        Phone = Text(phone, 20, "Phone");
        Email = mail;
        Source = source;
        OwnerUserId = ownerUserId;
        Notes = Text(notes, 1000, "Notes");
    }

    /// <summary>Moves an open lead to another open status (New, Contacted, Qualified, Proposal), or reopens a lost one.</summary>
    public void SetStatus(LeadStatus status)
    {
        if (Status == LeadStatus.Converted) throw new DomainException("This lead was converted to a customer; work with the customer instead.");
        if (status is LeadStatus.Converted) throw new DomainException("Use Convert to turn a lead into a customer.");
        if (status is LeadStatus.Lost) throw new DomainException("Use Mark lost and give the reason.");
        Status = status;
        LostReason = string.Empty;
    }

    public void MarkLost(string reason)
    {
        if (Status.IsClosed()) throw new DomainException("This lead is already closed.");
        var text = (reason ?? string.Empty).Trim();
        if (text.Length is 0 or > 300) throw new DomainException("Enter why the lead was lost (up to 300 characters).");
        Status = LeadStatus.Lost;
        LostReason = text;
    }

    public void MarkConverted(Guid customerId)
    {
        if (Status.IsClosed()) throw new DomainException(Status == LeadStatus.Converted ? "This lead has already been converted." : "Reopen this lost lead before converting it.");
        Status = LeadStatus.Converted;
        CustomerId = customerId;
    }

    private static string Text(string? value, int max, string what)
    {
        var clean = (value ?? string.Empty).Trim();
        if (clean.Length > max) throw new DomainException($"{what} can have up to {max} characters.");
        return clean;
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();
}

/// <summary>A person at a customer or supplier.</summary>
public sealed class Contact : ICompanyOwned, IAudited
{
    private Contact()
    {
        Name = Role = Phone = Email = string.Empty;
    }

    public Contact(Guid id, Guid companyId, Guid partyId) : this()
    {
        Id = id;
        CompanyId = companyId;
        PartyId = partyId;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid PartyId { get; private set; }

    public string Name { get; private set; }

    /// <summary>Designation, for example "Purchase manager".</summary>
    public string Role { get; private set; }

    public string Phone { get; private set; }

    public string Email { get; private set; }

    public bool IsPrimary { get; private set; }

    public void Update(string name, string role, string phone, string email, bool isPrimary)
    {
        var clean = (name ?? string.Empty).Trim();
        if (clean.Length is 0 or > 120) throw new DomainException("Enter a contact name of up to 120 characters.");
        var mail = (email ?? string.Empty).Trim();
        if (mail.Length > 120 || (mail.Length > 0 && !mail.Contains('@', StringComparison.Ordinal))) throw new DomainException("Enter a valid email address.");
        var r = (role ?? string.Empty).Trim();
        var p = (phone ?? string.Empty).Trim();
        if (r.Length > 60 || p.Length > 20) throw new DomainException("Role can have up to 60 characters and phone up to 20.");
        (Name, Role, Phone, Email, IsPrimary) = (clean, r, p, mail, isPrimary);
    }
}

/// <summary>Something a lead or customer asked about, for example a product or a price.</summary>
public sealed class Enquiry : ICompanyOwned, IAudited
{
    private Enquiry()
    {
        Subject = Details = string.Empty;
    }

    public Enquiry(Guid id, Guid companyId, DateOnly date, Guid? leadId, Guid? customerId, DateTime createdUtc, Guid createdBy) : this()
    {
        if ((leadId is null) == (customerId is null)) throw new DomainException("An enquiry belongs to either a lead or a customer.");
        Id = id;
        CompanyId = companyId;
        Date = date;
        LeadId = leadId;
        CustomerId = customerId;
        Status = EnquiryStatus.Open;
        CreatedUtc = DateTime.SpecifyKind(createdUtc, DateTimeKind.Utc);
        CreatedBy = createdBy;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public DateOnly Date { get; private set; }

    public Guid? LeadId { get; private set; }

    public Guid? CustomerId { get; private set; }

    public string Subject { get; private set; }

    public string Details { get; private set; }

    public Guid? ProductId { get; private set; }

    public ActivityKind Channel { get; private set; }

    public EnquiryStatus Status { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    public Guid CreatedBy { get; private set; }

    public void Update(string subject, string details, Guid? productId, ActivityKind channel, EnquiryStatus status)
    {
        var clean = (subject ?? string.Empty).Trim();
        if (clean.Length is 0 or > 120) throw new DomainException("Enter the subject (up to 120 characters).");
        var text = (details ?? string.Empty).Trim();
        if (text.Length > 1000) throw new DomainException("Details can have up to 1000 characters.");
        (Subject, Details, ProductId, Channel, Status) = (clean, text, productId, channel, status);
    }

    /// <summary>When a lead is converted, its enquiries move to the new customer.</summary>
    public void MoveToCustomer(Guid customerId)
    {
        LeadId = null;
        CustomerId = customerId;
    }
}

/// <summary>
/// A follow-up task or reminder with a due date and an assignee. Overdue means not done and due before today.
/// Linked to at most one lead, customer, enquiry, service job or maintenance schedule.
/// </summary>
public sealed class FollowUp : ICompanyOwned, IAudited
{
    private FollowUp()
    {
        Title = Notes = string.Empty;
    }

    public FollowUp(Guid id, Guid companyId, DateTime createdUtc, Guid createdBy) : this()
    {
        Id = id;
        CompanyId = companyId;
        CreatedUtc = DateTime.SpecifyKind(createdUtc, DateTimeKind.Utc);
        CreatedBy = createdBy;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Title { get; private set; }

    public string Notes { get; private set; }

    public DateOnly DueDate { get; private set; }

    public Guid? AssigneeUserId { get; private set; }

    public Guid? LeadId { get; private set; }

    public Guid? CustomerId { get; private set; }

    public Guid? EnquiryId { get; private set; }

    public Guid? ServiceJobId { get; private set; }

    /// <summary>Set when a maintenance schedule created the task (one task per schedule and due date).</summary>
    public Guid? ScheduleId { get; private set; }

    public bool IsDone { get; private set; }

    public DateTime? DoneUtc { get; private set; }

    public Guid? DoneBy { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    public Guid CreatedBy { get; private set; }

    public bool IsOverdue(DateOnly today) => !IsDone && DueDate < today;

    public void Update(string title, string notes, DateOnly dueDate, Guid? assigneeUserId, Guid? leadId, Guid? customerId, Guid? enquiryId, Guid? serviceJobId)
    {
        var clean = (title ?? string.Empty).Trim();
        if (clean.Length is 0 or > 120) throw new DomainException("Enter what needs doing (up to 120 characters).");
        var text = (notes ?? string.Empty).Trim();
        if (text.Length > 1000) throw new DomainException("Notes can have up to 1000 characters.");
        if (new[] { leadId, customerId, enquiryId, serviceJobId }.Count(x => x is not null) > 1) throw new DomainException("Link a follow-up to one lead, customer, enquiry or service job.");
        (Title, Notes, DueDate, AssigneeUserId) = (clean, text, dueDate, assigneeUserId);
        (LeadId, CustomerId, EnquiryId, ServiceJobId) = (leadId, customerId, enquiryId, serviceJobId);
    }

    public void LinkSchedule(Guid scheduleId) => ScheduleId = scheduleId;

    public void SetDone(bool done, DateTime nowUtc, Guid userId)
    {
        IsDone = done;
        DoneUtc = done ? DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc) : null;
        DoneBy = done ? userId : null;
    }

    public void MoveToCustomer(Guid customerId)
    {
        LeadId = null;
        CustomerId = customerId;
    }
}

/// <summary>A call note or other contact with a lead or customer, for the activity timeline. Append-only.</summary>
public sealed class Activity : ICompanyOwned
{
    private Activity()
    {
        Summary = string.Empty;
    }

    public Activity(Guid id, Guid companyId, Guid? leadId, Guid? customerId, ActivityKind kind, DateTime occurredUtc, string summary, Guid recordedBy,
        DateTime recordedUtc) : this()
    {
        if ((leadId is null) == (customerId is null)) throw new DomainException("An activity belongs to either a lead or a customer.");
        var text = (summary ?? string.Empty).Trim();
        if (text.Length is 0 or > 2000) throw new DomainException("Write what was discussed (up to 2000 characters).");
        Id = id;
        CompanyId = companyId;
        LeadId = leadId;
        CustomerId = customerId;
        Kind = kind;
        OccurredUtc = DateTime.SpecifyKind(occurredUtc, DateTimeKind.Utc);
        Summary = text;
        RecordedBy = recordedBy;
        RecordedUtc = DateTime.SpecifyKind(recordedUtc, DateTimeKind.Utc);
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>The lead the activity was recorded on. Kept after conversion; <see cref="CustomerId"/> is the customer.</summary>
    public Guid? LeadId { get; private set; }

    public Guid? CustomerId { get; private set; }

    public ActivityKind Kind { get; private set; }

    public DateTime OccurredUtc { get; private set; }

    public string Summary { get; private set; }

    public Guid RecordedBy { get; private set; }

    public DateTime RecordedUtc { get; private set; }
}
