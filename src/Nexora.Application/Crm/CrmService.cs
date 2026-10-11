using Nexora.Application.Abstractions;
using Nexora.Application.Billing;
using Nexora.Application.Security;
using Nexora.Domain.Audit;
using Nexora.Domain.Billing;
using Nexora.Domain.Common;
using Nexora.Domain.Crm;
using Nexora.Domain.Inventory;
using Nexora.Domain.Security;
using Nexora.Domain.Service;
using Nexora.Domain.Users;

namespace Nexora.Application.Crm;

public sealed record MemberRow(Guid UserId, string DisplayName);

public sealed record LeadInput(Guid? Id, string Name, string Organisation = "", string Phone = "", string Email = "", LeadSource Source = LeadSource.WalkIn,
    Guid? OwnerUserId = null, string Notes = "");

public sealed record LeadRow(Guid Id, string Name, string Organisation, string Phone, string Email, LeadSource Source, LeadStatus Status, Guid? OwnerUserId,
    string Owner, string Notes, string LostReason, Guid? CustomerId, string Customer, DateTime CreatedUtc, int OpenFollowUps)
{
    public bool IsProspect => Status.IsProspect();
}

/// <summary>Customers that look like the lead (same name, phone or email). Conversion stops here so no duplicate is created.</summary>
public sealed class PossibleDuplicateException(string message, IReadOnlyList<PartyRow> candidates) : WorkspaceValidationException(message)
{
    public IReadOnlyList<PartyRow> Candidates { get; } = candidates;
}

public sealed record ContactInput(Guid? Id, Guid PartyId, string Name, string Role = "", string Phone = "", string Email = "", bool IsPrimary = false);

public sealed record ContactRow(Guid Id, Guid PartyId, string Name, string Role, string Phone, string Email, bool IsPrimary);

public sealed record EnquiryInput(Guid? Id, DateOnly Date, Guid? LeadId, Guid? CustomerId, string Subject, string Details = "", Guid? ProductId = null,
    ActivityKind Channel = ActivityKind.Call, EnquiryStatus Status = EnquiryStatus.Open);

public sealed record EnquiryRow(Guid Id, DateOnly Date, Guid? LeadId, Guid? CustomerId, string For, string Subject, string Details, Guid? ProductId,
    string Product, ActivityKind Channel, EnquiryStatus Status);

public sealed record FollowUpInput(Guid? Id, string Title, DateOnly DueDate, Guid? AssigneeUserId = null, string Notes = "", Guid? LeadId = null,
    Guid? CustomerId = null, Guid? EnquiryId = null, Guid? ServiceJobId = null);

public sealed record FollowUpRow(Guid Id, string Title, string Notes, DateOnly DueDate, Guid? AssigneeUserId, string Assignee, bool IsDone, bool IsOverdue,
    Guid? LeadId, Guid? CustomerId, Guid? EnquiryId, Guid? ServiceJobId, string Regarding)
{
    public string State => IsDone ? "Done" : IsOverdue ? "Overdue" : "Open";
}

public sealed record FollowUpFilter(bool MineOnly = false, bool IncludeDone = false, DateOnly? DueOnOrBefore = null, Guid? LeadId = null, Guid? CustomerId = null,
    Guid? ServiceJobId = null);

/// <summary>Counts for the dashboard and the notifications panel: follow-ups assigned to the user (or unassigned ones they created).</summary>
public sealed record FollowUpSummary(int Overdue, int DueToday, IReadOnlyList<FollowUpRow> Items);

/// <summary>One entry of a lead's or customer's timeline, newest first.</summary>
public sealed record TimelineEntry(DateTime WhenUtc, string Kind, string Title, string Detail, string By);

public sealed record CustomerOverview(PartyRow Customer, decimal? CreditAvailable, bool CanSeeSales, IReadOnlyList<BillingDocumentRow> Invoices,
    IReadOnlyList<SettlementRow> Payments, bool CanSeeService, IReadOnlyList<ServiceHistoryRow> ServiceJobs, IReadOnlyList<ContactRow> Contacts,
    IReadOnlyList<EnquiryRow> Enquiries, IReadOnlyList<FollowUpRow> FollowUps, IReadOnlyList<TimelineEntry> Timeline, IReadOnlyList<CustomerPriceRow> Prices);

/// <summary>A service job in a history list (per customer or per serial number).</summary>
public sealed record ServiceHistoryRow(Guid Id, string Number, DateOnly ReceivedOn, string Customer, string Item, string SerialNumber, string Fault, ServiceStatus Status,
    ServiceCharge Charge, DateOnly? DeliveredOn)
{
    public string StatusLabel => Status.Label();
}

public sealed record CustomerPriceRow(Guid Id, Guid ProductId, string Sku, string Product, decimal Price, decimal SellingPrice);

/// <summary>
/// CRM: leads and prospects, conversion to customers, contacts, enquiries, follow-up tasks, call notes and the activity
/// timeline, the customer view (read from billing and service, never copied) and customer-specific prices.
/// crm.view to read, crm.manage to change; converting a lead also needs sales.create; customer prices need sales.price-override.
/// </summary>
public sealed class CrmService(IUnitOfWorkFactory units, IAuthorizer authorizer, PartyService parties, BillingService billing, SettlementService settlements, IClock clock)
{
    /// <summary>Users of the company, for owner and assignee choices.</summary>
    public async Task<IReadOnlyList<MemberRow>> MembersAsync(CancellationToken cancellationToken = default)
    {
        authorizer.RequireSession();
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        return (await unit.ListAsync<User>(u => u.IsActive, cancellationToken)).OrderBy(u => u.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(u => new MemberRow(u.Id, u.DisplayName)).ToList();
    }

    // ----- Leads -----

    public async Task<IReadOnlyList<LeadRow>> LeadsAsync(bool includeClosed = false, string? search = null, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.CrmView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var leads = await unit.ListAsync<Lead>(l => includeClosed || (l.Status != LeadStatus.Converted && l.Status != LeadStatus.Lost), cancellationToken);
        var names = await NamesAsync(unit, cancellationToken);
        var customers = (await unit.ListAsync<Party>(p => p.Kind == PartyKind.Customer, cancellationToken)).ToDictionary(p => p.Id, p => p.Name);
        var open = (await unit.ListAsync<FollowUp>(f => !f.IsDone && f.LeadId != null, cancellationToken)).GroupBy(f => f.LeadId!.Value).ToDictionary(g => g.Key, g => g.Count());
        var text = (search ?? string.Empty).Trim();
        return leads.Where(l => text.Length == 0 || l.Name.Contains(text, StringComparison.OrdinalIgnoreCase) || l.Organisation.Contains(text, StringComparison.OrdinalIgnoreCase)
                || l.Phone.Contains(text, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(l => l.CreatedUtc)
            .Select(l => new LeadRow(l.Id, l.Name, l.Organisation, l.Phone, l.Email, l.Source, l.Status, l.OwnerUserId, Name(names, l.OwnerUserId), l.Notes,
                l.LostReason, l.CustomerId, l.CustomerId is { } c ? customers.GetValueOrDefault(c, "?") : string.Empty, l.CreatedUtc, open.GetValueOrDefault(l.Id)))
            .ToList();
    }

    public async Task<Guid> SaveLeadAsync(LeadInput input, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.CrmManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        Lead lead;
        if (input.Id is { } id)
        {
            lead = await unit.FindAsync<Lead>(id, cancellationToken) ?? throw new WorkspaceValidationException("That lead no longer exists.");
        }
        else
        {
            lead = new Lead(Guid.NewGuid(), session.CompanyId, clock.UtcNow, session.UserId);
            unit.Add(lead);
        }

        await EnsureMemberAsync(unit, input.OwnerUserId, cancellationToken);
        Guard(() => lead.Update(input.Name, input.Organisation, input.Phone, input.Email, input.Source, input.OwnerUserId ?? (input.Id is null ? session.UserId : null), input.Notes));
        await unit.CommitAsync(cancellationToken);
        return lead.Id;
    }

    public async Task SetLeadStatusAsync(Guid id, LeadStatus status, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.CrmManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var lead = await unit.FindAsync<Lead>(id, cancellationToken) ?? throw new WorkspaceValidationException("That lead no longer exists.");
        Guard(() => lead.SetStatus(status));
        await unit.CommitAsync(cancellationToken);
    }

    public async Task MarkLeadLostAsync(Guid id, string reason, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.CrmManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var lead = await unit.FindAsync<Lead>(id, cancellationToken) ?? throw new WorkspaceValidationException("That lead no longer exists.");
        Guard(() => lead.MarkLost(reason));
        await unit.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Converts a lead into a customer in one transaction: creates the customer (or links <paramref name="linkToCustomerId"/>),
    /// closes the lead, and moves its enquiries and open follow-ups to the customer. When a customer with the same name,
    /// phone or email exists and no link is given, nothing is created and <see cref="PossibleDuplicateException"/> lists them.
    /// </summary>
    public async Task<Guid> ConvertLeadAsync(Guid leadId, Guid? linkToCustomerId = null, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.CrmManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var lead = await unit.FindAsync<Lead>(leadId, cancellationToken) ?? throw new WorkspaceValidationException("That lead no longer exists.");
        if (lead.Status == LeadStatus.Converted) throw new WorkspaceValidationException($"{lead.Name} has already been converted.");
        Guid customerId;
        if (linkToCustomerId is { } link)
        {
            var existing = await unit.FindAsync<Party>(link, cancellationToken);
            if (existing is not { Kind: PartyKind.Customer }) throw new WorkspaceValidationException("Choose an existing customer to link the lead to.");
            customerId = existing.Id;
        }
        else
        {
            authorizer.Demand(Permissions.SalesCreate);
            var name = lead.Name;
            var phone = lead.Phone;
            var email = lead.Email;
            var candidates = (await unit.ListAsync<Party>(p => p.Kind == PartyKind.Customer, cancellationToken))
                .Where(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)
                    || (phone.Length > 0 && Digits(p.Phone) == Digits(phone) && Digits(phone).Length >= 6)
                    || (email.Length > 0 && string.Equals(p.Email, email, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (candidates.Count > 0)
            {
                var rows = new List<PartyRow>();
                foreach (var c in candidates) rows.Add(await parties.GetAsync(c.Id, cancellationToken));
                throw new PossibleDuplicateException(
                    $"A customer that looks like {lead.Name} already exists: {string.Join(", ", candidates.Select(c => c.Name))}. Link the lead to that customer instead of creating another.",
                    rows);
            }

            var customer = await parties.CreateCoreAsync(unit, session, PartyKind.Customer, new PartyDetails(lead.Name, lead.Phone, lead.Email), cancellationToken);
            customerId = customer.Id;
        }

        Guard(() => lead.MarkConverted(customerId));
        foreach (var enquiry in await unit.ListAsync<Enquiry>(e => e.LeadId == leadId, cancellationToken)) enquiry.MoveToCustomer(customerId);
        foreach (var task in await unit.ListAsync<FollowUp>(f => f.LeadId == leadId, cancellationToken)) task.MoveToCustomer(customerId);
        unit.Record(new AuditEvent("Converted", nameof(Lead), leadId.ToString(), $"Converted lead {lead.Name} to customer" + (linkToCustomerId is null ? " (new)" : " (existing)")));
        await unit.CommitAsync(cancellationToken);
        return customerId;
    }

    // ----- Contacts -----

    public async Task<IReadOnlyList<ContactRow>> ContactsAsync(Guid partyId, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.CrmView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        return (await unit.ListAsync<Contact>(c => c.PartyId == partyId, cancellationToken)).OrderByDescending(c => c.IsPrimary).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(c => new ContactRow(c.Id, c.PartyId, c.Name, c.Role, c.Phone, c.Email, c.IsPrimary)).ToList();
    }

    public async Task<Guid> SaveContactAsync(ContactInput input, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.CrmManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        if (await unit.FindAsync<Party>(input.PartyId, cancellationToken) is null) throw new WorkspaceValidationException("That customer or supplier no longer exists.");
        Contact contact;
        if (input.Id is { } id)
        {
            contact = await unit.FindAsync<Contact>(id, cancellationToken) ?? throw new WorkspaceValidationException("That contact no longer exists.");
            if (contact.PartyId != input.PartyId) throw new WorkspaceValidationException("A contact cannot move to another party.");
        }
        else
        {
            contact = new Contact(Guid.NewGuid(), session.CompanyId, input.PartyId);
            unit.Add(contact);
        }

        Guard(() => contact.Update(input.Name, input.Role, input.Phone, input.Email, input.IsPrimary));
        if (input.IsPrimary)
        {
            // One primary contact per party.
            var partyId = input.PartyId;
            foreach (var other in await unit.ListAsync<Contact>(c => c.PartyId == partyId && c.IsPrimary && c.Id != contact.Id, cancellationToken))
                other.Update(other.Name, other.Role, other.Phone, other.Email, false);
        }

        await unit.CommitAsync(cancellationToken);
        return contact.Id;
    }

    public async Task DeleteContactAsync(Guid id, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.CrmManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var contact = await unit.FindAsync<Contact>(id, cancellationToken) ?? throw new WorkspaceValidationException("That contact no longer exists.");
        unit.Remove(contact);
        await unit.CommitAsync(cancellationToken);
    }

    // ----- Enquiries -----

    public async Task<IReadOnlyList<EnquiryRow>> EnquiriesAsync(Guid? leadId = null, Guid? customerId = null, bool openOnly = false, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.CrmView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var enquiries = await unit.ListAsync<Enquiry>(e => (leadId == null || e.LeadId == leadId) && (customerId == null || e.CustomerId == customerId)
            && (!openOnly || e.Status != EnquiryStatus.Closed), cancellationToken);
        return await EnquiryRowsAsync(unit, enquiries, cancellationToken);
    }

    public async Task<Guid> SaveEnquiryAsync(EnquiryInput input, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.CrmManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        if (input.LeadId is { } leadId && await unit.FindAsync<Lead>(leadId, cancellationToken) is null) throw new WorkspaceValidationException("That lead no longer exists.");
        if (input.CustomerId is { } customerId && await unit.FindAsync<Party>(customerId, cancellationToken) is not { Kind: PartyKind.Customer })
            throw new WorkspaceValidationException("Choose a customer.");
        if (input.ProductId is { } productId && await unit.FindAsync<Product>(productId, cancellationToken) is null) throw new WorkspaceValidationException("That product no longer exists.");
        Enquiry enquiry;
        if (input.Id is { } id)
        {
            enquiry = await unit.FindAsync<Enquiry>(id, cancellationToken) ?? throw new WorkspaceValidationException("That enquiry no longer exists.");
        }
        else
        {
            enquiry = Guard(() => new Enquiry(Guid.NewGuid(), session.CompanyId, input.Date, input.LeadId, input.CustomerId, clock.UtcNow, session.UserId));
            unit.Add(enquiry);
        }

        Guard(() => enquiry.Update(input.Subject, input.Details, input.ProductId, input.Channel, input.Status));
        await unit.CommitAsync(cancellationToken);
        return enquiry.Id;
    }

    // ----- Follow-ups -----

    public async Task<IReadOnlyList<FollowUpRow>> FollowUpsAsync(FollowUpFilter filter, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.CrmView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var until = filter.DueOnOrBefore ?? DateOnly.MaxValue;
        var me = session.UserId;
        var tasks = await unit.ListAsync<FollowUp>(f => (filter.IncludeDone || !f.IsDone) && f.DueDate <= until
            && (filter.LeadId == null || f.LeadId == filter.LeadId) && (filter.CustomerId == null || f.CustomerId == filter.CustomerId)
            && (filter.ServiceJobId == null || f.ServiceJobId == filter.ServiceJobId)
            && (!filter.MineOnly || f.AssigneeUserId == me || (f.AssigneeUserId == null && f.CreatedBy == me)), cancellationToken);
        return await FollowUpRowsAsync(unit, tasks, cancellationToken);
    }

    /// <summary>Overdue and due-today follow-ups of the signed-in user, for the dashboard and notifications. Empty without crm.view.</summary>
    public async Task<FollowUpSummary> MySummaryAsync(CancellationToken cancellationToken = default)
    {
        if (!authorizer.Has(Permissions.CrmView)) return new FollowUpSummary(0, 0, []);
        var today = Today();
        var rows = await FollowUpsAsync(new FollowUpFilter(MineOnly: true, DueOnOrBefore: today), cancellationToken);
        return new FollowUpSummary(rows.Count(r => r.IsOverdue), rows.Count(r => r.DueDate == today), rows.OrderBy(r => r.DueDate).Take(10).ToList());
    }

    public async Task<Guid> SaveFollowUpAsync(FollowUpInput input, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.CrmManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        await EnsureMemberAsync(unit, input.AssigneeUserId, cancellationToken);
        if (input.LeadId is { } l && await unit.FindAsync<Lead>(l, cancellationToken) is null) throw new WorkspaceValidationException("That lead no longer exists.");
        if (input.CustomerId is { } c && await unit.FindAsync<Party>(c, cancellationToken) is null) throw new WorkspaceValidationException("That customer no longer exists.");
        if (input.EnquiryId is { } e && await unit.FindAsync<Enquiry>(e, cancellationToken) is null) throw new WorkspaceValidationException("That enquiry no longer exists.");
        if (input.ServiceJobId is { } j && await unit.FindAsync<ServiceJob>(j, cancellationToken) is null) throw new WorkspaceValidationException("That service job no longer exists.");
        FollowUp task;
        if (input.Id is { } id)
        {
            task = await unit.FindAsync<FollowUp>(id, cancellationToken) ?? throw new WorkspaceValidationException("That follow-up no longer exists.");
        }
        else
        {
            task = new FollowUp(Guid.NewGuid(), session.CompanyId, clock.UtcNow, session.UserId);
            unit.Add(task);
        }

        Guard(() => task.Update(input.Title, input.Notes, input.DueDate, input.AssigneeUserId, input.LeadId, input.CustomerId, input.EnquiryId, input.ServiceJobId));
        await unit.CommitAsync(cancellationToken);
        return task.Id;
    }

    /// <summary>The assignee may tick off their own follow-up; otherwise crm.manage is needed.</summary>
    public async Task SetFollowUpDoneAsync(Guid id, bool done, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.CrmView);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var task = await unit.FindAsync<FollowUp>(id, cancellationToken) ?? throw new WorkspaceValidationException("That follow-up no longer exists.");
        if (task.AssigneeUserId != session.UserId) authorizer.Demand(Permissions.CrmManage);
        task.SetDone(done, clock.UtcNow, session.UserId);
        await unit.CommitAsync(cancellationToken);
    }

    // ----- Activities and timeline -----

    public async Task<Guid> AddActivityAsync(Guid? leadId, Guid? customerId, ActivityKind kind, DateTime occurredUtc, string summary, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.CrmManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        if (leadId is { } l && await unit.FindAsync<Lead>(l, cancellationToken) is null) throw new WorkspaceValidationException("That lead no longer exists.");
        if (customerId is { } c && await unit.FindAsync<Party>(c, cancellationToken) is null) throw new WorkspaceValidationException("That customer no longer exists.");
        if (occurredUtc > clock.UtcNow.AddMinutes(5)) throw new WorkspaceValidationException("An activity cannot be in the future. Use a follow-up for planned contact.");
        var activity = Guard(() => new Activity(Guid.NewGuid(), session.CompanyId, leadId, customerId, kind, occurredUtc, summary, session.UserId, clock.UtcNow));
        unit.Add(activity);
        await unit.CommitAsync(cancellationToken);
        return activity.Id;
    }

    /// <summary>
    /// The timeline of a lead or customer, newest first: activities, enquiries, follow-ups, and for customers the
    /// activities recorded while it was a lead, its sales documents, receipts and service jobs (where the user may see them).
    /// </summary>
    public async Task<IReadOnlyList<TimelineEntry>> TimelineAsync(Guid? leadId, Guid? customerId, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.CrmView);
        if ((leadId is null) == (customerId is null)) throw new WorkspaceValidationException("Choose a lead or a customer.");
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var names = await NamesAsync(unit, cancellationToken);
        var leadIds = leadId is { } lid ? [lid] : (await unit.ListAsync<Lead>(l => l.CustomerId == customerId, cancellationToken)).Select(l => l.Id).ToList();
        var entries = new List<TimelineEntry>();
        foreach (var a in await unit.ListAsync<Activity>(a => (customerId != null && a.CustomerId == customerId) || (a.LeadId != null && leadIds.Contains(a.LeadId.Value)), cancellationToken))
            entries.Add(new TimelineEntry(a.OccurredUtc, a.Kind.ToString(), a.Kind == ActivityKind.Note ? "Note" : a.Kind.ToString(), a.Summary, Name(names, a.RecordedBy)));
        foreach (var e in await unit.ListAsync<Enquiry>(e => (customerId != null && e.CustomerId == customerId) || (e.LeadId != null && leadIds.Contains(e.LeadId.Value)), cancellationToken))
            entries.Add(new TimelineEntry(e.CreatedUtc, "Enquiry", e.Subject, $"{e.Status} · {e.Details}", Name(names, e.CreatedBy)));
        foreach (var f in await unit.ListAsync<FollowUp>(f => (customerId != null && f.CustomerId == customerId) || (f.LeadId != null && leadIds.Contains(f.LeadId.Value)), cancellationToken))
            entries.Add(new TimelineEntry(f.DoneUtc ?? f.CreatedUtc, "Follow-up", f.Title, f.IsDone ? "Done" : $"Due {f.DueDate:dd-MM-yyyy}", Name(names, f.AssigneeUserId ?? f.CreatedBy)));
        if (customerId is { } cid)
        {
            if (authorizer.Has(Permissions.SalesView))
            {
                foreach (var d in await billing.ListAsync(new BillingListFilter([BillingDocumentKind.SalesInvoice, BillingDocumentKind.SalesReturn], PartyId: cid), cancellationToken))
                    entries.Add(new TimelineEntry(d.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), d.KindLabel, d.Number, $"{d.Total:N2} · {d.StatusLabel}", string.Empty));
                foreach (var s in await settlements.ListAsync(PartyKind.Customer, partyId: cid, cancellationToken: cancellationToken))
                    entries.Add(new TimelineEntry(s.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), s.KindLabel, s.Number, $"{s.Amount:N2}" + (s.IsReversed ? " · reversed" : string.Empty), string.Empty));
            }

            if (authorizer.Has(Permissions.ServiceView))
                foreach (var j in await unit.ListAsync<ServiceJob>(j => j.CustomerId == cid, cancellationToken))
                    entries.Add(new TimelineEntry(j.ReceivedOn.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), "Service job", j.Number, $"{j.ItemDescription} · {j.Status.Label()}", string.Empty));
        }

        return entries.OrderByDescending(e => e.WhenUtc).ToList();
    }

    /// <summary>Everything about a customer in one place. Billing and service figures are read from their own records.</summary>
    public async Task<CustomerOverview> CustomerAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.CrmView);
        PartyRow customer;
        await using (var unit = await units.BeginAsync(readOnly: true, cancellationToken))
        {
            var party = await unit.FindAsync<Party>(customerId, cancellationToken);
            if (party is not { Kind: PartyKind.Customer }) throw new WorkspaceValidationException("That customer no longer exists.");
        }

        var canSeeSales = authorizer.Has(Permissions.SalesView);
        customer = canSeeSales ? await parties.GetAsync(customerId, cancellationToken) : await LimitedPartyAsync(customerId, cancellationToken);
        var invoices = canSeeSales
            ? await billing.ListAsync(new BillingListFilter([BillingDocumentKind.SalesInvoice, BillingDocumentKind.SalesReturn], PartyId: customerId), cancellationToken)
            : [];
        var payments = canSeeSales ? await settlements.ListAsync(PartyKind.Customer, partyId: customerId, cancellationToken: cancellationToken) : [];
        var canSeeService = authorizer.Has(Permissions.ServiceView);
        IReadOnlyList<ServiceHistoryRow> jobs = [];
        if (canSeeService)
        {
            await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
            jobs = (await unit.ListAsync<ServiceJob>(j => j.CustomerId == customerId, cancellationToken)).OrderByDescending(j => j.ReceivedOn)
                .Select(j => new ServiceHistoryRow(j.Id, j.Number, j.ReceivedOn, customer.Name, j.ItemDescription, j.SerialNumber, j.Fault, j.Status, j.Charge, j.DeliveredOn)).ToList();
        }

        var credit = canSeeSales && customer.CreditLimit > 0 ? customer.CreditLimit - customer.LedgerBalance : (decimal?)null;
        return new CustomerOverview(customer, credit, canSeeSales, invoices, payments, canSeeService, jobs, await ContactsAsync(customerId, cancellationToken),
            await EnquiriesAsync(customerId: customerId, cancellationToken: cancellationToken),
            await FollowUpsAsync(new FollowUpFilter(IncludeDone: true, CustomerId: customerId), cancellationToken),
            await TimelineAsync(null, customerId, cancellationToken), await CustomerPricesAsync(customerId, cancellationToken));
    }

    // ----- Customer prices -----

    public async Task<IReadOnlyList<CustomerPriceRow>> CustomerPricesAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        if (!authorizer.Has(Permissions.CrmView)) authorizer.Demand(Permissions.SalesView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var prices = await unit.ListAsync<CustomerPrice>(p => p.PartyId == customerId, cancellationToken);
        var ids = prices.Select(p => p.ProductId).ToList();
        var products = (await unit.ListAsync<Product>(p => ids.Contains(p.Id), cancellationToken)).ToDictionary(p => p.Id);
        return prices.Where(p => products.ContainsKey(p.ProductId)).OrderBy(p => products[p.ProductId].Sku, StringComparer.Ordinal)
            .Select(p => new CustomerPriceRow(p.Id, p.ProductId, products[p.ProductId].Sku, products[p.ProductId].DisplayName, p.Price, products[p.ProductId].SellingPrice)).ToList();
    }

    /// <summary>Sets a customer's own price for a product (per base unit). Changing prices needs sales.price-override.</summary>
    public async Task SetCustomerPriceAsync(Guid customerId, Guid productId, decimal price, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.SalesPriceOverride);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        if (await unit.FindAsync<Party>(customerId, cancellationToken) is not { Kind: PartyKind.Customer }) throw new WorkspaceValidationException("Choose a customer.");
        var product = await unit.FindAsync<Product>(productId, cancellationToken) ?? throw new WorkspaceValidationException("That product no longer exists.");
        var existing = (await unit.ListAsync<CustomerPrice>(p => p.PartyId == customerId && p.ProductId == productId, cancellationToken)).SingleOrDefault();
        if (existing is null) unit.Add(Guard(() => new CustomerPrice(Guid.NewGuid(), session.CompanyId, customerId, productId, price)));
        else Guard(() => existing.SetPrice(price));
        unit.Record(new AuditEvent("CustomerPriceSet", nameof(CustomerPrice), customerId.ToString(), $"Price of {product.Sku} for the customer set to {price:N2}"));
        await unit.CommitAsync(cancellationToken);
    }

    public async Task RemoveCustomerPriceAsync(Guid priceId, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.SalesPriceOverride);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var price = await unit.FindAsync<CustomerPrice>(priceId, cancellationToken) ?? throw new WorkspaceValidationException("That price no longer exists.");
        unit.Remove(price);
        await unit.CommitAsync(cancellationToken);
    }

    // ----- Internals -----

    private DateOnly Today() => DateOnly.FromDateTime(clock.UtcNow.ToLocalTime());

    /// <summary>Name, contact details and credit limit only, for CRM users who may not see sales figures.</summary>
    private async Task<PartyRow> LimitedPartyAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var p = await unit.FindAsync<Party>(id, cancellationToken) ?? throw new WorkspaceValidationException("That customer no longer exists.");
        return new PartyRow(p.Id, p.Kind, p.Name, p.Phone, p.Email, p.Gstin, p.StateCode, Domain.Companies.IndianState.FromCode(p.StateCode)?.Name ?? string.Empty,
            p.Address, p.City, p.Pincode, p.CreditLimit, p.IsActive, p.AccountId, 0, 0, 0);
    }

    private async Task<IReadOnlyList<EnquiryRow>> EnquiryRowsAsync(IUnitOfWork unit, List<Enquiry> enquiries, CancellationToken cancellationToken)
    {
        var leads = (await unit.ListAsync<Lead>(cancellationToken: cancellationToken)).ToDictionary(l => l.Id, l => l.Name);
        var customers = (await unit.ListAsync<Party>(p => p.Kind == PartyKind.Customer, cancellationToken)).ToDictionary(p => p.Id, p => p.Name);
        var productIds = enquiries.Where(e => e.ProductId != null).Select(e => e.ProductId!.Value).ToList();
        var products = (await unit.ListAsync<Product>(p => productIds.Contains(p.Id), cancellationToken)).ToDictionary(p => p.Id, p => p.DisplayName);
        return enquiries.OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedUtc)
            .Select(e => new EnquiryRow(e.Id, e.Date, e.LeadId, e.CustomerId,
                e.LeadId is { } l ? "Lead: " + leads.GetValueOrDefault(l, "?") : customers.GetValueOrDefault(e.CustomerId!.Value, "?"),
                e.Subject, e.Details, e.ProductId, e.ProductId is { } p ? products.GetValueOrDefault(p, "?") : string.Empty, e.Channel, e.Status))
            .ToList();
    }

    private async Task<IReadOnlyList<FollowUpRow>> FollowUpRowsAsync(IUnitOfWork unit, List<FollowUp> tasks, CancellationToken cancellationToken)
    {
        var today = Today();
        var names = await NamesAsync(unit, cancellationToken);
        var leads = (await unit.ListAsync<Lead>(cancellationToken: cancellationToken)).ToDictionary(l => l.Id, l => l.Name);
        var customers = (await unit.ListAsync<Party>(cancellationToken: cancellationToken)).ToDictionary(p => p.Id, p => p.Name);
        var jobs = (await unit.ListAsync<ServiceJob>(cancellationToken: cancellationToken)).ToDictionary(j => j.Id, j => j.Number);
        string Regarding(FollowUp f) =>
            f.LeadId is { } l ? "Lead " + leads.GetValueOrDefault(l, "?")
            : f.CustomerId is { } c ? customers.GetValueOrDefault(c, "?")
            : f.ServiceJobId is { } j ? "Job " + jobs.GetValueOrDefault(j, "?")
            : f.EnquiryId is not null ? "Enquiry" : string.Empty;
        return tasks.OrderBy(f => f.IsDone).ThenBy(f => f.DueDate)
            .Select(f => new FollowUpRow(f.Id, f.Title, f.Notes, f.DueDate, f.AssigneeUserId, Name(names, f.AssigneeUserId), f.IsDone, f.IsOverdue(today), f.LeadId,
                f.CustomerId, f.EnquiryId, f.ServiceJobId, Regarding(f)))
            .ToList();
    }

    private static async Task EnsureMemberAsync(IUnitOfWork unit, Guid? userId, CancellationToken cancellationToken)
    {
        if (userId is { } id && await unit.FindAsync<User>(id, cancellationToken) is null) throw new WorkspaceValidationException("That person is not a user of this company.");
    }

    private static async Task<Dictionary<Guid, string>> NamesAsync(IUnitOfWork unit, CancellationToken cancellationToken) =>
        (await unit.ListAsync<User>(cancellationToken: cancellationToken)).ToDictionary(u => u.Id, u => u.DisplayName);

    private static string Name(Dictionary<Guid, string> names, Guid? id) => id is { } u ? names.GetValueOrDefault(u, "?") : string.Empty;

    /// <summary>Digits of a phone number; the last 10 for longer numbers, so +91 and leading-zero forms of the same Indian number match.</summary>
    private static string Digits(string text)
    {
        var digits = new string([.. (text ?? string.Empty).Where(char.IsAsciiDigit)]);
        return digits.Length > 10 ? digits[^10..] : digits;
    }

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
