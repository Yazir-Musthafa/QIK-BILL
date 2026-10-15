using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexora.Application;
using Nexora.Application.Audit;
using Nexora.Application.Companies;
using Nexora.Application.Security;
using Nexora.Application.Users;
using Nexora.Domain.Audit;
using Nexora.Domain.Companies;
using Nexora.Domain.Security;
using Nexora.Desktop.Services;

namespace Nexora.Desktop.ViewModels.Settings;

public sealed record SettingsSection(string Key, string Label, string Glyph, string? Permission, Type ViewModelType);

/// <summary>Settings host: a section list on the left, the selected section on the right.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private readonly ShellContext _shell;

    public SettingsViewModel(IServiceProvider services, IAuthorizer authorizer, ShellContext shell)
    {
        _services = services;
        _shell = shell;
        Sections = AllSections.Where(s => s.Permission is null || authorizer.Has(s.Permission)).ToList();
        SelectedSection = Sections[0];
    }

    public static IReadOnlyList<SettingsSection> AllSections { get; } =
    [
        new("general", "General", "", null, typeof(GeneralSettingsViewModel)),
        new("profile", "Company profile", "", Permissions.CompanyView, typeof(CompanyProfileViewModel)),
        new("locations", "Branches and warehouses", "", Permissions.CompanyView, typeof(LocationsViewModel)),
        new("numbering", "Document numbering", "", Permissions.CompanyView, typeof(NumberingViewModel)),
        new("users", "Users", "", Permissions.UsersView, typeof(UsersViewModel)),
        new("roles", "Roles and permissions", "", Permissions.UsersView, typeof(RolesViewModel)),
        new("inventory", "Inventory", "\uE7B8", Permissions.InventoryView, typeof(Inventory.InventorySettingsViewModel)),
        new("years", "Financial years", "\uE787", Permissions.AccountsView, typeof(Accounting.FinancialYearsViewModel)),
        new("transfer", "Import/Export", "\uE8B5", null, typeof(ImportExportViewModel)),
        new("backup", "Backup", "\uE74E", Permissions.BackupManage, typeof(BackupViewModel)),
        new("audit", "Audit trail", "", Permissions.AuditView, typeof(AuditViewModel)),
    ];

    public string Title => "Settings";

    public IReadOnlyList<SettingsSection> Sections { get; }

    [ObservableProperty]
    private SettingsSection _selectedSection;

    [ObservableProperty]
    private object? _current;

    partial void OnSelectedSectionChanged(SettingsSection value)
    {
        var wantsShell = value.ViewModelType.GetConstructors().Single().GetParameters().Any(p => p.ParameterType == typeof(ShellContext));
        var page = ActivatorUtilities.CreateInstance(_services, value.ViewModelType, wantsShell ? [_shell] : []);
        Current = page;
        if (page is ILoadable loadable) _ = loadable.LoadAsync();
    }

    public void SyncTheme(bool dark)
    {
        if (Current is GeneralSettingsViewModel general) general.SyncTheme(dark);
    }
}

/// <summary>A page that loads its data after it is shown.</summary>
public interface ILoadable
{
    Task LoadAsync();
}

/// <summary>What settings pages need from the shell: theme switching and company list refresh.</summary>
public sealed record ShellContext(Func<string> CurrentTheme, Action<string> ApplyTheme, Func<Task> CompaniesChanged, Action<string> Navigate);

public sealed partial class GeneralSettingsViewModel(
    IWorkspaceService workspace,
    CompanyService companies,
    IAuthorizer authorizer,
    DialogService dialogs,
    ToastService toasts,
    ILogger<GeneralSettingsViewModel> logger,
    ShellContext shell) : PageViewModel(logger, toasts), ILoadable
{
    private bool _syncing;

    public ObservableCollection<CompanySummary> Companies { get; } = [];

    public IReadOnlyList<IndianState> States { get; } = IndianState.All;

    public IReadOnlyList<FinancialYearOption> FinancialYears { get; } = Enumerable
        .Range(FinancialYear.Containing(DateOnly.FromDateTime(DateTime.Today)).StartYear - 5, 7).Reverse()
        .Select(y => new FinancialYearOption(y, new FinancialYear(y).Label)).ToList();

    public bool CanCreateCompany => authorizer.Has(Permissions.CompanyCreate);

    public bool CanDeleteDemo => authorizer.Has(Permissions.CompanyManage);

    [ObservableProperty] private bool _isDarkTheme = shell.CurrentTheme() == ThemeService.Dark;
    [ObservableProperty] private string _newCompanyName = string.Empty;
    [ObservableProperty] private IndianState? _newCompanyState;
    [ObservableProperty] private FinancialYearOption? _newCompanyYear;

    partial void OnIsDarkThemeChanged(bool value)
    {
        if (!_syncing) shell.ApplyTheme(value ? ThemeService.Dark : ThemeService.Light);
    }

    public void SyncTheme(bool dark)
    {
        _syncing = true;
        IsDarkTheme = dark;
        _syncing = false;
    }

    public async Task LoadAsync() => await RunAsync(async () =>
    {
        Companies.Clear();
        foreach (var company in await workspace.GetCompaniesAsync()) Companies.Add(company);
        NewCompanyYear ??= FinancialYears.FirstOrDefault(y => y.StartYear == FinancialYear.Containing(DateOnly.FromDateTime(DateTime.Today)).StartYear);
    });

    [RelayCommand]
    private async Task CreateCompanyAsync()
    {
        if (NewCompanyState is null || NewCompanyYear is null)
        {
            Error = "Enter the name, state and first financial year of the new company.";
            return;
        }

        if (await RunAsync(() => companies.CreateCompanyAsync(new NewCompanyRequest(NewCompanyName, NewCompanyState.Code, NewCompanyYear.StartYear)),
                $"{NewCompanyName.Trim()} created. Switch to it from the company list in the top bar."))
        {
            NewCompanyName = string.Empty;
            await shell.CompaniesChanged();
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task DeleteDemoAsync(CompanySummary company)
    {
        var confirmed = await dialogs.ConfirmAsync("Delete demonstration company?",
            $"\"{company.Name}\" and all its sample data, including its audit trail, will be removed. Your own companies are not affected. This cannot be undone.",
            "Delete", isDestructive: true);
        if (!confirmed) return;
        if (await RunAsync(() => workspace.DeleteDemonstrationCompanyAsync(company.Id), "Demonstration company deleted."))
        {
            Companies.Remove(company);
            await shell.CompaniesChanged();
        }
    }
}

public sealed partial class CompanyProfileViewModel(
    CompanyService companies,
    IAuthorizer authorizer,
    ToastService toasts,
    ILogger<CompanyProfileViewModel> logger,
    ShellContext shell) : PageViewModel(logger, toasts), ILoadable
{
    public bool CanEdit => authorizer.Has(Permissions.CompanyManage);

    public IReadOnlyList<IndianState> States { get; } = IndianState.All;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _legalName = string.Empty;
    [ObservableProperty] private IndianState? _state;
    [ObservableProperty] private string _addressLine1 = string.Empty;
    [ObservableProperty] private string _addressLine2 = string.Empty;
    [ObservableProperty] private string _city = string.Empty;
    [ObservableProperty] private string _pincode = string.Empty;
    [ObservableProperty] private string _phone = string.Empty;
    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _pan = string.Empty;
    [ObservableProperty] private string _gstin = string.Empty;
    [ObservableProperty] private string _financialYear = string.Empty;
    [ObservableProperty] private bool _hasLogo;

    public async Task LoadAsync() => await RunAsync(async () =>
    {
        var details = await companies.GetDetailsAsync();
        var p = details.Profile;
        (Name, LegalName, AddressLine1, AddressLine2, City, Pincode, Phone, Email, Pan, Gstin) =
            (p.Name, p.LegalName, p.AddressLine1, p.AddressLine2, p.City, p.Pincode, p.Phone, p.Email, p.Pan, p.Gstin);
        State = IndianState.FromCode(p.StateCode);
        FinancialYear = new FinancialYear(details.FinancialYearStartYear).Label;
        HasLogo = details.HasLogo;
    });

    [RelayCommand]
    private async Task SaveAsync()
    {
        var profile = new CompanyProfile(Name, LegalName, State?.Code ?? string.Empty, AddressLine1, AddressLine2, City, Pincode, Phone, Email, Pan, Gstin);
        if (await RunAsync(() => companies.UpdateProfileAsync(profile), "Company profile saved."))
        {
            await shell.CompaniesChanged();
            await LoadAsync();
        }
    }

    /// <summary>Called by the view after the user picks a file.</summary>
    public async Task SetLogoAsync(byte[]? logo)
    {
        if (await RunAsync(() => companies.SetLogoAsync(logo), logo is null ? "Logo removed." : "Logo saved.")) HasLogo = logo is not null;
    }
}

public sealed partial class LocationsViewModel(
    CompanyService companies,
    IAuthorizer authorizer,
    ToastService toasts,
    ILogger<LocationsViewModel> logger) : PageViewModel(logger, toasts), ILoadable
{
    private static readonly BranchRow NoBranch = new(Guid.Empty, "", "No branch", "", "", true);

    public bool CanEdit => authorizer.Has(Permissions.CompanyManage);

    public IReadOnlyList<IndianState> States { get; } = IndianState.All;

    public ObservableCollection<BranchRow> Branches { get; } = [];

    public ObservableCollection<BranchRow> BranchChoices { get; } = [];

    public ObservableCollection<WarehouseRow> Warehouses { get; } = [];

    [ObservableProperty] private BranchRow? _selectedBranch;
    [ObservableProperty] private string _branchCode = string.Empty;
    [ObservableProperty] private string _branchName = string.Empty;
    [ObservableProperty] private IndianState? _branchState;
    [ObservableProperty] private string _branchAddress = string.Empty;

    [ObservableProperty] private WarehouseRow? _selectedWarehouse;
    [ObservableProperty] private string _warehouseCode = string.Empty;
    [ObservableProperty] private string _warehouseName = string.Empty;
    [ObservableProperty] private BranchRow? _warehouseBranch;

    public async Task LoadAsync() => await RunAsync(async () =>
    {
        Branches.Clear();
        BranchChoices.Clear();
        BranchChoices.Add(NoBranch);
        foreach (var branch in await companies.ListBranchesAsync())
        {
            Branches.Add(branch);
            BranchChoices.Add(branch);
        }

        Warehouses.Clear();
        foreach (var warehouse in await companies.ListWarehousesAsync()) Warehouses.Add(warehouse);
    });

    partial void OnSelectedBranchChanged(BranchRow? value)
    {
        if (value is null) return;
        (BranchCode, BranchName, BranchAddress) = (value.Code, value.Name, value.Address);
        BranchState = IndianState.FromCode(value.StateCode);
    }

    partial void OnSelectedWarehouseChanged(WarehouseRow? value)
    {
        if (value is null) return;
        (WarehouseCode, WarehouseName) = (value.Code, value.Name);
        WarehouseBranch = BranchChoices.FirstOrDefault(b => b.Id == (value.BranchId ?? Guid.Empty)) ?? NoBranch;
    }

    [RelayCommand]
    private void NewBranch()
    {
        SelectedBranch = null;
        (BranchCode, BranchName, BranchAddress, BranchState) = (string.Empty, string.Empty, string.Empty, null);
    }

    [RelayCommand]
    private async Task SaveBranchAsync()
    {
        var input = new BranchInput(SelectedBranch?.Id, BranchCode, BranchName, BranchState?.Code ?? string.Empty, BranchAddress);
        if (await RunAsync(() => companies.SaveBranchAsync(input), "Branch saved.")) await LoadAsync();
    }

    [RelayCommand]
    private async Task ToggleBranchAsync(BranchRow branch)
    {
        if (await RunAsync(() => companies.SetBranchActiveAsync(branch.Id, !branch.IsActive))) await LoadAsync();
    }

    [RelayCommand]
    private void NewWarehouse()
    {
        SelectedWarehouse = null;
        (WarehouseCode, WarehouseName, WarehouseBranch) = (string.Empty, string.Empty, NoBranch);
    }

    [RelayCommand]
    private async Task SaveWarehouseAsync()
    {
        var branchId = WarehouseBranch is { } b && b.Id != Guid.Empty ? b.Id : (Guid?)null;
        var input = new WarehouseInput(SelectedWarehouse?.Id, WarehouseCode, WarehouseName, branchId);
        if (await RunAsync(() => companies.SaveWarehouseAsync(input), "Warehouse saved.")) await LoadAsync();
    }

    [RelayCommand]
    private async Task ToggleWarehouseAsync(WarehouseRow warehouse)
    {
        if (await RunAsync(() => companies.SetWarehouseActiveAsync(warehouse.Id, !warehouse.IsActive))) await LoadAsync();
    }
}

public sealed partial class NumberingViewModel(
    CompanyService companies,
    IAuthorizer authorizer,
    ToastService toasts,
    ILogger<NumberingViewModel> logger) : PageViewModel(logger, toasts), ILoadable
{
    public bool CanEdit => authorizer.Has(Permissions.CompanyManage);

    public ObservableCollection<NumberingRow> Rules { get; } = [];

    [ObservableProperty] private NumberingRow? _selected;
    [ObservableProperty] private string _prefix = string.Empty;
    [ObservableProperty] private int _padding = 4;
    [ObservableProperty] private bool _resetEachYear = true;

    public async Task LoadAsync() => await RunAsync(async () =>
    {
        var keep = Selected?.Id;
        Rules.Clear();
        foreach (var rule in await companies.ListNumberingAsync()) Rules.Add(rule);
        Selected = Rules.FirstOrDefault(r => r.Id == keep) ?? Rules.FirstOrDefault();
    });

    partial void OnSelectedChanged(NumberingRow? value)
    {
        if (value is null) return;
        (Prefix, Padding, ResetEachYear) = (value.Prefix, value.Padding, value.ResetEachFinancialYear);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Selected is not { } rule) return;
        if (await RunAsync(() => companies.UpdateNumberingAsync(rule.Id, Prefix, Padding, ResetEachYear), "Numbering saved. New documents use it; existing numbers do not change."))
            await LoadAsync();
    }
}

public sealed partial class UsersViewModel(
    UserService users,
    RoleService roles,
    IAuthorizer authorizer,
    DialogService dialogs,
    ToastService toasts,
    ILogger<UsersViewModel> logger) : PageViewModel(logger, toasts), ILoadable
{
    public bool CanEdit => authorizer.Has(Permissions.UsersManage);

    public ObservableCollection<UserRow> Users { get; } = [];

    public ObservableCollection<RoleRow> Roles { get; } = [];

    [ObservableProperty] private UserRow? _selected;
    [ObservableProperty] private RoleRow? _selectedRole;
    [ObservableProperty] private string _newUsername = string.Empty;
    [ObservableProperty] private string _newDisplayName = string.Empty;
    [ObservableProperty] private RoleRow? _newRole;
    [ObservableProperty] private string _existingUsername = string.Empty;

    // Temporary passwords come from PasswordBox events in the view.
    public string NewTemporaryPassword { get; set; } = string.Empty;

    public string ResetTemporaryPassword { get; set; } = string.Empty;

    public event EventHandler? PasswordsCleared;

    public async Task LoadAsync() => await RunAsync(async () =>
    {
        var keep = Selected?.MembershipId;
        Roles.Clear();
        foreach (var role in await roles.ListAsync()) Roles.Add(role);
        Users.Clear();
        foreach (var user in await users.ListAsync()) Users.Add(user);
        Selected = Users.FirstOrDefault(u => u.MembershipId == keep);
        NewRole ??= Roles.FirstOrDefault(r => r.Name == "Read-only");
    });

    partial void OnSelectedChanged(UserRow? value) => SelectedRole = value is null ? null : Roles.FirstOrDefault(r => r.Id == value.RoleId);

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (NewRole is null)
        {
            Error = "Choose a role for the new user.";
            return;
        }

        if (await RunAsync(() => users.CreateAsync(new NewUserRequest(NewUsername, NewDisplayName, NewTemporaryPassword, NewRole.Id)),
                $"User {NewUsername.Trim()} created. Give them the temporary password; they must change it when they first sign in."))
        {
            (NewUsername, NewDisplayName, NewTemporaryPassword) = (string.Empty, string.Empty, string.Empty);
            PasswordsCleared?.Invoke(this, EventArgs.Empty);
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task AddExistingAsync()
    {
        if (NewRole is null) return;
        if (await RunAsync(() => users.AddExistingAsync(ExistingUsername, NewRole.Id), $"{ExistingUsername.Trim()} can now open this company."))
        {
            ExistingUsername = string.Empty;
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task ChangeRoleAsync()
    {
        if (Selected is not { } user || SelectedRole is not { } role) return;
        if (await RunAsync(() => users.ChangeRoleAsync(user.MembershipId, role.Id), $"{user.DisplayName} is now {role.Name}.")) await LoadAsync();
    }

    [RelayCommand]
    private async Task ToggleActiveAsync()
    {
        if (Selected is not { } user) return;
        if (user.IsActive && !await dialogs.ConfirmAsync($"Remove {user.DisplayName}'s access?",
                "They will no longer be able to open this company. Their past work stays in the records and the audit trail.", "Remove access", true))
            return;
        if (await RunAsync(() => users.SetActiveAsync(user.MembershipId, !user.IsActive), user.IsActive ? "Access removed." : "Access restored."))
            await LoadAsync();
    }

    [RelayCommand]
    private async Task ResetPasswordAsync()
    {
        if (Selected is not { } user) return;
        if (await RunAsync(() => users.ResetPasswordAsync(user.UserId, ResetTemporaryPassword),
                $"Temporary password set for {user.DisplayName}. They must change it at next sign-in."))
        {
            ResetTemporaryPassword = string.Empty;
            PasswordsCleared?.Invoke(this, EventArgs.Empty);
            await LoadAsync();
        }
    }
}

public sealed partial class PermissionChoice(PermissionInfo info, bool isChecked) : ObservableObject
{
    public PermissionInfo Info { get; } = info;

    [ObservableProperty]
    private bool _isChecked = isChecked;
}

public sealed record PermissionGroup(string Module, IReadOnlyList<PermissionChoice> Items);

public sealed partial class RolesViewModel(
    RoleService roles,
    IAuthorizer authorizer,
    DialogService dialogs,
    ToastService toasts,
    ILogger<RolesViewModel> logger) : PageViewModel(logger, toasts), ILoadable
{
    public bool CanEdit => authorizer.Has(Permissions.RolesManage);

    public ObservableCollection<RoleRow> Roles { get; } = [];

    [ObservableProperty] private RoleRow? _selected;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private decimal _maxDiscountPercent;
    [ObservableProperty] private bool _isAdministrator;
    [ObservableProperty] private bool _isSystem;
    [ObservableProperty] private IReadOnlyList<PermissionGroup> _groups = [];

    public async Task LoadAsync() => await RunAsync(async () =>
    {
        var keep = Selected?.Id;
        Roles.Clear();
        foreach (var role in await roles.ListAsync()) Roles.Add(role);
        Selected = Roles.FirstOrDefault(r => r.Id == keep) ?? Roles.FirstOrDefault();
    });

    partial void OnSelectedChanged(RoleRow? value)
    {
        Name = value?.Name ?? string.Empty;
        Description = value?.Description ?? string.Empty;
        MaxDiscountPercent = value?.MaxDiscountPercent ?? 0;
        IsAdministrator = value?.IsAdministrator ?? false;
        IsSystem = value?.IsSystem ?? false;
        var granted = value?.Permissions ?? new HashSet<string>();
        Groups = roles.Catalogue.GroupBy(p => p.Module)
            .Select(g => new PermissionGroup(g.Key, g.Select(p => new PermissionChoice(p, granted.Contains(p.Key))).ToList()))
            .ToList();
    }

    [RelayCommand]
    private void New() => Selected = null;

    [RelayCommand]
    private async Task SaveAsync()
    {
        var granted = Groups.SelectMany(g => g.Items).Where(i => i.IsChecked).Select(i => i.Info.Key).ToList();
        Guid saved = Guid.Empty;
        if (await RunAsync(async () => saved = await roles.SaveAsync(new RoleInput(Selected?.Id, Name, Description, granted, MaxDiscountPercent)),
                "Role saved. Members get the new permissions the next time they sign in or switch company."))
        {
            await LoadAsync();
            Selected = Roles.FirstOrDefault(r => r.Id == saved);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Selected is not { } role) return;
        if (!await dialogs.ConfirmAsync($"Delete the {role.Name} role?", "This cannot be undone.", "Delete", true)) return;
        if (await RunAsync(() => roles.DeleteAsync(role.Id), "Role deleted."))
        {
            Selected = null;
            await LoadAsync();
        }
    }
}

public sealed partial class AuditViewModel(
    AuditService audit,
    ToastService toasts,
    ILogger<AuditViewModel> logger) : PageViewModel(logger, toasts), ILoadable
{
    public ObservableCollection<AuditRow> Entries { get; } = [];

    [ObservableProperty] private AuditRow? _selected;
    [ObservableProperty] private string? _verification;
    [ObservableProperty] private bool _isIntact = true;

    public IReadOnlyList<AuditChange> SelectedChanges => Selected?.Changes ?? [];

    partial void OnSelectedChanged(AuditRow? value) => OnPropertyChanged(nameof(SelectedChanges));

    public async Task LoadAsync() => await RunAsync(async () =>
    {
        Entries.Clear();
        foreach (var entry in await audit.QueryAsync(new AuditQuery(Take: 500))) Entries.Add(entry);
    });

    [RelayCommand]
    private async Task VerifyAsync() => await RunAsync(async () =>
    {
        var result = await audit.VerifyAsync();
        IsIntact = result.IsIntact;
        Verification = result.IsIntact
            ? $"Audit trail intact: {result.EntriesChecked} entries checked, none changed or removed."
            : $"Audit trail broken at entry {result.FirstBrokenId}. An entry was changed or removed outside QIK Suite. Keep a copy of the database file and contact your administrator.";
    });
}

public sealed partial class ChangePasswordViewModel(
    IWorkspaceService workspace,
    ToastService toasts,
    ILogger<ChangePasswordViewModel> logger) : PageViewModel(logger, toasts)
{
    public string Title => "Change password";

    public bool IsForced => workspace.CurrentSession?.MustChangePassword == true;

    public string Current { get; set; } = string.Empty;

    public string NewPassword { get; set; } = string.Empty;

    public string Confirmation { get; set; } = string.Empty;

    public event EventHandler? Changed;

    public event EventHandler? Cancelled;

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (NewPassword != Confirmation)
        {
            Error = "The new passwords do not match.";
            return;
        }

        // Hashing is CPU-bound; keep it off the UI thread.
        if (await RunAsync(() => Task.Run(() => workspace.ChangePasswordAsync(Current, NewPassword)), "Password changed."))
        {
            Current = NewPassword = Confirmation = string.Empty;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
