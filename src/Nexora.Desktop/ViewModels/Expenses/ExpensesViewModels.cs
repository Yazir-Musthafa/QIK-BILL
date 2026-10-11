using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Nexora.Application;
using Nexora.Application.Billing;
using Nexora.Application.Companies;
using Nexora.Application.Crm;
using Nexora.Application.Expenses;
using Nexora.Application.Inventory;
using Nexora.Application.Security;
using Nexora.Domain.Billing;
using Nexora.Domain.Expenses;
using Nexora.Domain.Security;
using Nexora.Desktop.Services;
using Nexora.Desktop.ViewModels.Accounting;
using Nexora.Desktop.ViewModels.Billing;
using Nexora.Desktop.ViewModels.Inventory;
using Nexora.Desktop.ViewModels.Settings;

namespace Nexora.Desktop.ViewModels.Expenses;

/// <summary>An optional choice in a list; the first entry of such lists has no id and reads "(none)" or "(all)".</summary>
public sealed record Choice(Guid? Id, string Name);

public sealed record ExpenseSection(string Key, string Label);

/// <summary>One editable expense line; the amount stays text so typing is not interrupted.</summary>
public sealed partial class ExpenseLineEditor : ObservableObject
{
    [ObservableProperty] private ExpenseCategoryRow? _category;
    [ObservableProperty] private string _amount = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private Choice? _tax;
    [ObservableProperty] private bool _eligible = true;
}

/// <summary>
/// Expenses: list with filters and totals, detail with approval actions and attachments, the editor, categories,
/// recurring templates, totals by category, branch, department, method or month, and the self-approval setting.
/// All rules (approval, posting, attachment checks) are enforced by the expense service. UI unverified at runtime.
/// </summary>
public sealed partial class ExpensesViewModel : PageViewModel, ILoadable
{
    private readonly ExpenseService _expenses;
    private readonly SettlementService _settlements;
    private readonly PartyService _parties;
    private readonly CompanyService _companies;
    private readonly CrmService _crm;
    private readonly InventoryCatalogueService _catalogue;
    private readonly IAuthorizer _authorizer;
    private readonly DialogService _dialogs;

    public ExpensesViewModel(ExpenseService expenses, SettlementService settlements, PartyService parties, CompanyService companies, CrmService crm,
        InventoryCatalogueService catalogue, IAuthorizer authorizer, DialogService dialogs, ToastService toasts, ILogger<ExpensesViewModel> logger) : base(logger, toasts)
    {
        _expenses = expenses;
        _settlements = settlements;
        _parties = parties;
        _companies = companies;
        _crm = crm;
        _catalogue = catalogue;
        _authorizer = authorizer;
        _dialogs = dialogs;
        _section = Sections[0];
        StartExpense();
    }

    public string Title => "Expenses";

    public bool CanManage => _authorizer.Has(Permissions.ExpensesManage);

    public bool CanApprove => _authorizer.Has(Permissions.ExpensesApprove);

    public IReadOnlyList<ExpenseSection> Sections { get; } =
    [
        new("list", "Expenses"),
        new("editor", "Expense editor"),
        new("categories", "Categories"),
        new("recurring", "Recurring"),
        new("totals", "Totals"),
        new("settings", "Settings"),
    ];

    public IReadOnlyList<string> StatusFilters { get; } = ["All", "Draft", "Pending approval", "Approved", "Rejected", "Posted"];

    public IReadOnlyList<PaymentMethod> Methods { get; } = Enum.GetValues<PaymentMethod>();

    public IReadOnlyList<RecurrenceFrequency> Frequencies { get; } = Enum.GetValues<RecurrenceFrequency>();

    public IReadOnlyList<ExpenseGrouping> Groupings { get; } = Enum.GetValues<ExpenseGrouping>();

    public ObservableCollection<ExpenseRow> Rows { get; } = [];

    public ObservableCollection<ExpenseCategoryRow> Categories { get; } = [];

    public ObservableCollection<Choice> CategoryFilters { get; } = [];

    public ObservableCollection<CashAccountChoice> CashAccounts { get; } = [];

    public ObservableCollection<Choice> Suppliers { get; } = [];

    public ObservableCollection<Choice> Employees { get; } = [];

    public ObservableCollection<Choice> Branches { get; } = [];

    public ObservableCollection<Choice> TaxCategories { get; } = [];

    public ObservableCollection<Choice> LedgerChoices { get; } = [];

    public ObservableCollection<Choice> GroupChoices { get; } = [];

    public ObservableCollection<ExpenseLineEditor> Lines { get; } = [];

    public ObservableCollection<RecurringExpenseRow> Recurring { get; } = [];

    public ObservableCollection<ExpenseTotalRow> Totals { get; } = [];

    [ObservableProperty] private ExpenseSection _section;

    // List and filters
    [ObservableProperty] private string _fromDate = string.Empty;
    [ObservableProperty] private string _toDate = string.Empty;
    [ObservableProperty] private string _statusFilter = "All";
    [ObservableProperty] private Choice? _categoryFilter;
    [ObservableProperty] private string _departmentFilter = string.Empty;
    [ObservableProperty] private ExpenseRow? _selected;
    [ObservableProperty] private ExpenseView? _detail;
    [ObservableProperty] private string _reason = string.Empty;
    [ObservableProperty] private string _actionDate = DateText.From(DateText.Today);
    [ObservableProperty] private AttachmentRow? _selectedAttachment;

    // Editor
    [ObservableProperty] private Guid? _editingId;
    [ObservableProperty] private string _date = DateText.From(DateText.Today);
    [ObservableProperty] private CashAccountChoice? _paidFrom;
    [ObservableProperty] private PaymentMethod _method = PaymentMethod.Cash;
    [ObservableProperty] private Choice? _supplier;
    [ObservableProperty] private Choice? _employee;
    [ObservableProperty] private string _employeeName = string.Empty;
    [ObservableProperty] private Choice? _branch;
    [ObservableProperty] private string _department = string.Empty;
    [ObservableProperty] private string _reference = string.Empty;
    [ObservableProperty] private string _narration = string.Empty;
    [ObservableProperty] private string _editorHeading = "New expense";

    // Categories
    [ObservableProperty] private ExpenseCategoryRow? _selectedCategory;
    [ObservableProperty] private string _categoryName = string.Empty;
    [ObservableProperty] private Choice? _categoryLedger;
    [ObservableProperty] private bool _createLedger;
    [ObservableProperty] private Choice? _ledgerGroup;
    [ObservableProperty] private Choice? _categoryTax;

    // Recurring
    [ObservableProperty] private RecurringExpenseRow? _selectedRecurring;
    [ObservableProperty] private string _recurringName = string.Empty;
    [ObservableProperty] private ExpenseCategoryRow? _recurringCategory;
    [ObservableProperty] private string _recurringAmount = string.Empty;
    [ObservableProperty] private RecurrenceFrequency _recurringFrequency = RecurrenceFrequency.Monthly;
    [ObservableProperty] private string _recurringNextDue = DateText.From(DateText.Today);
    [ObservableProperty] private string _recurringEnd = string.Empty;
    [ObservableProperty] private CashAccountChoice? _recurringPaidFrom;
    [ObservableProperty] private Choice? _recurringTax;
    [ObservableProperty] private string _generateUpTo = DateText.From(DateText.Today);

    // Totals and settings
    [ObservableProperty] private ExpenseGrouping _grouping = ExpenseGrouping.Category;
    [ObservableProperty] private bool _allowSelfApproval;

    public bool ShowList => Section.Key == "list";

    public bool ShowEditor => Section.Key == "editor";

    public bool ShowCategories => Section.Key == "categories";

    public bool ShowRecurring => Section.Key == "recurring";

    public bool ShowTotals => Section.Key == "totals";

    public bool ShowSettings => Section.Key == "settings";

    public bool HasDetail => Detail is not null;

    public bool DetailEditable => CanManage && Detail is { Status: ExpenseStatus.Draft or ExpenseStatus.Rejected };

    public bool DetailCanDecide => Detail is { CanApprove: true };

    public bool DetailCanPost => CanManage && Detail is { Status: ExpenseStatus.Approved };

    public bool DetailCanReverse => CanManage && Detail is { Status: ExpenseStatus.Posted, IsReversed: false };

    partial void OnSectionChanged(ExpenseSection value)
    {
        foreach (var name in new[] { nameof(ShowList), nameof(ShowEditor), nameof(ShowCategories), nameof(ShowRecurring), nameof(ShowTotals), nameof(ShowSettings) })
            OnPropertyChanged(name);
        _ = LoadAsync();
    }

    partial void OnDetailChanged(ExpenseView? value)
    {
        foreach (var name in new[] { nameof(HasDetail), nameof(DetailEditable), nameof(DetailCanDecide), nameof(DetailCanPost), nameof(DetailCanReverse) }) OnPropertyChanged(name);
    }

    partial void OnSelectedChanged(ExpenseRow? value) => _ = LoadDetailAsync(value?.Id);

    partial void OnSelectedCategoryChanged(ExpenseCategoryRow? value)
    {
        if (value is null) return;
        CategoryName = value.Name;
        CategoryLedger = LedgerChoices.FirstOrDefault(l => l.Id == value.AccountId);
        CategoryTax = TaxCategories.FirstOrDefault(t => t.Id == value.DefaultTaxCategoryId) ?? TaxCategories.FirstOrDefault();
        CreateLedger = false;
    }

    partial void OnSelectedRecurringChanged(RecurringExpenseRow? value)
    {
        if (value is null) return;
        (RecurringName, RecurringAmount, RecurringFrequency) = (value.Name, NumberText.Format(value.Amount), value.Frequency);
        RecurringCategory = Categories.FirstOrDefault(c => c.Id == value.CategoryId);
        RecurringNextDue = DateText.From(value.NextDueDate);
        RecurringEnd = value.EndDate is { } end ? DateText.From(end) : string.Empty;
        RecurringPaidFrom = CashAccounts.FirstOrDefault(a => a.Id == value.PaidFromAccountId);
        RecurringTax = TaxCategories.FirstOrDefault(t => t.Id == value.TaxCategoryId) ?? TaxCategories.FirstOrDefault();
    }

    public async Task LoadAsync() => await RunAsync(async () =>
    {
        if (CashAccounts.Count == 0) await LoadReferenceDataAsync();
        switch (Section.Key)
        {
            case "list":
                await LoadRowsAsync();
                break;
            case "categories":
                await ReloadCategoriesAsync();
                break;
            case "recurring":
                Recurring.Clear();
                foreach (var row in await _expenses.RecurringAsync()) Recurring.Add(row);
                break;
            case "totals":
                Totals.Clear();
                foreach (var row in await _expenses.TotalsAsync(Filter(), Grouping)) Totals.Add(row);
                break;
            case "settings":
                AllowSelfApproval = await _expenses.AllowSelfApprovalAsync();
                break;
        }
    });

    private async Task LoadReferenceDataAsync()
    {
        foreach (var (id, name) in await _settlements.CashAndBankAccountsAsync()) CashAccounts.Add(new CashAccountChoice(id, name));
        Suppliers.Add(new Choice(null, "(none)"));
        if (_authorizer.Has(Permissions.PurchasesView))
            foreach (var p in await _parties.ListAsync(PartyKind.Supplier)) Suppliers.Add(new Choice(p.Id, p.Name));
        Employees.Add(new Choice(null, "(none)"));
        foreach (var m in await _crm.MembersAsync()) Employees.Add(new Choice(m.UserId, m.DisplayName));
        Branches.Add(new Choice(null, "(none)"));
        if (_authorizer.Has(Permissions.CompanyView))
            foreach (var b in (await _companies.ListBranchesAsync()).Where(b => b.IsActive)) Branches.Add(new Choice(b.Id, b.Name));
        TaxCategories.Add(new Choice(null, "(no GST)"));
        foreach (var t in (await _catalogue.ListTaxCategoriesAsync()).Where(t => t.IsActive)) TaxCategories.Add(new Choice(t.Id, t.Name));
        foreach (var (id, name, isGroup) in await _expenses.ExpenseAccountsAsync())
        {
            if (isGroup) GroupChoices.Add(new Choice(id, name));
            else LedgerChoices.Add(new Choice(id, name));
        }

        await ReloadCategoriesAsync();
        PaidFrom ??= CashAccounts.FirstOrDefault();
        RecurringPaidFrom ??= CashAccounts.FirstOrDefault();
        (Supplier, Employee, Branch) = (Suppliers[0], Employees[0], Branches[0]);
        (CategoryTax, RecurringTax) = (TaxCategories[0], TaxCategories[0]);
        LedgerGroup = GroupChoices.FirstOrDefault(g => g.Name.EndsWith("Indirect Expenses", StringComparison.Ordinal)) ?? GroupChoices.FirstOrDefault();
    }

    private async Task ReloadCategoriesAsync()
    {
        var keepFilter = CategoryFilter?.Id;
        Categories.Clear();
        foreach (var c in await _expenses.CategoriesAsync()) Categories.Add(c);
        CategoryFilters.Clear();
        CategoryFilters.Add(new Choice(null, "(all)"));
        foreach (var c in Categories) CategoryFilters.Add(new Choice(c.Id, c.Name));
        CategoryFilter = CategoryFilters.FirstOrDefault(c => c.Id == keepFilter) ?? CategoryFilters[0];
    }

    private ExpenseFilter Filter()
    {
        DateOnly? Parse(string text, string field) => string.IsNullOrWhiteSpace(text) ? null : DateText.Parse(text, field);
        ExpenseStatus? status = StatusFilter switch
        {
            "Draft" => ExpenseStatus.Draft,
            "Pending approval" => ExpenseStatus.PendingApproval,
            "Approved" => ExpenseStatus.Approved,
            "Rejected" => ExpenseStatus.Rejected,
            "Posted" => ExpenseStatus.Posted,
            _ => null,
        };
        return new ExpenseFilter(Parse(FromDate, "From"), Parse(ToDate, "To"), status, CategoryFilter?.Id, null, DepartmentFilter);
    }

    private async Task LoadRowsAsync()
    {
        var keep = Selected?.Id;
        Rows.Clear();
        foreach (var row in await _expenses.ListAsync(Filter())) Rows.Add(row);
        Selected = Rows.FirstOrDefault(r => r.Id == keep);
    }

    private async Task LoadDetailAsync(Guid? id)
    {
        if (id is not { } expenseId)
        {
            Detail = null;
            return;
        }

        await RunAsync(async () => Detail = await _expenses.GetAsync(expenseId));
    }

    [RelayCommand]
    private Task ApplyFilterAsync() => LoadAsync();

    // ----- Approval and posting -----

    private async Task ActAsync(Func<Guid, Task> action, string success)
    {
        if (Detail is not { } d) return;
        if (await RunAsync(() => action(d.Id), success))
        {
            Reason = string.Empty;
            await LoadAsync();
            await LoadDetailAsync(d.Id);
        }
    }

    [RelayCommand]
    private Task SubmitAsync() => ActAsync(id => _expenses.SubmitAsync(id), "Submitted for approval.");

    [RelayCommand]
    private Task ApproveAsync() => ActAsync(id => _expenses.ApproveAsync(id), "Approved.");

    [RelayCommand]
    private Task RejectAsync() => ActAsync(id => _expenses.RejectAsync(id, Reason), "Rejected and sent back.");

    [RelayCommand]
    private async Task PostAsync()
    {
        if (Detail is not { } d) return;
        if (!await _dialogs.ConfirmAsync($"Post this expense of {Application.Formatting.IndianNumberFormat.Number(d.Total)}?",
                "The accounting voucher is posted and the expense can no longer be changed (only reversed).", "Post"))
            return;
        await ActAsync(id => _expenses.PostAsync(id), "Expense posted.");
    }

    [RelayCommand]
    private async Task ReverseAsync()
    {
        if (Detail is not { } d) return;
        if (!await _dialogs.ConfirmAsync($"Reverse expense {d.Number}?", "A reversal voucher undoes its accounting. The expense stays in the list, marked Reversed.", "Reverse", true))
            return;
        await ActAsync(id => _expenses.ReverseAsync(id, DateText.Parse(ActionDate, "Reversal date"), Reason), "Expense reversed.");
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Detail is not { } d) return;
        if (!await _dialogs.ConfirmAsync("Delete this draft?", "It has no number and no accounting effect.", "Delete", true)) return;
        if (await RunAsync(() => _expenses.DeleteDraftAsync(d.Id), "Draft deleted."))
        {
            Detail = null;
            await LoadAsync();
        }
    }

    // ----- Attachments -----

    [RelayCommand]
    private async Task AddAttachmentAsync()
    {
        if (Detail is not { } d) return;
        var dialog = new OpenFileDialog { Filter = "Receipts (*.pdf;*.png;*.jpg;*.jpeg)|*.pdf;*.png;*.jpg;*.jpeg", Multiselect = false };
        if (dialog.ShowDialog() != true) return;
        if (await RunAsync(async () =>
            {
                var info = new FileInfo(dialog.FileName);
                if (info.Length > ExpenseAttachment.MaxBytes) throw new WorkspaceValidationException("Attachments can be at most 5 MB.");
                await _expenses.AddAttachmentAsync(d.Id, info.Name, await File.ReadAllBytesAsync(dialog.FileName));
            }, "Attachment added."))
            await LoadDetailAsync(d.Id);
    }

    [RelayCommand]
    private async Task OpenAttachmentAsync()
    {
        if (SelectedAttachment is not { } file) return;
        await RunAsync(async () =>
        {
            var (name, _, content) = await _expenses.AttachmentAsync(file.Id);
            var folder = Path.Combine(Path.GetTempPath(), "QIKSuite", "attachments");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{file.Id:N}-{name}");
            await File.WriteAllBytesAsync(path, content);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        });
    }

    [RelayCommand]
    private async Task RemoveAttachmentAsync()
    {
        if (SelectedAttachment is not { } file || Detail is not { } d) return;
        if (await RunAsync(() => _expenses.RemoveAttachmentAsync(file.Id), "Attachment removed.")) await LoadDetailAsync(d.Id);
    }

    // ----- Editor -----

    [RelayCommand]
    private void StartExpense()
    {
        (EditingId, EditorHeading) = (null, "New expense");
        (Reference, Narration, Department, EmployeeName) = (string.Empty, string.Empty, string.Empty, string.Empty);
        Lines.Clear();
        Lines.Add(new ExpenseLineEditor());
    }

    [RelayCommand]
    private void AddLine() => Lines.Add(new ExpenseLineEditor { Tax = TaxCategories.FirstOrDefault() });

    [RelayCommand]
    private void RemoveLine(ExpenseLineEditor line)
    {
        if (Lines.Count > 1) Lines.Remove(line);
    }

    [RelayCommand]
    private async Task EditSelectedAsync()
    {
        if (Detail is not { } d || !DetailEditable) return;
        (EditingId, EditorHeading) = (d.Id, "Edit expense");
        Date = DateText.From(d.Date);
        PaidFrom = CashAccounts.FirstOrDefault(a => a.Id == d.PaidFromAccountId) ?? PaidFrom;
        Method = d.Method;
        Supplier = Suppliers.FirstOrDefault(s => s.Id == d.SupplierId) ?? Suppliers.FirstOrDefault();
        Employee = Employees.FirstOrDefault(e => e.Id == d.EmployeeUserId) ?? Employees.FirstOrDefault();
        EmployeeName = d.EmployeeUserId is null ? d.EmployeeName : string.Empty;
        Branch = Branches.FirstOrDefault(b => b.Id == d.BranchId) ?? Branches.FirstOrDefault();
        (Department, Reference, Narration) = (d.Department, d.Reference, d.Narration);
        Lines.Clear();
        foreach (var l in d.Lines)
            Lines.Add(new ExpenseLineEditor
            {
                Category = Categories.FirstOrDefault(c => c.Id == l.CategoryId),
                Amount = NumberText.Format(l.Amount),
                Description = l.Description,
                Tax = TaxCategories.FirstOrDefault(t => t.Id == l.TaxCategoryId) ?? TaxCategories.FirstOrDefault(),
                Eligible = l.InputTaxEligible,
            });
        Section = Sections[1];
        await Task.CompletedTask;
    }

    private async Task<Guid> SaveCoreAsync()
    {
        if (PaidFrom is null) throw new WorkspaceValidationException("Choose the cash or bank account.");
        var lines = Lines.Where(l => l.Category is not null || !string.IsNullOrWhiteSpace(l.Amount))
            .Select((l, i) => new ExpenseLineInput(l.Category?.Id ?? throw new WorkspaceValidationException($"Line {i + 1}: choose a category."),
                NumberText.Parse(l.Amount, $"Line {i + 1} amount"), l.Description, l.Tax?.Id, l.Eligible))
            .ToList();
        var id = await _expenses.SaveDraftAsync(new ExpenseInput(EditingId, DateText.Parse(Date, "Date"), PaidFrom.Id, lines, Method, Supplier?.Id, Employee?.Id,
            EmployeeName, Branch?.Id, Department, Reference, Narration));
        EditingId = id;
        return id;
    }

    [RelayCommand]
    private async Task SaveDraftAsync()
    {
        if (await RunAsync(async () => await SaveCoreAsync(), "Draft saved. GST and totals were recalculated."))
            await LoadDetailAsync(EditingId);
    }

    [RelayCommand]
    private async Task SaveAndSubmitAsync()
    {
        if (await RunAsync(async () => await _expenses.SubmitAsync(await SaveCoreAsync()), "Saved and submitted for approval."))
        {
            StartExpense();
            Section = Sections[0];
        }
    }

    // ----- Categories -----

    [RelayCommand]
    private void NewCategory()
    {
        SelectedCategory = null;
        (CategoryName, CreateLedger) = (string.Empty, false);
        CategoryLedger = null;
        CategoryTax = TaxCategories.FirstOrDefault();
    }

    [RelayCommand]
    private async Task SaveCategoryAsync()
    {
        if (await RunAsync(() => _expenses.SaveCategoryAsync(new ExpenseCategoryInput(SelectedCategory?.Id, CategoryName, CreateLedger ? null : CategoryLedger?.Id,
                CreateLedger ? LedgerGroup?.Id : null, CategoryTax?.Id)), "Category saved."))
        {
            LedgerChoices.Clear();
            GroupChoices.Clear();
            foreach (var (id, name, isGroup) in await _expenses.ExpenseAccountsAsync())
            {
                if (isGroup) GroupChoices.Add(new Choice(id, name));
                else LedgerChoices.Add(new Choice(id, name));
            }

            await ReloadCategoriesAsync();
        }
    }

    [RelayCommand]
    private async Task ToggleCategoryAsync()
    {
        if (SelectedCategory is not { } c) return;
        if (await RunAsync(() => _expenses.SetCategoryActiveAsync(c.Id, !c.IsActive), c.IsActive ? "Category deactivated." : "Category reactivated.")) await ReloadCategoriesAsync();
    }

    // ----- Recurring -----

    [RelayCommand]
    private void NewRecurring()
    {
        SelectedRecurring = null;
        (RecurringName, RecurringAmount, RecurringEnd) = (string.Empty, string.Empty, string.Empty);
        RecurringNextDue = DateText.From(DateText.Today);
    }

    [RelayCommand]
    private async Task SaveRecurringAsync()
    {
        if (await RunAsync(async () =>
            {
                if (RecurringCategory is null) throw new WorkspaceValidationException("Choose the category.");
                if (RecurringPaidFrom is null) throw new WorkspaceValidationException("Choose the cash or bank account.");
                await _expenses.SaveRecurringAsync(new RecurringExpenseInput(SelectedRecurring?.Id, RecurringName, RecurringCategory.Id, NumberText.Parse(RecurringAmount, "Amount"),
                    RecurringFrequency, DateText.Parse(RecurringNextDue, "Next due"), RecurringPaidFrom.Id, TaxCategoryId: RecurringTax?.Id,
                    EndDate: string.IsNullOrWhiteSpace(RecurringEnd) ? null : DateText.Parse(RecurringEnd, "End date")));
            }, "Recurring expense saved."))
            await LoadAsync();
    }

    [RelayCommand]
    private async Task ToggleRecurringAsync()
    {
        if (SelectedRecurring is not { } r) return;
        if (await RunAsync(() => _expenses.SetRecurringActiveAsync(r.Id, !r.IsActive), r.IsActive ? "Paused." : "Resumed.")) await LoadAsync();
    }

    [RelayCommand]
    private async Task GenerateDueAsync()
    {
        IReadOnlyList<Guid> created = [];
        if (await RunAsync(async () => created = await _expenses.GenerateDueAsync(DateText.Parse(GenerateUpTo, "Up to"))))
        {
            Toasts.Show(created.Count == 0 ? "Nothing is due." : $"Created {created.Count} draft(s). They still need approval and posting.", ToastKind.Success);
            await LoadAsync();
        }
    }

    // ----- Totals and settings -----

    [RelayCommand]
    private Task RefreshTotalsAsync() => LoadAsync();

    [RelayCommand]
    private async Task SaveSettingsAsync() =>
        await RunAsync(() => _expenses.SetAllowSelfApprovalAsync(AllowSelfApproval), AllowSelfApproval ? "People may now approve their own expenses." : "Self-approval is off.");
}
