using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nexora.Application;
using Nexora.Application.Billing;
using Nexora.Application.Crm;
using Nexora.Application.Inventory;
using Nexora.Application.Security;
using Nexora.Domain.Billing;
using Nexora.Domain.Crm;
using Nexora.Domain.Security;
using Nexora.Desktop.Services;
using Nexora.Desktop.ViewModels.Accounting;
using Nexora.Desktop.ViewModels.Expenses;
using Nexora.Desktop.ViewModels.Inventory;
using Nexora.Desktop.ViewModels.Settings;

namespace Nexora.Desktop.ViewModels.Crm;

public sealed record CrmSection(string Key, string Label);

/// <summary>
/// CRM: leads and prospects with conversion to customers, follow-ups, enquiries, and a customer view that reads
/// balances, purchases, payments and service from their own records. UI unverified at runtime.
/// </summary>
public sealed partial class CrmViewModel : PageViewModel, ILoadable
{
    private readonly CrmService _crm;
    private readonly PartyService _parties;
    private readonly InventoryCatalogueService _catalogue;
    private readonly IAuthorizer _authorizer;
    private readonly DialogService _dialogs;

    public CrmViewModel(CrmService crm, PartyService parties, InventoryCatalogueService catalogue, IAuthorizer authorizer, DialogService dialogs, ToastService toasts,
        ILogger<CrmViewModel> logger) : base(logger, toasts)
    {
        _crm = crm;
        _parties = parties;
        _catalogue = catalogue;
        _authorizer = authorizer;
        _dialogs = dialogs;
        _section = Sections[0];
    }

    public string Title => "CRM";

    public bool CanManage => _authorizer.Has(Permissions.CrmManage);

    public bool CanSetPrices => _authorizer.Has(Permissions.SalesPriceOverride);

    public IReadOnlyList<CrmSection> Sections { get; } = [new("leads", "Leads and prospects"), new("followups", "Follow-ups"), new("enquiries", "Enquiries"), new("customers", "Customers")];

    public IReadOnlyList<LeadSource> Sources { get; } = Enum.GetValues<LeadSource>();

    public IReadOnlyList<LeadStatus> OpenStatuses { get; } = [LeadStatus.New, LeadStatus.Contacted, LeadStatus.Qualified, LeadStatus.Proposal];

    public IReadOnlyList<ActivityKind> ActivityKinds { get; } = Enum.GetValues<ActivityKind>();

    public IReadOnlyList<EnquiryStatus> EnquiryStatuses { get; } = Enum.GetValues<EnquiryStatus>();

    public ObservableCollection<LeadRow> Leads { get; } = [];

    public ObservableCollection<TimelineEntry> Timeline { get; } = [];

    public ObservableCollection<PartyRow> DuplicateCandidates { get; } = [];

    public ObservableCollection<FollowUpRow> FollowUps { get; } = [];

    public ObservableCollection<EnquiryRow> Enquiries { get; } = [];

    public ObservableCollection<Choice> Members { get; } = [];

    public ObservableCollection<Choice> CustomerChoices { get; } = [];

    public ObservableCollection<Choice> ProductChoices { get; } = [];

    public ObservableCollection<PartyRow> Customers { get; } = [];

    public ObservableCollection<ContactRow> Contacts { get; } = [];

    public ObservableCollection<CustomerPriceRow> Prices { get; } = [];

    [ObservableProperty] private CrmSection _section;

    // Leads
    [ObservableProperty] private bool _showClosedLeads;
    [ObservableProperty] private LeadRow? _selectedLead;
    [ObservableProperty] private string _leadName = string.Empty;
    [ObservableProperty] private string _leadOrganisation = string.Empty;
    [ObservableProperty] private string _leadPhone = string.Empty;
    [ObservableProperty] private string _leadEmail = string.Empty;
    [ObservableProperty] private LeadSource _leadSource = LeadSource.WalkIn;
    [ObservableProperty] private Choice? _leadOwner;
    [ObservableProperty] private string _leadNotes = string.Empty;
    [ObservableProperty] private LeadStatus _leadStatus = LeadStatus.New;
    [ObservableProperty] private string _lostReason = string.Empty;
    [ObservableProperty] private PartyRow? _duplicateChoice;
    [ObservableProperty] private ActivityKind _activityKind = ActivityKind.Call;
    [ObservableProperty] private string _activitySummary = string.Empty;

    // Follow-ups
    [ObservableProperty] private bool _mineOnly = true;
    [ObservableProperty] private bool _includeDone;
    [ObservableProperty] private FollowUpRow? _selectedFollowUp;
    [ObservableProperty] private string _followUpTitle = string.Empty;
    [ObservableProperty] private string _followUpDue = DateText.From(DateText.Today);
    [ObservableProperty] private Choice? _followUpAssignee;
    [ObservableProperty] private Choice? _followUpCustomer;
    [ObservableProperty] private string _followUpNotes = string.Empty;

    // Enquiries
    [ObservableProperty] private EnquiryRow? _selectedEnquiry;
    [ObservableProperty] private string _enquiryDate = DateText.From(DateText.Today);
    [ObservableProperty] private Choice? _enquiryCustomer;
    [ObservableProperty] private string _enquirySubject = string.Empty;
    [ObservableProperty] private string _enquiryDetails = string.Empty;
    [ObservableProperty] private Choice? _enquiryProduct;
    [ObservableProperty] private ActivityKind _enquiryChannel = ActivityKind.Call;
    [ObservableProperty] private EnquiryStatus _enquiryStatus = EnquiryStatus.Open;

    // Customer view
    [ObservableProperty] private PartyRow? _selectedCustomer;
    [ObservableProperty] private CustomerOverview? _overview;
    [ObservableProperty] private string _contactName = string.Empty;
    [ObservableProperty] private string _contactRole = string.Empty;
    [ObservableProperty] private string _contactPhone = string.Empty;
    [ObservableProperty] private string _contactEmail = string.Empty;
    [ObservableProperty] private bool _contactPrimary;
    [ObservableProperty] private ContactRow? _selectedContact;
    [ObservableProperty] private Choice? _priceProduct;
    [ObservableProperty] private string _priceAmount = string.Empty;
    [ObservableProperty] private CustomerPriceRow? _selectedPrice;

    public bool ShowLeads => Section.Key == "leads";

    public bool ShowFollowUps => Section.Key == "followups";

    public bool ShowEnquiries => Section.Key == "enquiries";

    public bool ShowCustomers => Section.Key == "customers";

    public bool HasDuplicates => DuplicateCandidates.Count > 0;

    public bool HasOverview => Overview is not null;

    partial void OnSectionChanged(CrmSection value)
    {
        foreach (var name in new[] { nameof(ShowLeads), nameof(ShowFollowUps), nameof(ShowEnquiries), nameof(ShowCustomers) }) OnPropertyChanged(name);
        _ = LoadAsync();
    }

    partial void OnShowClosedLeadsChanged(bool value) => _ = LoadAsync();

    partial void OnMineOnlyChanged(bool value) => _ = LoadAsync();

    partial void OnIncludeDoneChanged(bool value) => _ = LoadAsync();

    partial void OnOverviewChanged(CustomerOverview? value) => OnPropertyChanged(nameof(HasOverview));

    partial void OnSelectedLeadChanged(LeadRow? value)
    {
        DuplicateCandidates.Clear();
        OnPropertyChanged(nameof(HasDuplicates));
        if (value is null) return;
        (LeadName, LeadOrganisation, LeadPhone, LeadEmail, LeadSource, LeadNotes, LeadStatus) = (value.Name, value.Organisation, value.Phone, value.Email, value.Source, value.Notes,
            value.Status);
        LeadOwner = Members.FirstOrDefault(m => m.Id == value.OwnerUserId) ?? Members.FirstOrDefault();
        _ = LoadTimelineAsync(value.Id, null);
    }

    partial void OnSelectedFollowUpChanged(FollowUpRow? value)
    {
        if (value is null) return;
        (FollowUpTitle, FollowUpNotes, FollowUpDue) = (value.Title, value.Notes, DateText.From(value.DueDate));
        FollowUpAssignee = Members.FirstOrDefault(m => m.Id == value.AssigneeUserId) ?? Members.FirstOrDefault();
        FollowUpCustomer = CustomerChoices.FirstOrDefault(c => c.Id == value.CustomerId) ?? CustomerChoices.FirstOrDefault();
    }

    partial void OnSelectedEnquiryChanged(EnquiryRow? value)
    {
        if (value is null) return;
        (EnquiryDate, EnquirySubject, EnquiryDetails, EnquiryChannel, EnquiryStatus) = (DateText.From(value.Date), value.Subject, value.Details, value.Channel, value.Status);
        EnquiryCustomer = CustomerChoices.FirstOrDefault(c => c.Id == value.CustomerId) ?? CustomerChoices.FirstOrDefault();
        EnquiryProduct = ProductChoices.FirstOrDefault(p => p.Id == value.ProductId) ?? ProductChoices.FirstOrDefault();
    }

    partial void OnSelectedCustomerChanged(PartyRow? value) => _ = LoadOverviewAsync();

    partial void OnSelectedContactChanged(ContactRow? value)
    {
        if (value is null) return;
        (ContactName, ContactRole, ContactPhone, ContactEmail, ContactPrimary) = (value.Name, value.Role, value.Phone, value.Email, value.IsPrimary);
    }

    public async Task LoadAsync() => await RunAsync(async () =>
    {
        if (Members.Count == 0) await LoadReferenceDataAsync();
        switch (Section.Key)
        {
            case "leads":
                var keep = SelectedLead?.Id;
                Leads.Clear();
                foreach (var row in await _crm.LeadsAsync(ShowClosedLeads)) Leads.Add(row);
                SelectedLead = Leads.FirstOrDefault(l => l.Id == keep);
                break;
            case "followups":
                FollowUps.Clear();
                foreach (var row in await _crm.FollowUpsAsync(new FollowUpFilter(MineOnly, IncludeDone))) FollowUps.Add(row);
                break;
            case "enquiries":
                Enquiries.Clear();
                foreach (var row in await _crm.EnquiriesAsync()) Enquiries.Add(row);
                break;
            case "customers":
                var selected = SelectedCustomer?.Id;
                Customers.Clear();
                foreach (var row in await CustomersAsync()) Customers.Add(row);
                SelectedCustomer = Customers.FirstOrDefault(c => c.Id == selected);
                break;
        }
    });

    private async Task<IReadOnlyList<PartyRow>> CustomersAsync()
    {
        if (_authorizer.Has(Permissions.SalesView)) return await _parties.ListAsync(PartyKind.Customer);
        // Without sales access, names come from the CRM records (no balances).
        var rows = new List<PartyRow>();
        foreach (var c in CustomerChoices.Where(c => c.Id is not null)) rows.Add((await _crm.CustomerAsync(c.Id!.Value)).Customer);
        return rows;
    }

    private async Task LoadReferenceDataAsync()
    {
        Members.Add(new Choice(null, "(nobody)"));
        foreach (var m in await _crm.MembersAsync()) Members.Add(new Choice(m.UserId, m.DisplayName));
        CustomerChoices.Add(new Choice(null, "(none)"));
        if (_authorizer.Has(Permissions.SalesView))
            foreach (var p in await _parties.ListAsync(PartyKind.Customer)) CustomerChoices.Add(new Choice(p.Id, p.Name));
        ProductChoices.Add(new Choice(null, "(none)"));
        if (_authorizer.Has(Permissions.InventoryView))
            foreach (var p in (await _catalogue.ListProductsAsync()).Where(p => !p.HasVariants)) ProductChoices.Add(new Choice(p.Id, $"{p.Sku} · {p.DisplayName}"));
        (LeadOwner, FollowUpAssignee, FollowUpCustomer, EnquiryCustomer, EnquiryProduct, PriceProduct) =
            (Members[0], Members[0], CustomerChoices[0], CustomerChoices[0], ProductChoices[0], ProductChoices[0]);
    }

    private async Task LoadTimelineAsync(Guid? leadId, Guid? customerId)
    {
        await RunAsync(async () =>
        {
            Timeline.Clear();
            foreach (var entry in await _crm.TimelineAsync(leadId, customerId)) Timeline.Add(entry);
        });
    }

    private async Task LoadOverviewAsync()
    {
        if (SelectedCustomer is not { } customer)
        {
            Overview = null;
            return;
        }

        await RunAsync(async () =>
        {
            Overview = await _crm.CustomerAsync(customer.Id);
            Contacts.Clear();
            foreach (var c in Overview.Contacts) Contacts.Add(c);
            Prices.Clear();
            foreach (var p in Overview.Prices) Prices.Add(p);
            Timeline.Clear();
            foreach (var t in Overview.Timeline) Timeline.Add(t);
        });
    }

    // ----- Leads -----

    [RelayCommand]
    private void NewLead()
    {
        SelectedLead = null;
        (LeadName, LeadOrganisation, LeadPhone, LeadEmail, LeadNotes, LeadSource) = (string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, LeadSource.WalkIn);
        LeadOwner = Members.FirstOrDefault();
        Timeline.Clear();
    }

    [RelayCommand]
    private async Task SaveLeadAsync()
    {
        Guid saved = Guid.Empty;
        if (await RunAsync(async () => saved = await _crm.SaveLeadAsync(new LeadInput(SelectedLead?.Id, LeadName, LeadOrganisation, LeadPhone, LeadEmail, LeadSource,
                LeadOwner?.Id, LeadNotes)), "Lead saved."))
        {
            await LoadAsync();
            SelectedLead = Leads.FirstOrDefault(l => l.Id == saved);
        }
    }

    [RelayCommand]
    private async Task SetLeadStatusAsync()
    {
        if (SelectedLead is not { } lead) return;
        if (await RunAsync(() => _crm.SetLeadStatusAsync(lead.Id, LeadStatus), "Status changed.")) await LoadAsync();
    }

    [RelayCommand]
    private async Task MarkLostAsync()
    {
        if (SelectedLead is not { } lead) return;
        if (await RunAsync(() => _crm.MarkLeadLostAsync(lead.Id, LostReason), "Marked lost."))
        {
            LostReason = string.Empty;
            await LoadAsync();
        }
    }

    /// <summary>Converts the lead. When a similar customer exists, the candidates are listed to link to instead.</summary>
    [RelayCommand]
    private async Task ConvertAsync()
    {
        if (SelectedLead is not { } lead) return;
        DuplicateCandidates.Clear();
        try
        {
            await _crm.ConvertLeadAsync(lead.Id);
            Toasts.Show($"{lead.Name} is now a customer.", ToastKind.Success);
            await LoadAsync();
        }
        catch (PossibleDuplicateException ex)
        {
            foreach (var candidate in ex.Candidates) DuplicateCandidates.Add(candidate);
            DuplicateChoice = DuplicateCandidates.FirstOrDefault();
            Error = ex.Message;
        }
        catch (WorkspaceValidationException ex)
        {
            Error = ex.Message;
        }

        OnPropertyChanged(nameof(HasDuplicates));
    }

    [RelayCommand]
    private async Task LinkToCustomerAsync()
    {
        if (SelectedLead is not { } lead || DuplicateChoice is not { } customer) return;
        if (await RunAsync(() => _crm.ConvertLeadAsync(lead.Id, customer.Id), $"{lead.Name} linked to {customer.Name}."))
        {
            DuplicateCandidates.Clear();
            OnPropertyChanged(nameof(HasDuplicates));
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task AddActivityAsync()
    {
        Guid? lead = ShowLeads ? SelectedLead?.Id : null;
        Guid? customer = ShowCustomers ? SelectedCustomer?.Id : null;
        if (lead is null && customer is null) return;
        if (await RunAsync(() => _crm.AddActivityAsync(lead, customer, ActivityKind, DateTime.UtcNow, ActivitySummary), "Recorded."))
        {
            ActivitySummary = string.Empty;
            if (customer is not null) await LoadOverviewAsync();
            else await LoadTimelineAsync(lead, null);
        }
    }

    // ----- Follow-ups -----

    [RelayCommand]
    private void NewFollowUp()
    {
        SelectedFollowUp = null;
        (FollowUpTitle, FollowUpNotes, FollowUpDue) = (string.Empty, string.Empty, DateText.From(DateText.Today));
        FollowUpCustomer = CustomerChoices.FirstOrDefault();
    }

    [RelayCommand]
    private async Task SaveFollowUpAsync()
    {
        if (await RunAsync(() => _crm.SaveFollowUpAsync(new FollowUpInput(SelectedFollowUp?.Id, FollowUpTitle, DateText.Parse(FollowUpDue, "Due date"), FollowUpAssignee?.Id,
                FollowUpNotes, SelectedFollowUp?.LeadId, FollowUpCustomer?.Id ?? SelectedFollowUp?.CustomerId, SelectedFollowUp?.EnquiryId, SelectedFollowUp?.ServiceJobId)),
                "Follow-up saved."))
            await LoadAsync();
    }

    [RelayCommand]
    private async Task ToggleDoneAsync()
    {
        if (SelectedFollowUp is not { } task) return;
        if (await RunAsync(() => _crm.SetFollowUpDoneAsync(task.Id, !task.IsDone), task.IsDone ? "Reopened." : "Done.")) await LoadAsync();
    }

    // ----- Enquiries -----

    [RelayCommand]
    private void NewEnquiry()
    {
        SelectedEnquiry = null;
        (EnquirySubject, EnquiryDetails, EnquiryStatus) = (string.Empty, string.Empty, EnquiryStatus.Open);
        EnquiryDate = DateText.From(DateText.Today);
    }

    [RelayCommand]
    private async Task SaveEnquiryAsync()
    {
        if (await RunAsync(async () =>
            {
                var customer = EnquiryCustomer?.Id ?? SelectedEnquiry?.CustomerId;
                var lead = customer is null ? SelectedEnquiry?.LeadId ?? (ShowLeads ? SelectedLead?.Id : null) : null;
                if (customer is null && lead is null) throw new WorkspaceValidationException("Choose the customer (or add the enquiry from a lead).");
                await _crm.SaveEnquiryAsync(new EnquiryInput(SelectedEnquiry?.Id, DateText.Parse(EnquiryDate, "Date"), lead, customer, EnquirySubject, EnquiryDetails,
                    EnquiryProduct?.Id, EnquiryChannel, EnquiryStatus));
            }, "Enquiry saved."))
            await LoadAsync();
    }

    // ----- Customer view -----

    [RelayCommand]
    private async Task SaveContactAsync()
    {
        if (SelectedCustomer is not { } customer) return;
        if (await RunAsync(() => _crm.SaveContactAsync(new ContactInput(SelectedContact?.Id, customer.Id, ContactName, ContactRole, ContactPhone, ContactEmail, ContactPrimary)),
                "Contact saved."))
        {
            (ContactName, ContactRole, ContactPhone, ContactEmail, ContactPrimary) = (string.Empty, string.Empty, string.Empty, string.Empty, false);
            await LoadOverviewAsync();
        }
    }

    [RelayCommand]
    private async Task DeleteContactAsync()
    {
        if (SelectedContact is not { } contact) return;
        if (await RunAsync(() => _crm.DeleteContactAsync(contact.Id), "Contact removed.")) await LoadOverviewAsync();
    }

    [RelayCommand]
    private async Task SavePriceAsync()
    {
        if (SelectedCustomer is not { } customer || PriceProduct?.Id is not { } product) return;
        if (await RunAsync(() => _crm.SetCustomerPriceAsync(customer.Id, product, NumberText.Parse(PriceAmount, "Price")), "Customer price saved."))
        {
            PriceAmount = string.Empty;
            await LoadOverviewAsync();
        }
    }

    [RelayCommand]
    private async Task RemovePriceAsync()
    {
        if (SelectedPrice is not { } price) return;
        if (!await _dialogs.ConfirmAsync($"Remove the special price of {price.Sku}?", "Sales to this customer will use the product's selling price again.", "Remove")) return;
        if (await RunAsync(() => _crm.RemoveCustomerPriceAsync(price.Id), "Price removed.")) await LoadOverviewAsync();
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();
}
