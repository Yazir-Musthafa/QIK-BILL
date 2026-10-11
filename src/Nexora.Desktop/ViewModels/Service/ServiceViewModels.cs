using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nexora.Application;
using Nexora.Application.Billing;
using Nexora.Application.Companies;
using Nexora.Application.Crm;
using Nexora.Application.Inventory;
using Nexora.Application.Security;
using Nexora.Application.Service;
using Nexora.Domain.Billing;
using Nexora.Domain.Security;
using Nexora.Domain.Service;
using Nexora.Desktop.Services;
using Nexora.Desktop.Services.Printing;
using Nexora.Desktop.ViewModels.Accounting;
using Nexora.Desktop.ViewModels.Expenses;
using Nexora.Desktop.ViewModels.Inventory;
using Nexora.Desktop.ViewModels.Settings;
using Nexora.Printing;

namespace Nexora.Desktop.ViewModels.Service;

public sealed record ServiceSection(string Key, string Label);

/// <summary>
/// Service jobs: tickets and job cards with the enforced workflow, warranty from the sale of the serial, spare parts
/// through stock, labour, the service invoice through billing, printing, maintenance reminders and history.
/// UI unverified at runtime.
/// </summary>
public sealed partial class ServiceViewModel : PageViewModel, ILoadable
{
    private readonly ServiceJobService _service;
    private readonly PartyService _parties;
    private readonly CompanyService _companies;
    private readonly CrmService _crm;
    private readonly InventoryCatalogueService _catalogue;
    private readonly PrintActions _print;
    private readonly IAuthorizer _authorizer;
    private readonly DialogService _dialogs;

    public ServiceViewModel(ServiceJobService service, PartyService parties, CompanyService companies, CrmService crm, InventoryCatalogueService catalogue,
        PrintActions print, IAuthorizer authorizer, DialogService dialogs, ToastService toasts, ILogger<ServiceViewModel> logger) : base(logger, toasts)
    {
        _service = service;
        _parties = parties;
        _companies = companies;
        _crm = crm;
        _catalogue = catalogue;
        _print = print;
        _authorizer = authorizer;
        _dialogs = dialogs;
        _section = Sections[0];
    }

    public string Title => "Service jobs";

    public bool CanManage => _authorizer.Has(Permissions.ServiceManage);

    public bool CanInvoice => CanManage && _authorizer.Has(Permissions.SalesCreate);

    public IReadOnlyList<ServiceSection> Sections { get; } =
        [new("jobs", "Jobs"), new("editor", "Job card"), new("maintenance", "Maintenance"), new("history", "History"), new("settings", "Settings")];

    public IReadOnlyList<ServiceCharge> Charges { get; } = Enum.GetValues<ServiceCharge>();

    public ObservableCollection<ServiceJobRow> Jobs { get; } = [];

    public ObservableCollection<ServiceStatus> NextStatuses { get; } = [];

    public ObservableCollection<Choice> CustomerChoices { get; } = [];

    public ObservableCollection<Choice> ProductChoices { get; } = [];

    public ObservableCollection<Choice> PartChoices { get; } = [];

    public ObservableCollection<Choice> Technicians { get; } = [];

    public ObservableCollection<Choice> Warehouses { get; } = [];

    public ObservableCollection<Choice> TaxCategories { get; } = [];

    public ObservableCollection<MaintenanceRow> Schedules { get; } = [];

    public ObservableCollection<ServiceHistoryRow> History { get; } = [];

    [ObservableProperty] private ServiceSection _section;

    // Jobs list and detail
    [ObservableProperty] private bool _showClosed;
    [ObservableProperty] private string _search = string.Empty;
    [ObservableProperty] private ServiceJobRow? _selected;
    [ObservableProperty] private ServiceJobView? _detail;
    [ObservableProperty] private ServiceStatus? _nextStatus;
    [ObservableProperty] private string _moveReason = string.Empty;
    [ObservableProperty] private string _actionDate = DateText.From(DateText.Today);
    [ObservableProperty] private ServiceCharge _charge;
    [ObservableProperty] private Choice? _partProduct;
    [ObservableProperty] private string _partQuantity = "1";
    [ObservableProperty] private string _partBatch = string.Empty;
    [ObservableProperty] private string _partSerial = string.Empty;
    [ObservableProperty] private ServicePartRow? _selectedPart;
    [ObservableProperty] private string _labourDescription = string.Empty;
    [ObservableProperty] private string _labourQuantity = "1";
    [ObservableProperty] private string _labourRate = string.Empty;
    [ObservableProperty] private string _labourSac = string.Empty;
    [ObservableProperty] private Choice? _labourTax;
    [ObservableProperty] private ServiceLabourRow? _selectedLabour;

    // Job editor
    [ObservableProperty] private Guid? _editingId;
    [ObservableProperty] private string _receivedOn = DateText.From(DateText.Today);
    [ObservableProperty] private Choice? _jobCustomer;
    [ObservableProperty] private Choice? _jobProduct;
    [ObservableProperty] private string _jobItem = string.Empty;
    [ObservableProperty] private string _jobSerial = string.Empty;
    [ObservableProperty] private string _jobFault = string.Empty;
    [ObservableProperty] private string _jobAccessories = string.Empty;
    [ObservableProperty] private string _jobDiagnosis = string.Empty;
    [ObservableProperty] private Choice? _jobTechnician;
    [ObservableProperty] private string _jobEstimate = string.Empty;
    [ObservableProperty] private Choice? _jobWarehouse;
    [ObservableProperty] private string _warrantyText = string.Empty;

    // Maintenance
    [ObservableProperty] private MaintenanceRow? _selectedSchedule;
    [ObservableProperty] private Choice? _scheduleCustomer;
    [ObservableProperty] private string _scheduleItem = string.Empty;
    [ObservableProperty] private string _scheduleSerial = string.Empty;
    [ObservableProperty] private string _scheduleInterval = "6";
    [ObservableProperty] private string _scheduleNextDue = DateText.From(DateText.Today.AddMonths(6));
    [ObservableProperty] private Choice? _scheduleAssignee;
    [ObservableProperty] private string _scheduleNotes = string.Empty;

    // History
    [ObservableProperty] private Choice? _historyCustomer;
    [ObservableProperty] private string _historySerial = string.Empty;

    // Settings
    [ObservableProperty] private string _defaultLabourSac = string.Empty;
    [ObservableProperty] private string _reminderLeadDays = "7";

    public bool ShowJobs => Section.Key == "jobs";

    public bool ShowEditor => Section.Key == "editor";

    public bool ShowMaintenance => Section.Key == "maintenance";

    public bool ShowHistory => Section.Key == "history";

    public bool ShowSettings => Section.Key == "settings";

    public bool HasDetail => Detail is not null;

    public bool DetailOpen => Detail is { Status: not (ServiceStatus.Delivered or ServiceStatus.Cancelled) };

    public bool DetailCanChange => CanManage && DetailOpen;

    public bool DetailHasInvoice => Detail?.InvoiceId is not null;

    public bool DetailInvoicePosted => Detail is { InvoiceState: BillingStatus.Posted, InvoiceCancelled: false };

    public string EditorTitle => EditingId is null ? "New job card" : "Edit job card";

    partial void OnSectionChanged(ServiceSection value)
    {
        foreach (var name in new[] { nameof(ShowJobs), nameof(ShowEditor), nameof(ShowMaintenance), nameof(ShowHistory), nameof(ShowSettings) }) OnPropertyChanged(name);
        _ = LoadAsync();
    }

    partial void OnShowClosedChanged(bool value) => _ = LoadAsync();

    partial void OnEditingIdChanged(Guid? value) => OnPropertyChanged(nameof(EditorTitle));

    partial void OnSelectedChanged(ServiceJobRow? value) => _ = LoadDetailAsync();

    partial void OnDetailChanged(ServiceJobView? value)
    {
        foreach (var name in new[] { nameof(HasDetail), nameof(DetailOpen), nameof(DetailCanChange), nameof(DetailHasInvoice), nameof(DetailInvoicePosted) })
            OnPropertyChanged(name);
        NextStatuses.Clear();
        if (value is null) return;
        foreach (var s in value.NextStatuses) NextStatuses.Add(s);
        NextStatus = NextStatuses.FirstOrDefault();
        Charge = value.Charge;
    }

    partial void OnSelectedScheduleChanged(MaintenanceRow? value)
    {
        if (value is null) return;
        (ScheduleItem, ScheduleSerial, ScheduleInterval, ScheduleNextDue, ScheduleNotes) =
            (value.Item, value.SerialNumber, value.IntervalMonths.ToString(System.Globalization.CultureInfo.InvariantCulture), DateText.From(value.NextDueDate), value.Notes);
        ScheduleCustomer = CustomerChoices.FirstOrDefault(c => c.Id == value.CustomerId);
        ScheduleAssignee = Technicians.FirstOrDefault(t => t.Id == value.AssigneeUserId) ?? Technicians.FirstOrDefault();
    }

    public async Task LoadAsync() => await RunAsync(async () =>
    {
        if (CustomerChoices.Count == 0) await LoadReferenceDataAsync();
        switch (Section.Key)
        {
            case "jobs":
                var keep = Selected?.Id;
                Jobs.Clear();
                foreach (var row in await _service.ListAsync(ShowClosed, Search)) Jobs.Add(row);
                Selected = Jobs.FirstOrDefault(j => j.Id == keep);
                break;
            case "maintenance":
                Schedules.Clear();
                foreach (var row in await _service.SchedulesAsync()) Schedules.Add(row);
                break;
            case "settings":
                var (sac, lead) = await _service.SettingsAsync();
                (DefaultLabourSac, ReminderLeadDays) = (sac, lead.ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;
        }
    });

    private async Task LoadReferenceDataAsync()
    {
        foreach (var c in await _parties.CustomerChoicesAsync()) CustomerChoices.Add(new Choice(c.Id, c.Phone.Length > 0 ? $"{c.Name} · {c.Phone}" : c.Name));
        ProductChoices.Add(new Choice(null, "(not one of our products)"));
        if (_authorizer.Has(Permissions.InventoryView))
            foreach (var p in (await _catalogue.ListProductsAsync()).Where(p => !p.HasVariants))
            {
                ProductChoices.Add(new Choice(p.Id, $"{p.Sku} · {p.DisplayName}"));
                PartChoices.Add(new Choice(p.Id, $"{p.Sku} · {p.DisplayName}"));
            }

        Technicians.Add(new Choice(null, "(not assigned)"));
        foreach (var m in await _crm.MembersAsync()) Technicians.Add(new Choice(m.UserId, m.DisplayName));
        foreach (var w in (await _companies.ListWarehousesAsync()).Where(w => w.IsActive)) Warehouses.Add(new Choice(w.Id, w.Name));
        TaxCategories.Add(new Choice(null, "(choose)"));
        if (_authorizer.Has(Permissions.InventoryView))
            foreach (var t in (await _catalogue.ListTaxCategoriesAsync()).Where(t => t.IsActive)) TaxCategories.Add(new Choice(t.Id, t.Name));
        (JobProduct, JobTechnician, JobWarehouse, LabourTax, ScheduleAssignee, PartProduct) =
            (ProductChoices[0], Technicians[0], Warehouses.FirstOrDefault(), TaxCategories[0], Technicians[0], PartChoices.FirstOrDefault());
        HistoryCustomer = null;
    }

    private async Task LoadDetailAsync()
    {
        if (Selected is not { } row)
        {
            Detail = null;
            return;
        }

        await RunAsync(async () => Detail = await _service.GetAsync(row.Id));
    }

    private async Task ReloadAsync()
    {
        await LoadAsync();
        await LoadDetailAsync();
    }

    // ----- Jobs list -----

    [RelayCommand]
    private Task ApplySearchAsync() => LoadAsync();

    [RelayCommand]
    private void NewJob()
    {
        EditingId = null;
        (ReceivedOn, JobItem, JobSerial, JobFault, JobAccessories, JobDiagnosis, JobEstimate, WarrantyText) =
            (DateText.From(DateText.Today), string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
        (JobCustomer, JobProduct, JobTechnician, JobWarehouse) = (null, ProductChoices.FirstOrDefault(), Technicians.FirstOrDefault(), Warehouses.FirstOrDefault());
        Section = Sections[1];
    }

    [RelayCommand]
    private void EditSelected()
    {
        if (Detail is not { } d) return;
        EditingId = d.Id;
        (ReceivedOn, JobItem, JobSerial, JobFault, JobAccessories, JobDiagnosis) = (DateText.From(d.ReceivedOn), d.Item, d.SerialNumber, d.Fault, d.Accessories, d.Diagnosis);
        JobEstimate = d.EstimatedCompletion is { } e ? DateText.From(e) : string.Empty;
        JobCustomer = CustomerChoices.FirstOrDefault(c => c.Id == d.CustomerId);
        JobProduct = ProductChoices.FirstOrDefault(p => p.Id == d.ProductId) ?? ProductChoices.FirstOrDefault();
        JobTechnician = Technicians.FirstOrDefault(t => t.Id == d.TechnicianUserId) ?? Technicians.FirstOrDefault();
        JobWarehouse = Warehouses.FirstOrDefault(w => w.Id == d.WarehouseId);
        WarrantyText = d.WarrantyUntil is { } until ? $"Under warranty until {DateText.From(until)} ({d.WarrantySource})." : string.Empty;
        Section = Sections[1];
    }

    [RelayCommand]
    private async Task MoveAsync()
    {
        if (Detail is not { } d || NextStatus is not { } status) return;
        if (status == ServiceStatus.Cancelled && !await _dialogs.ConfirmAsync($"Cancel job {d.Number}?", "A cancelled job cannot be reopened.", "Cancel job", true)) return;
        if (await RunAsync(() => _service.MoveAsync(d.Id, status, MoveReason, DateText.Parse(ActionDate, "Date")), $"Job {d.Number} is now {status.Label().ToLowerInvariant()}."))
        {
            MoveReason = string.Empty;
            await ReloadAsync();
        }
    }

    [RelayCommand]
    private async Task SetChargeAsync()
    {
        if (Detail is not { } d || d.Charge == Charge) return;
        if (await RunAsync(() => _service.SetChargeAsync(d.Id, Charge), "Charge changed.")) await ReloadAsync();
    }

    [RelayCommand]
    private async Task IssuePartAsync()
    {
        if (Detail is not { } d || PartProduct?.Id is not { } product) return;
        if (await RunAsync(() => _service.IssuePartAsync(d.Id, new ServicePartInput(product, NumberText.Parse(PartQuantity, "Quantity"), DateText.Parse(ActionDate, "Date"),
                SerialNumber: string.IsNullOrWhiteSpace(PartSerial) ? null : PartSerial, BatchNumber: string.IsNullOrWhiteSpace(PartBatch) ? null : PartBatch)), "Part issued from stock."))
        {
            (PartQuantity, PartBatch, PartSerial) = ("1", string.Empty, string.Empty);
            await ReloadAsync();
        }
    }

    [RelayCommand]
    private async Task ReturnPartAsync()
    {
        if (SelectedPart is not { } part) return;
        if (!await _dialogs.ConfirmAsync($"Return {part.Sku} to stock?", "It comes back at the cost it left with, and its cost posting is reversed.", "Return")) return;
        if (await RunAsync(() => _service.ReturnPartAsync(part.Id, DateText.Parse(ActionDate, "Date")), "Part returned to stock.")) await ReloadAsync();
    }

    [RelayCommand]
    private async Task AddLabourAsync()
    {
        if (Detail is not { } d) return;
        if (await RunAsync(() => _service.AddLabourAsync(d.Id, new ServiceLabourInput(LabourDescription, NumberText.Parse(LabourQuantity, "Quantity"),
                NumberText.Parse(LabourRate, "Rate"), LabourSac, LabourTax?.Id)), "Labour added."))
        {
            (LabourDescription, LabourQuantity, LabourRate) = (string.Empty, "1", string.Empty);
            await ReloadAsync();
        }
    }

    [RelayCommand]
    private async Task RemoveLabourAsync()
    {
        if (SelectedLabour is not { } labour) return;
        if (await RunAsync(() => _service.RemoveLabourAsync(labour.Id), "Labour removed.")) await ReloadAsync();
    }

    [RelayCommand]
    private async Task CreateInvoiceAsync()
    {
        if (Detail is not { } d) return;
        if (await RunAsync(() => _service.CreateInvoiceAsync(d.Id), "Service invoice draft created. Review and post it in Sales invoices.")) await ReloadAsync();
    }

    [RelayCommand]
    private async Task PreviewJobCardAsync()
    {
        if (Detail is { } d) await _print.PreviewAsync(new ServiceJobSubject(d.Id));
    }

    [RelayCommand]
    private async Task PrintJobCardAsync()
    {
        if (Detail is { } d) await _print.PrintAsync(new ServiceJobSubject(d.Id));
    }

    [RelayCommand]
    private async Task ExportJobCardAsync()
    {
        if (Detail is { } d) await _print.ExportPdfAsync(new ServiceJobSubject(d.Id));
    }

    [RelayCommand]
    private async Task PrintInvoiceAsync()
    {
        if (Detail is { InvoiceId: { } invoice } && DetailInvoicePosted) await _print.PreviewAsync(new BillingSubject(invoice));
    }

    // ----- Job editor -----

    [RelayCommand]
    private async Task CheckWarrantyAsync()
    {
        if (string.IsNullOrWhiteSpace(JobSerial))
        {
            WarrantyText = "Enter the serial number to look up its sale.";
            return;
        }

        await RunAsync(async () =>
        {
            var info = await _service.WarrantyAsync(JobSerial);
            WarrantyText = info switch
            {
                null => "No posted sale of this serial number was found. The job will be chargeable unless you change it.",
                { WarrantyUntil: { } until } when info.IsCovered(DateText.Parse(ReceivedOn, "Received on")) =>
                    $"Under warranty until {DateText.From(until)} (invoice {info.InvoiceNumber}, sold {DateText.From(info.SoldOn!.Value)}).",
                { WarrantyUntil: { } until } => $"Warranty ended on {DateText.From(until)} (invoice {info.InvoiceNumber}).",
                _ => $"Sold on invoice {info.InvoiceNumber}; the product has no warranty period.",
            };
            if (info?.ProductId is { } product) JobProduct = ProductChoices.FirstOrDefault(p => p.Id == product) ?? JobProduct;
        });
    }

    [RelayCommand]
    private async Task SaveJobAsync()
    {
        Guid saved = EditingId ?? Guid.Empty;
        if (await RunAsync(async () =>
            {
                if (JobCustomer?.Id is not { } customer) throw new WorkspaceValidationException("Choose the customer.");
                if (JobWarehouse?.Id is not { } warehouse) throw new WorkspaceValidationException("Choose the warehouse parts are issued from.");
                var input = new ServiceJobInput(EditingId, DateText.Parse(ReceivedOn, "Received on"), customer, JobItem, JobFault, warehouse, JobProduct?.Id, JobSerial,
                    JobAccessories, JobDiagnosis, JobTechnician?.Id, string.IsNullOrWhiteSpace(JobEstimate) ? null : DateText.Parse(JobEstimate, "Estimated completion"));
                if (EditingId is null) saved = await _service.CreateAsync(input);
                else await _service.UpdateAsync(input);
            }, "Job card saved."))
        {
            Section = Sections[0];
            await LoadAsync();
            Selected = Jobs.FirstOrDefault(j => j.Id == saved);
        }
    }

    [RelayCommand]
    private void CancelEdit() => Section = Sections[0];

    // ----- Maintenance -----

    [RelayCommand]
    private void NewSchedule()
    {
        SelectedSchedule = null;
        (ScheduleItem, ScheduleSerial, ScheduleInterval, ScheduleNotes) = (string.Empty, string.Empty, "6", string.Empty);
        ScheduleNextDue = DateText.From(DateText.Today.AddMonths(6));
        (ScheduleCustomer, ScheduleAssignee) = (null, Technicians.FirstOrDefault());
    }

    [RelayCommand]
    private async Task SaveScheduleAsync()
    {
        if (await RunAsync(async () =>
            {
                if (ScheduleCustomer?.Id is not { } customer) throw new WorkspaceValidationException("Choose the customer.");
                if (!int.TryParse(ScheduleInterval, out var months)) throw new WorkspaceValidationException("Interval: enter whole months, for example 6.");
                await _service.SaveScheduleAsync(new MaintenanceInput(SelectedSchedule?.Id, customer, ScheduleItem, months, DateText.Parse(ScheduleNextDue, "Next due"),
                    SelectedSchedule?.ProductId, ScheduleSerial, ScheduleAssignee?.Id, ScheduleNotes));
            }, "Schedule saved."))
            await LoadAsync();
    }

    [RelayCommand]
    private async Task ToggleScheduleAsync()
    {
        if (SelectedSchedule is not { } s) return;
        if (await RunAsync(() => _service.SetScheduleActiveAsync(s.Id, !s.IsActive), s.IsActive ? "Schedule paused." : "Schedule resumed.")) await LoadAsync();
    }

    [RelayCommand]
    private async Task GenerateRemindersAsync()
    {
        var created = 0;
        if (await RunAsync(async () => created = await _service.GenerateRemindersAsync(DateText.Today)))
        {
            Toasts.Show(created == 0 ? "No reminders are due." : $"{created} follow-up task(s) created.", ToastKind.Success);
            await LoadAsync();
        }
    }

    // ----- History -----

    [RelayCommand]
    private async Task ShowHistoryAsync()
    {
        await RunAsync(async () =>
        {
            if (HistoryCustomer?.Id is null && string.IsNullOrWhiteSpace(HistorySerial))
                throw new WorkspaceValidationException("Choose a customer or enter a serial number.");
            History.Clear();
            foreach (var row in await _service.HistoryAsync(HistoryCustomer?.Id, string.IsNullOrWhiteSpace(HistorySerial) ? null : HistorySerial)) History.Add(row);
        });
    }

    // ----- Settings -----

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        await RunAsync(() =>
        {
            if (!int.TryParse(ReminderLeadDays, out var days)) throw new WorkspaceValidationException("Reminder lead: enter whole days, for example 7.");
            return _service.SaveSettingsAsync(DefaultLabourSac, days);
        }, "Service settings saved.");
    }

    [RelayCommand]
    private Task RefreshAsync() => ReloadAsync();
}
