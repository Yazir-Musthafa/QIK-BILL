using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nexora.Accounting;
using Nexora.Application;
using Nexora.Application.Billing;
using Nexora.Application.Companies;
using Nexora.Application.Formatting;
using Nexora.Application.Inventory;
using Nexora.Application.Security;
using Nexora.Domain.Accounting;
using Nexora.Domain.Billing;
using Nexora.Domain.Security;
using Nexora.Desktop.Services;
using Nexora.Desktop.Services.Printing;
using Nexora.Desktop.ViewModels.Accounting;
using Nexora.Desktop.ViewModels.Expenses;
using Nexora.Desktop.ViewModels.Settings;
using Nexora.Reporting.Export;
using Nexora.Reporting.Reports;

namespace Nexora.Desktop.ViewModels.Reporting;

public sealed partial class ReportsViewModel(ReportService reports, ReportExportService export, PartyService parties,
    InventoryCatalogueService inventory, CompanyService companies, ChartOfAccountsService chart, IAuthorizer authorizer,
    PrintActions printing, ReportDrillActions drill, ToastService toasts, ILogger<ReportsViewModel> logger) : PageViewModel(logger, toasts), ILoadable
{
    private IReadOnlyList<Choice> _customers = [], _suppliers = [];
    private IReadOnlyList<AccountRow> _chart = [];
    private ReportRequest? _lastRequest;
    public string Title => "Reports";
    public bool CanExport => authorizer.Has(Permissions.DataExport);
    public IReadOnlyList<ReportPeriod> Periods { get; } = Enum.GetValues<ReportPeriod>();
    public IReadOnlyList<string> Groupings { get; } = ["Category", "Date", "Month", "Branch", "Department", "Method"];
    public IReadOnlyList<int> PageSizes { get; } = [50, 100, 200, 500];
    [ObservableProperty] private IReadOnlyList<ReportDefinition> _catalogue = [];
    [ObservableProperty] private ReportDefinition? _selectedReport;
    [ObservableProperty] private string _from = DateText.From(new DateOnly(DateText.Today.Year, DateText.Today.Month, 1));
    [ObservableProperty] private string _to = DateText.From(DateText.Today);
    [ObservableProperty] private string _asOf = DateText.From(DateText.Today);
    [ObservableProperty] private ReportPeriod _period = ReportPeriod.Month;
    [ObservableProperty] private string _grouping = "Category";
    [ObservableProperty] private IReadOnlyList<Choice> _partyChoices = [];
    [ObservableProperty] private IReadOnlyList<Choice> _products = [];
    [ObservableProperty] private IReadOnlyList<Choice> _warehouses = [];
    [ObservableProperty] private IReadOnlyList<Choice> _accounts = [];
    [ObservableProperty] private Choice? _party;
    [ObservableProperty] private Choice? _product;
    [ObservableProperty] private Choice? _warehouse;
    [ObservableProperty] private Choice? _account;
    [ObservableProperty] private string _search = string.Empty;
    [ObservableProperty] private ReportColumn? _sortColumn;
    [ObservableProperty] private bool _descending;
    [ObservableProperty] private int _pageSize = 100;
    [ObservableProperty] private ReportResult? _result;
    [ObservableProperty] private ReportRow? _selectedRow;
    [ObservableProperty] private string _pageText = "Choose a report, set its parameters, then Run.";
    [ObservableProperty] private string _totalsText = string.Empty;
    [ObservableProperty] private string _notes = string.Empty;
    public bool HasRange => SelectedReport?.Has(ReportParameter.DateRange) == true;
    public bool HasAsOf => SelectedReport?.Has(ReportParameter.AsOf) == true;
    public bool HasPeriod => SelectedReport?.Has(ReportParameter.Period) == true;
    public bool HasParty => SelectedReport?.Has(ReportParameter.Customer) == true || SelectedReport?.Has(ReportParameter.Supplier) == true;
    public bool HasProduct => SelectedReport?.Has(ReportParameter.Product) == true;
    public bool HasWarehouse => SelectedReport?.Has(ReportParameter.Warehouse) == true;
    public bool HasAccount => SelectedReport?.Has(ReportParameter.Account) == true || SelectedReport?.Has(ReportParameter.BankAccount) == true;
    public bool HasGrouping => SelectedReport?.Has(ReportParameter.ExpenseGrouping) == true;
    public bool HasResult => Result is not null;
    public bool CanPrevious => Result is { Page: > 1 };
    public bool CanNext => Result is { } r && r.Page < r.PageCount;
    public bool CanSearch => SelectedReport?.Key != "accounts.ledger";
    public IReadOnlyList<ReportColumn> SortColumns => Result is not { } result ? [] : result.Definition.Key switch
    {
        "sales.register" or "purchases.register" => result.Columns.Where(c => c.Key is "date" or "number" or "party" or "total").ToList(),
        "inventory.movements" => result.Columns.Where(c => c.Key == "date").ToList(),
        _ => result.Rows.Any(r => r.Emphasis) ? [] : result.Columns,
    };
    public Task LoadAsync() => RunAsync(async () =>
    {
        Catalogue = reports.Available();
        if (authorizer.Has(Permissions.SalesView)) _customers = Optional((await parties.ListAsync(PartyKind.Customer, true)).Select(p => new Choice(p.Id, p.Name)));
        if (authorizer.Has(Permissions.PurchasesView)) _suppliers = Optional((await parties.ListAsync(PartyKind.Supplier, true)).Select(p => new Choice(p.Id, p.Name)));
        if (authorizer.Has(Permissions.InventoryView)) Products = Optional((await inventory.ListProductsAsync(true)).Select(p => new Choice(p.Id, p.DisplayName)));
        if (authorizer.Has(Permissions.CompanyView)) Warehouses = Optional((await companies.ListWarehousesAsync()).Select(w => new Choice(w.Id, w.Name)));
        if (authorizer.Has(Permissions.AccountsView)) _chart = await chart.ListAsync();
        SelectedReport = Catalogue.FirstOrDefault();
    });
    partial void OnSelectedReportChanged(ReportDefinition? value)
    {
        Result = null; SortColumn = null; SelectedRow = null; Notes = TotalsText = string.Empty;
        Search = string.Empty; Descending = false; _lastRequest = null;
        OnPropertyChanged(nameof(CanSearch));
        PageText = value?.Description ?? "No reports are available for your role.";
        PartyChoices = value?.Has(ReportParameter.Supplier) == true ? _suppliers : _customers;
        Accounts = _chart.Where(a => !a.IsGroup && (value?.Has(ReportParameter.BankAccount) != true || a.Kind == AccountKind.Bank)).Select(a => new Choice(a.Id, a.Code + " · " + a.Name)).ToList();
        Party = PartyChoices.FirstOrDefault(); Product = Products.FirstOrDefault(); Warehouse = Warehouses.FirstOrDefault(); Account = Accounts.FirstOrDefault();
        foreach (var property in new[] { nameof(HasRange), nameof(HasAsOf), nameof(HasPeriod), nameof(HasParty), nameof(HasProduct), nameof(HasWarehouse), nameof(HasAccount), nameof(HasGrouping) }) OnPropertyChanged(property);
    }
    partial void OnResultChanged(ReportResult? value)
    {
        foreach (var property in new[] { nameof(HasResult), nameof(CanPrevious), nameof(CanNext), nameof(SortColumns) }) OnPropertyChanged(property);
    }
    private ReportRequest Request() => new()
    {
        From = HasRange ? DateText.Parse(From, "From") : null, To = HasRange ? DateText.Parse(To, "To") : null,
        AsOf = HasAsOf ? DateText.Parse(AsOf, "As of") : null, Period = Period, Grouping = Grouping,
        PartyId = HasParty ? Party?.Id : null, ProductId = HasProduct ? Product?.Id : null, WarehouseId = HasWarehouse ? Warehouse?.Id : null,
        AccountId = HasAccount ? Account?.Id : null, Search = CanSearch ? Search : null, SortKey = SortColumn?.Key, SortDescending = Descending, PageSize = PageSize,
    };
    [RelayCommand] private Task RunReportAsync() => RunAsync(async () =>
    {
        if (SelectedReport is null) return;
        await ShowAsync(SelectedReport.Key, Request());
    });
    private async Task ShowAsync(string key, ReportRequest request)
    {
        var result = await reports.RunAsync(key, request);
        Result = result; _lastRequest = request; SelectedRow = null;
        PageText = $"Page {result.Page} of {result.PageCount} · {result.TotalRows} rows · {result.ParameterText}";
        Notes = string.Join(Environment.NewLine, result.Notes);
        TotalsText = "Selection totals: " + string.Join(" · ", result.Columns.Select((c, i) =>
            result.Totals is { } totals && totals[i] is decimal d ? c.Header + " " + IndianNumberFormat.Number(d) : null).Where(t => t is not null));
        if (result.Totals is null) TotalsText = string.Empty;
    }
    [RelayCommand] private Task PreviousAsync() => ChangePageAsync(-1);
    [RelayCommand] private Task NextAsync() => ChangePageAsync(1);
    private Task ChangePageAsync(int delta) => RunAsync(async () =>
    {
        if (Result is { } result && _lastRequest is { } request && result.Page + delta >= 1 && result.Page + delta <= result.PageCount)
            await ShowAsync(result.Definition.Key, request with { Page = result.Page + delta });
    });
    [RelayCommand] private Task ExportAsync(string format) => RunAsync(async () =>
    {
        if (Result is { } result && _lastRequest is { } request)
            await DataFiles.SaveAsync(await export.ExportAsync(result.Definition.Key, request, Enum.Parse<ExportFormat>(format)));
    });
    [RelayCommand] private Task PreviewAsync() => OutputAsync(false);
    [RelayCommand] private Task PrintAsync() => OutputAsync(true);
    private Task OutputAsync(bool print) => RunAsync(async () =>
    {
        if (Result is not { } result || _lastRequest is not { } request) return;
        var subject = await export.PrintSubjectAsync(result.Definition.Key, request);
        if (print) await printing.PrintAsync(subject); else await printing.PreviewAsync(subject);
    });
    [RelayCommand] private Task OpenSourceAsync() => RunAsync(async () =>
    {
        if (SelectedRow?.Drill is not { } target) throw new WorkspaceValidationException("This row has no source detail. Select a detail row.");
        if (target.Kind == DrillKind.Ledger)
        {
            var previous = _lastRequest;
            var ledger = Catalogue.FirstOrDefault(r => r.Key == "accounts.ledger") ?? throw new WorkspaceValidationException("Your role cannot open ledgers.");
            SelectedReport = ledger;
            Account = Accounts.FirstOrDefault(a => a.Id == target.Id);
            var request = ReportNavigation.Ledger(previous, target.Id, DateText.Today, PageSize);
            From = DateText.From(request.From!.Value);
            To = DateText.From(request.To!.Value);
            await ShowAsync(ledger.Key, request);
        }
        else await drill.OpenAsync(target);
    });
    private static IReadOnlyList<Choice> Optional(IEnumerable<Choice> choices) => new[] { new Choice(null, "(All)") }.Concat(choices).ToList();
}
