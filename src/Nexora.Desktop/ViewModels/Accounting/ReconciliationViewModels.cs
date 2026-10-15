using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nexora.Accounting;
using Nexora.Application;
using Nexora.Application.Formatting;
using Nexora.Application.Security;
using Nexora.Domain.Accounting;
using Nexora.Domain.Security;
using Nexora.Desktop.Services;
using Nexora.Desktop.ViewModels.Settings;

namespace Nexora.Desktop.ViewModels.Accounting;

public sealed partial class CostCentresViewModel(CostCentreService centres, IAuthorizer authorizer, ToastService toasts,
    ILogger<CostCentresViewModel> logger) : PageViewModel(logger, toasts), ILoadable
{
    public string Title => "Cost centres";
    public bool CanEdit => authorizer.Has(Permissions.AccountsManage);
    public IReadOnlyList<CostCentreKind> Kinds { get; } = Enum.GetValues<CostCentreKind>();
    [ObservableProperty] private IReadOnlyList<CostCentreRow> _centres = [];
    [ObservableProperty] private CostCentreRow? _selected;
    [ObservableProperty] private string _code = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private CostCentreKind _kind;
    public Task LoadAsync() => RunAsync(async () => Centres = await centres.ListAsync());
    partial void OnSelectedChanged(CostCentreRow? value)
    {
        if (value is not null) (Code, Name, Kind) = (value.Code, value.Name, value.Kind);
    }
    [RelayCommand] private void New() { Selected = null; Code = Name = string.Empty; Kind = CostCentreKind.Cost; }
    [RelayCommand] private async Task SaveAsync()
    {
        Guid id = default;
        if (await RunAsync(async () => id = await centres.SaveAsync(Selected?.Id, Code, Name, Kind), "Centre saved."))
        {
            await LoadAsync();
            Selected = Centres.FirstOrDefault(c => c.Id == id);
        }
    }
    [RelayCommand] private async Task ToggleActiveAsync()
    {
        if (Selected is { } row && await RunAsync(() => centres.SetActiveAsync(row.Id, !row.IsActive), "Centre updated.")) await LoadAsync();
    }
}

public sealed partial class BankReconciliationViewModel(BankReconciliationService bank, ChartOfAccountsService chart, IAuthorizer authorizer,
    ToastService toasts, ILogger<BankReconciliationViewModel> logger) : PageViewModel(logger, toasts), ILoadable
{
    public string Title => "Bank reconciliation";
    public bool CanEdit => authorizer.Has(Permissions.AccountsManage);
    [ObservableProperty] private IReadOnlyList<AccountRow> _accounts = [];
    [ObservableProperty] private AccountRow? _account;
    [ObservableProperty] private string _asOf = DateText.From(DateText.Today);
    [ObservableProperty] private string _clearedOn = DateText.From(DateText.Today);
    [ObservableProperty] private string _statementReference = string.Empty;
    [ObservableProperty] private bool _includeCleared;
    [ObservableProperty] private IReadOnlyList<BankEntryRow> _entries = [];
    [ObservableProperty] private BankEntryRow? _selected;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string _statementBalance = string.Empty;
    [ObservableProperty] private string _difference = string.Empty;
    private decimal _bankBalance;
    public async Task LoadAsync()
    {
        if (await RunAsync(async () =>
        {
            Accounts = (await chart.ListAsync()).Where(a => a.Kind == AccountKind.Bank && !a.IsGroup).ToList();
            Account ??= Accounts.FirstOrDefault();
        })) await RefreshAsync();
    }
    [RelayCommand] private Task RefreshAsync() => RunAsync(async () =>
    {
        if (Account is null) throw new WorkspaceValidationException("Create a bank ledger under Chart of accounts first.");
        var result = await bank.GetAsync(Account.Id, DateText.Parse(AsOf, "Statement date"), IncludeCleared ? DateOnly.MinValue : null);
        Entries = result.Entries;
        Selected = null;
        _bankBalance = result.BankBalance;
        Summary = $"Book balance {Amount(result.BookBalance)} · Uncleared deposits {Amount(result.UnclearedDeposits)} · Uncleared withdrawals {Amount(result.UnclearedWithdrawals)} · Expected statement balance {Amount(result.BankBalance)}";
        UpdateDifference();
    });
    partial void OnStatementBalanceChanged(string value) => UpdateDifference();
    private void UpdateDifference() => Difference = decimal.TryParse(StatementBalance, System.Globalization.NumberStyles.Number,
        System.Globalization.CultureInfo.GetCultureInfo("en-IN"), out var actual) ? $"Statement less expected: {Amount(actual - _bankBalance)}" : "Enter the statement closing balance to compare.";
    [RelayCommand] private async Task ClearAsync()
    {
        if (Selected is { } row && await RunAsync(() => bank.ClearAsync([row.EntryId], DateText.Parse(ClearedOn, "Clearing date"), StatementReference), "Entry cleared.")) await RefreshAsync();
    }
    [RelayCommand] private async Task UnclearAsync()
    {
        if (Selected?.ClearingId is { } id && await RunAsync(() => bank.UnclearAsync([id]), "Entry marked uncleared.")) await RefreshAsync();
    }
    private static string Amount(decimal value) => IndianNumberFormat.Amount(value, 2);
}
