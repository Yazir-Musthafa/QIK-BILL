using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nexora.Accounting;
using Nexora.Application;
using Nexora.Application.Formatting;
using Nexora.Application.Security;
using Nexora.Domain.Accounting;
using Nexora.Domain.Companies;
using Nexora.Domain.Security;
using Nexora.Desktop.Services;
using Nexora.Desktop.ViewModels.Settings;

namespace Nexora.Desktop.ViewModels.Accounting;

/// <summary>Dates are typed as dd-MM-yyyy; parsing is strict so a mistyped date is reported, not guessed.</summary>
public static class DateText
{
    public const string Format = "dd-MM-yyyy";

    public static string From(DateOnly date) => date.ToString(Format, CultureInfo.InvariantCulture);

    public static DateOnly Parse(string? text, string field)
    {
        if (DateOnly.TryParseExact((text ?? string.Empty).Trim(), [Format, "d-M-yyyy", "dd/MM/yyyy", "d/M/yyyy"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;
        throw new WorkspaceValidationException($"{field}: enter a date as dd-mm-yyyy, for example 01-04-2026.");
    }

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);
}

public sealed record AccountChoice(Guid Id, string Code, string Name, AccountNature Nature, bool IsGroup)
{
    public string Display => $"{Code} · {Name}";
}

public sealed partial class ChartOfAccountsViewModel(
    ChartOfAccountsService chart,
    IAuthorizer authorizer,
    ToastService toasts,
    ILogger<ChartOfAccountsViewModel> logger) : PageViewModel(logger, toasts), ILoadable
{
    public string Title => "Chart of accounts";

    public bool CanEdit => authorizer.Has(Permissions.AccountsManage);

    public ObservableCollection<AccountRow> Accounts { get; } = [];

    public ObservableCollection<AccountChoice> Groups { get; } = [];

    public IReadOnlyList<AccountKind> Kinds { get; } = Enum.GetValues<AccountKind>();

    [ObservableProperty] private AccountRow? _selected;
    [ObservableProperty] private string _code = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private AccountChoice? _parent;
    [ObservableProperty] private AccountKind _kind = AccountKind.General;
    [ObservableProperty] private bool _isExisting;

    public async Task LoadAsync() => await RunAsync(async () =>
    {
        var keep = Selected?.Id;
        Accounts.Clear();
        Groups.Clear();
        foreach (var row in await chart.ListAsync())
        {
            Accounts.Add(row);
            if (row.IsGroup) Groups.Add(new AccountChoice(row.Id, row.Code, row.Name, row.Nature, true));
        }

        Selected = Accounts.FirstOrDefault(a => a.Id == keep);
    });

    partial void OnSelectedChanged(AccountRow? value)
    {
        IsExisting = value is not null;
        if (value is null) return;
        (Code, Name, Kind) = (value.Code, value.Name, value.Kind);
        Parent = Groups.FirstOrDefault(g => g.Id == value.ParentId);
    }

    [RelayCommand]
    private void New()
    {
        var parent = Selected is { IsGroup: true } group ? Groups.FirstOrDefault(g => g.Id == group.Id) : Parent;
        Selected = null;
        (Code, Name, Kind, Parent) = (string.Empty, string.Empty, AccountKind.General, parent);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Parent is null)
        {
            Error = "Choose the group this account belongs to.";
            return;
        }

        if (await RunAsync(() => chart.SaveAsync(new AccountInput(Selected?.Id, Code, Name, Parent.Id, Kind)), "Account saved.")) await LoadAsync();
    }

    [RelayCommand]
    private async Task ToggleActiveAsync()
    {
        if (Selected is not { } account) return;
        if (await RunAsync(() => chart.SetActiveAsync(account.Id, !account.IsActive), account.IsActive ? "Account deactivated." : "Account reactivated."))
            await LoadAsync();
    }
}

/// <summary>One editable voucher line. Amounts are text so partial input does not fight the user.</summary>
public sealed partial class VoucherLineEditor : ObservableObject
{
    [ObservableProperty] private AccountChoice? _account;
    [ObservableProperty] private Guid? _costCentreId;
    [ObservableProperty] private string _debit = string.Empty;
    [ObservableProperty] private string _credit = string.Empty;
    [ObservableProperty] private string _narration = string.Empty;

    public decimal DebitValue => ParseAmount(Debit);

    public decimal CreditValue => ParseAmount(Credit);

    internal static decimal ParseAmount(string text) =>
        decimal.TryParse(text.Replace(",", string.Empty, StringComparison.Ordinal).Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0m;
}

public sealed partial class VouchersViewModel : PageViewModel, ILoadable
{
    private readonly VoucherService _vouchers;
    private readonly ChartOfAccountsService _chart;
    private readonly CostCentreService _centres;
    private readonly IAuthorizer _authorizer;
    private readonly DialogService _dialogs;

    public VouchersViewModel(VoucherService vouchers, ChartOfAccountsService chart, CostCentreService centres, IAuthorizer authorizer, DialogService dialogs,
        ToastService toasts, ILogger<VouchersViewModel> logger) : base(logger, toasts)
    {
        _vouchers = vouchers;
        _chart = chart;
        _centres = centres;
        _authorizer = authorizer;
        _dialogs = dialogs;
        Lines.CollectionChanged += (_, e) =>
        {
            foreach (VoucherLineEditor line in e.NewItems ?? Array.Empty<VoucherLineEditor>()) line.PropertyChanged += OnLineChanged;
            RecalculateTotals();
        };
        StartNew();
    }

    public string Title => "Vouchers";

    public bool CanCreate => _authorizer.Has(Permissions.VouchersCreate);

    public bool CanPost => _authorizer.Has(Permissions.VouchersPost);

    public bool CanReverse => _authorizer.Has(Permissions.VouchersReverse);

    public IReadOnlyList<VoucherType> EntryTypes { get; } =
        [VoucherType.Journal, VoucherType.Payment, VoucherType.Receipt, VoucherType.Contra, VoucherType.Sales, VoucherType.Purchase,
         VoucherType.Expense, VoucherType.Income, VoucherType.CreditNote, VoucherType.DebitNote];

    public ObservableCollection<VoucherSummary> Vouchers { get; } = [];

    public ObservableCollection<AccountChoice> LedgerAccounts { get; } = [];

    public ObservableCollection<VoucherLineEditor> Lines { get; } = [];

    public IReadOnlyList<Expenses.Choice> CostCentres { get; private set; } = [];

    [ObservableProperty] private VoucherSummary? _selected;
    [ObservableProperty] private Guid? _editingId;
    [ObservableProperty] private string? _editingNumber;
    [ObservableProperty] private VoucherType _type = VoucherType.Journal;
    [ObservableProperty] private string _date = DateText.From(DateText.Today);
    [ObservableProperty] private string _narration = string.Empty;
    [ObservableProperty] private bool _isReadOnly;
    [ObservableProperty] private string? _reversedBy;
    [ObservableProperty] private string _totalDebit = "0.00";
    [ObservableProperty] private string _totalCredit = "0.00";
    [ObservableProperty] private string? _difference;
    [ObservableProperty] private string _reversalDate = DateText.From(DateText.Today);

    public bool IsPostedVoucher => IsReadOnly && EditingNumber is not null;

    partial void OnIsReadOnlyChanged(bool value) => OnPropertyChanged(nameof(IsPostedVoucher));

    partial void OnEditingNumberChanged(string? value) => OnPropertyChanged(nameof(IsPostedVoucher));

    public async Task LoadAsync() => await RunAsync(async () =>
    {
        CostCentres = new[] { new Expenses.Choice(null, "(No centre)") }.Concat((await _centres.ListAsync()).Select(c => new Expenses.Choice(c.Id, c.Display))).ToList();
        OnPropertyChanged(nameof(CostCentres));
        LedgerAccounts.Clear();
        foreach (var row in (await _chart.ListAsync()).Where(r => !r.IsGroup && r.IsActive))
            LedgerAccounts.Add(new AccountChoice(row.Id, row.Code, row.Name, row.Nature, false));
        await RefreshListAsync();
    });

    partial void OnSelectedChanged(VoucherSummary? value)
    {
        if (value is not null) _ = OpenAsync(value.Id);
    }

    [RelayCommand]
    private void StartNew()
    {
        Selected = null;
        EditingId = null;
        EditingNumber = null;
        ReversedBy = null;
        IsReadOnly = !CanCreate;
        Narration = string.Empty;
        Date = DateText.From(DateText.Today);
        Lines.Clear();
        Lines.Add(new VoucherLineEditor());
        Lines.Add(new VoucherLineEditor());
    }

    [RelayCommand]
    private void AddLine()
    {
        if (!IsReadOnly) Lines.Add(new VoucherLineEditor());
    }

    [RelayCommand]
    private void RemoveLine(VoucherLineEditor line)
    {
        if (!IsReadOnly && Lines.Count > 2) Lines.Remove(line);
    }

    [RelayCommand]
    private async Task SaveDraftAsync()
    {
        Guid saved = Guid.Empty;
        if (await RunAsync(async () => saved = await _vouchers.SaveDraftAsync(BuildInput()), "Draft saved. It does not affect the ledger until posted."))
        {
            EditingId = saved;
            await RefreshListAsync();
        }
    }

    [RelayCommand]
    private async Task PostAsync()
    {
        string number = string.Empty;
        Guid id = Guid.Empty;
        if (await RunAsync(async () =>
            {
                id = await _vouchers.SaveDraftAsync(BuildInput());
                // Keep editing this draft: if posting fails, the next attempt updates it instead of creating another.
                EditingId = id;
                number = await _vouchers.PostAsync(id);
            }))
        {
            Toasts.Show($"Posted as {number}.", ToastKind.Success);
            await RefreshListAsync();
            await OpenAsync(id);
        }
        else if (EditingId is not null)
        {
            // The draft was saved before posting failed; show it in the list. Failure here must not hide the posting error.
            try
            {
                await RefreshListAsync();
            }
            catch (Exception)
            {
            }
        }
    }

    [RelayCommand]
    private async Task DeleteDraftAsync()
    {
        if (EditingId is not { } id || IsPostedVoucher) return;
        if (!await _dialogs.ConfirmAsync("Delete this draft?", "The draft voucher will be removed. Posted vouchers are not affected.", "Delete draft", true)) return;
        if (await RunAsync(() => _vouchers.DeleteDraftAsync(id), "Draft deleted."))
        {
            StartNew();
            await RefreshListAsync();
        }
    }

    [RelayCommand]
    private async Task ReverseAsync()
    {
        if (EditingId is not { } id || !IsPostedVoucher) return;
        if (!await _dialogs.ConfirmAsync($"Reverse voucher {EditingNumber}?",
                "A reversal voucher with debits and credits swapped will be posted on the reversal date. The original stays in the books, unchanged.",
                "Post reversal", true))
            return;
        string number = string.Empty;
        if (await RunAsync(async () => number = await _vouchers.ReverseAsync(id, DateText.Parse(ReversalDate, "Reversal date"), string.Empty)))
        {
            Toasts.Show($"Reversed with {number}.", ToastKind.Success);
            await RefreshListAsync();
            await OpenAsync(id);
        }
    }

    private async Task RefreshListAsync()
    {
        Vouchers.Clear();
        foreach (var voucher in await _vouchers.ListAsync(new VoucherQuery())) Vouchers.Add(voucher);
    }

    public async Task OpenAsync(Guid id) => await RunAsync(async () =>
    {
        var details = await _vouchers.GetAsync(id);
        EditingId = details.Id;
        EditingNumber = details.Number;
        Type = details.Type;
        Date = DateText.From(details.Date);
        Narration = details.Narration;
        ReversedBy = details.ReversedByNumber;
        IsReadOnly = details.Status == VoucherStatus.Posted || !CanCreate;
        Lines.Clear();
        foreach (var line in details.Lines)
        {
            Lines.Add(new VoucherLineEditor
            {
                Account = LedgerAccounts.FirstOrDefault(a => a.Id == line.AccountId) ?? new AccountChoice(line.AccountId, line.AccountCode, line.AccountName, AccountNature.Asset, false),
                Debit = line.Debit == 0 ? string.Empty : line.Debit.ToString("0.00", CultureInfo.InvariantCulture),
                Credit = line.Credit == 0 ? string.Empty : line.Credit.ToString("0.00", CultureInfo.InvariantCulture),
                Narration = line.Narration ?? string.Empty,
                CostCentreId = line.CostCentreId,
            });
        }
    });

    private VoucherInput BuildInput()
    {
        var lines = Lines.Where(l => l.Account is not null || l.DebitValue != 0 || l.CreditValue != 0)
            .Select((l, i) => l.Account is null
                ? throw new WorkspaceValidationException($"Line {i + 1}: choose an account.")
                : new VoucherLineInput(l.Account.Id, l.DebitValue, l.CreditValue, l.Narration, l.CostCentreId))
            .ToList();
        var editing = IsPostedVoucher ? null : EditingId;
        return new VoucherInput(editing, Type, DateText.Parse(Date, "Date"), Narration, lines);
    }

    private void OnLineChanged(object? sender, PropertyChangedEventArgs e) => RecalculateTotals();

    private void RecalculateTotals()
    {
        var debit = Lines.Sum(l => l.DebitValue);
        var credit = Lines.Sum(l => l.CreditValue);
        TotalDebit = IndianNumberFormat.Number(debit);
        TotalCredit = IndianNumberFormat.Number(credit);
        Difference = debit == credit ? null : $"Difference {IndianNumberFormat.Number(Math.Abs(debit - credit))}";
    }
}

public sealed partial class LedgerViewModel(
    LedgerService ledgers,
    ChartOfAccountsService chart,
    ToastService toasts,
    ILogger<LedgerViewModel> logger) : PageViewModel(logger, toasts), ILoadable
{
    public string Title => "Ledger";

    public ObservableCollection<AccountChoice> Accounts { get; } = [];

    public ObservableCollection<LedgerLine> Lines { get; } = [];

    [ObservableProperty] private AccountChoice? _account;
    [ObservableProperty] private string _from = DateText.From(FinancialYear.Containing(DateText.Today).StartDate);
    [ObservableProperty] private string _to = DateText.From(DateText.Today);
    [ObservableProperty] private string? _opening;
    [ObservableProperty] private string? _closing;

    public async Task LoadAsync() => await RunAsync(async () =>
    {
        Accounts.Clear();
        foreach (var row in (await chart.ListAsync()).Where(r => !r.IsGroup))
            Accounts.Add(new AccountChoice(row.Id, row.Code, row.Name, row.Nature, false));
        Account ??= Accounts.FirstOrDefault();
    });

    [RelayCommand]
    private async Task ShowAsync()
    {
        if (Account is null) return;
        await RunAsync(async () =>
        {
            var statement = await ledgers.StatementAsync(Account.Id, DateText.Parse(From, "From"), DateText.Parse(To, "To"));
            Lines.Clear();
            foreach (var line in statement.Lines) Lines.Add(line);
            Opening = Balance(statement.OpeningBalance);
            Closing = Balance(statement.ClosingBalance);
        });
    }

    internal static string Balance(decimal net) => net switch
    {
        > 0 => $"{IndianNumberFormat.Number(net)} Dr",
        < 0 => $"{IndianNumberFormat.Number(-net)} Cr",
        _ => "0.00",
    };
}

public sealed partial class TrialBalanceViewModel(
    LedgerService ledgers,
    ToastService toasts,
    ILogger<TrialBalanceViewModel> logger) : PageViewModel(logger, toasts), ILoadable
{
    public string Title => "Trial balance";

    public ObservableCollection<AccountBalance> Rows { get; } = [];

    [ObservableProperty] private string _asOf = DateText.From(DateText.Today);
    [ObservableProperty] private string _totalDebit = "0.00";
    [ObservableProperty] private string _totalCredit = "0.00";
    [ObservableProperty] private bool _isBalanced = true;

    public Task LoadAsync() => ShowAsync();

    [RelayCommand]
    private async Task ShowAsync() => await RunAsync(async () =>
    {
        var trial = await ledgers.TrialBalanceAsync(DateText.Parse(AsOf, "As of"));
        Rows.Clear();
        foreach (var row in trial.Rows) Rows.Add(row);
        TotalDebit = IndianNumberFormat.Number(trial.TotalDebit);
        TotalCredit = IndianNumberFormat.Number(trial.TotalCredit);
        IsBalanced = trial.IsBalanced;
    });
}

public sealed partial class StatementsViewModel(
    LedgerService ledgers,
    ToastService toasts,
    ILogger<StatementsViewModel> logger) : PageViewModel(logger, toasts), ILoadable
{
    public string Title => "Financial statements";

    [ObservableProperty] private string _from = DateText.From(FinancialYear.Containing(DateText.Today).StartDate);
    [ObservableProperty] private string _to = DateText.From(DateText.Today);
    [ObservableProperty] private ProfitAndLoss? _profitAndLoss;
    [ObservableProperty] private BalanceSheet? _balanceSheet;
    [ObservableProperty] private string? _netProfit;
    [ObservableProperty] private string? _assetsTotal;
    [ObservableProperty] private string? _liabilitiesTotal;

    public Task LoadAsync() => ShowAsync();

    [RelayCommand]
    private async Task ShowAsync() => await RunAsync(async () =>
    {
        var to = DateText.Parse(To, "To");
        ProfitAndLoss = await ledgers.ProfitAndLossAsync(DateText.Parse(From, "From"), to);
        BalanceSheet = await ledgers.BalanceSheetAsync(to);
        NetProfit = IndianNumberFormat.Number(ProfitAndLoss.NetProfit);
        AssetsTotal = IndianNumberFormat.Number(BalanceSheet.TotalAssets);
        LiabilitiesTotal = IndianNumberFormat.Number(BalanceSheet.TotalLiabilitiesAndEquity);
    });
}

public sealed partial class OpeningBalanceRow : ObservableObject
{
    public required AccountChoice Account { get; init; }

    [ObservableProperty] private string _debit = string.Empty;
    [ObservableProperty] private string _credit = string.Empty;
}

public sealed partial class OpeningBalancesViewModel(
    VoucherService vouchers,
    ChartOfAccountsService chart,
    LedgerService ledgers,
    DialogService dialogs,
    ToastService toasts,
    ILogger<OpeningBalancesViewModel> logger) : PageViewModel(logger, toasts), ILoadable
{
    public string Title => "Opening balances";

    public ObservableCollection<OpeningBalanceRow> Rows { get; } = [];

    public ObservableCollection<FinancialYearRow> Years { get; } = [];

    [ObservableProperty] private FinancialYearRow? _year;

    public async Task LoadAsync() => await RunAsync(async () =>
    {
        Years.Clear();
        foreach (var year in (await ledgers.ListYearsAsync()).Where(y => !y.IsClosed)) Years.Add(year);
        Year ??= Years.LastOrDefault();
        Rows.Clear();
        // Inventory is excluded: opening stock is entered under Stock (Opening stock), so quantities and value stay together.
        foreach (var row in (await chart.ListAsync()).Where(r => !r.IsGroup && r.IsActive && r.Nature.IsBalanceSheet()
                     && r.Kind != AccountKind.Inventory
                     && r.SystemKey != SystemAccounts.OpeningBalanceDifference && r.SystemKey != SystemAccounts.RetainedEarnings))
            Rows.Add(new OpeningBalanceRow { Account = new AccountChoice(row.Id, row.Code, row.Name, row.Nature, false) });
    });

    [RelayCommand]
    private async Task PostAsync()
    {
        if (Year is null) return;
        var lines = Rows
            .Select(r => new VoucherLineInput(r.Account.Id, VoucherLineEditor.ParseAmount(r.Debit), VoucherLineEditor.ParseAmount(r.Credit)))
            .Where(l => l.Debit != 0 || l.Credit != 0)
            .ToList();
        if (lines.Count == 0)
        {
            Error = "Enter at least one opening balance.";
            return;
        }

        if (!await dialogs.ConfirmAsync($"Post opening balances for FY {Year.Label}?",
                "They are posted as one opening voucher dated 1 April. Any difference goes to Opening Balance Difference. Corrections are made by reversing the voucher.",
                "Post opening balances"))
            return;
        string number = string.Empty;
        if (await RunAsync(async () => number = await vouchers.PostOpeningBalancesAsync(Year.StartYear, lines)))
        {
            Toasts.Show($"Opening balances posted as {number}.", ToastKind.Success);
            await LoadAsync();
        }
    }
}

public sealed partial class FinancialYearsViewModel(
    LedgerService ledgers,
    IAuthorizer authorizer,
    DialogService dialogs,
    ToastService toasts,
    ILogger<FinancialYearsViewModel> logger) : PageViewModel(logger, toasts), ILoadable
{
    public bool CanManage => authorizer.Has(Permissions.FinancialYearsManage);

    public ObservableCollection<FinancialYearRow> Years { get; } = [];

    [ObservableProperty] private FinancialYearRow? _selected;

    public async Task LoadAsync() => await RunAsync(async () =>
    {
        Years.Clear();
        foreach (var year in await ledgers.ListYearsAsync()) Years.Add(year);
    });

    [RelayCommand]
    private async Task OpenNextAsync()
    {
        int opened = 0;
        if (await RunAsync(async () => opened = await ledgers.OpenNextYearAsync()))
        {
            Toasts.Show($"FY {new FinancialYear(opened).Label} opened. Balances carry forward automatically.", ToastKind.Success);
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task CloseAsync()
    {
        if (Selected is not { IsClosed: false } year) return;
        if (!await dialogs.ConfirmAsync($"Close FY {year.Label}?",
                "Nothing can be posted or reversed into a closed year. An authorised user can reopen it later; that is recorded in the audit trail.",
                "Close year", true))
            return;
        if (await RunAsync(() => ledgers.CloseYearAsync(year.StartYear), $"FY {year.Label} closed.")) await LoadAsync();
    }

    [RelayCommand]
    private async Task ReopenAsync()
    {
        if (Selected is not { IsClosed: true } year) return;
        if (await RunAsync(() => ledgers.ReopenYearAsync(year.StartYear), $"FY {year.Label} reopened.")) await LoadAsync();
    }
}
