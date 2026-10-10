using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexora.Application;
using Nexora.Application.Security;
using Nexora.Domain.Companies;
using Nexora.Desktop.Services;
using Nexora.Desktop.ViewModels.Settings;

namespace Nexora.Desktop.ViewModels;

/// <summary>A command palette entry: either a destination or an action.</summary>
public sealed record PaletteEntry(string Label, string Hint, string Glyph, Action Execute);

public sealed partial class ShellViewModel : ObservableObject
{
    private readonly IWorkspaceService _workspace;
    private readonly IThemeService _theme;
    private readonly DialogService _dialogs;
    private readonly ToastService _toasts;
    private readonly ILoggerFactory _loggers;
    private readonly ILogger<ShellViewModel> _logger;
    private readonly IServiceProvider _services;
    private readonly IAuthorizer _authorizer;
    private bool _suppressCompanySwitch;

    public ShellViewModel(IWorkspaceService workspace, IThemeService theme, DialogService dialogs, ToastService toasts,
        ILoggerFactory loggers, IServiceProvider services, IAuthorizer authorizer, SessionInfo session, bool sidebarCollapsed)
    {
        _workspace = workspace;
        _theme = theme;
        _dialogs = dialogs;
        _toasts = toasts;
        _loggers = loggers;
        _services = services;
        _authorizer = authorizer;
        _logger = loggers.CreateLogger<ShellViewModel>();
        _session = session;
        _isSidebarCollapsed = sidebarCollapsed;
        _navItems = VisibleNavItems();
    }

    public event EventHandler? SignedOut;

    /// <summary>Destinations the current role may open. Hiding is a convenience; services enforce permissions.</summary>
    [ObservableProperty]
    private IReadOnlyList<NavItem> _navItems;

    public ObservableCollection<CompanySummary> Companies { get; } = [];

    public ObservableCollection<PaletteEntry> PaletteResults { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UserInitials), nameof(FinancialYearLabel))]
    private SessionInfo _session;

    [ObservableProperty]
    private CompanySummary? _selectedCompany;

    [ObservableProperty]
    private NavItem? _selectedNavItem;

    [ObservableProperty]
    private object? _currentPage;

    [ObservableProperty]
    private bool _isSidebarCollapsed;

    [ObservableProperty]
    private bool _isPaletteOpen;

    [ObservableProperty]
    private string _paletteQuery = string.Empty;

    [ObservableProperty]
    private PaletteEntry? _selectedPaletteEntry;

    [ObservableProperty]
    private bool _isNotificationsOpen;

    [ObservableProperty]
    private bool _isUserMenuOpen;

    public string UserInitials => string.Concat(Session.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Take(2).Select(p => char.ToUpperInvariant(p[0])));

    public string FinancialYearLabel => $"FY {new FinancialYear(Session.FinancialYearStartYear).Label}";

    public bool IsDarkTheme => _theme.Current == ThemeService.Dark;

    public async Task InitializeAsync()
    {
        await ReloadCompaniesAsync();
        Navigate(NavCatalog.Dashboard);
    }

    public async Task ReloadCompaniesAsync()
    {
        _suppressCompanySwitch = true;
        try
        {
            Companies.Clear();
            foreach (var company in await _workspace.GetCompaniesAsync()) Companies.Add(company);
            SelectedCompany = Companies.FirstOrDefault(c => c.Id == Session.CompanyId);
        }
        finally
        {
            _suppressCompanySwitch = false;
        }
    }

    public void Navigate(string key)
    {
        var item = NavCatalog.Find(key);
        if (item.Permission is { } permission && !_authorizer.Has(permission)) item = NavCatalog.Find(NavCatalog.Dashboard);
        // Set the key first so the selection change below does not navigate a second time.
        CurrentPageKey = item.Key;
        SelectedNavItem = NavItems.Contains(item) ? item : null;
        CurrentPage = CreatePage(item);
    }

    partial void OnSelectedNavItemChanged(NavItem? value)
    {
        if (value is not null && CurrentPageKey != value.Key) Navigate(value.Key);
    }

    partial void OnSelectedCompanyChanged(CompanySummary? value)
    {
        if (_suppressCompanySwitch || value is null || value.Id == Session.CompanyId) return;
        _ = SwitchCompanyAsync(value);
    }

    partial void OnIsSidebarCollapsedChanged(bool value) => _ = SavePreferencesAsync();

    partial void OnPaletteQueryChanged(string value) => RefreshPalette();

    private string? CurrentPageKey { get; set; }

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarCollapsed = !IsSidebarCollapsed;

    [RelayCommand]
    public void ToggleTheme() => SetTheme(IsDarkTheme ? ThemeService.Light : ThemeService.Dark);

    [RelayCommand]
    private void OpenPalette()
    {
        IsNotificationsOpen = IsUserMenuOpen = false;
        PaletteQuery = string.Empty;
        RefreshPalette();
        IsPaletteOpen = true;
    }

    [RelayCommand]
    private void ClosePalette() => IsPaletteOpen = false;

    [RelayCommand]
    private void MovePaletteSelection(string direction)
    {
        if (PaletteResults.Count == 0) return;
        var index = SelectedPaletteEntry is null ? -1 : PaletteResults.IndexOf(SelectedPaletteEntry);
        index = direction == "up" ? Math.Max(0, index - 1) : Math.Min(PaletteResults.Count - 1, index + 1);
        SelectedPaletteEntry = PaletteResults[index];
    }

    [RelayCommand]
    private void ExecutePaletteEntry(PaletteEntry? entry)
    {
        entry ??= SelectedPaletteEntry;
        if (entry is null) return;
        IsPaletteOpen = false;
        entry.Execute();
    }

    [RelayCommand]
    private void ToggleNotifications() => IsNotificationsOpen = !IsNotificationsOpen;

    [RelayCommand]
    private void ToggleUserMenu() => IsUserMenuOpen = !IsUserMenuOpen;

    [RelayCommand]
    private void OpenHelp() => Navigate(NavCatalog.Help);

    [RelayCommand]
    private void OpenGallery() => Navigate(NavCatalog.Gallery);

    [RelayCommand]
    private void GoToDashboard() => Navigate(NavCatalog.Dashboard);

    [RelayCommand]
    private void ChangePassword()
    {
        IsUserMenuOpen = false;
        Navigate(NavCatalog.ChangePassword);
    }

    [RelayCommand]
    private void SignOut()
    {
        IsUserMenuOpen = false;
        _workspace.SignOut();
        SignedOut?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Closes the topmost overlay. Returns false if nothing was open.</summary>
    public bool CloseOverlay()
    {
        if (IsPaletteOpen) IsPaletteOpen = false;
        else if (IsUserMenuOpen) IsUserMenuOpen = false;
        else if (IsNotificationsOpen) IsNotificationsOpen = false;
        else return false;
        return true;
    }

    private void SetTheme(string theme)
    {
        _theme.Apply(theme);
        OnPropertyChanged(nameof(IsDarkTheme));
        if (CurrentPage is SettingsViewModel settings) settings.SyncTheme(theme == ThemeService.Dark);
        _ = SavePreferencesAsync();
    }

    private object CreatePage(NavItem item)
    {
        switch (item.Key)
        {
            case NavCatalog.Dashboard:
                var dashboard = new DashboardViewModel(_workspace, _loggers.CreateLogger<DashboardViewModel>(), Navigate);
                _ = dashboard.LoadAsync();
                return dashboard;
            case NavCatalog.Settings:
                var context = new ShellContext(() => _theme.Current, t => { if (t != _theme.Current) SetTheme(t); }, SessionChangedAsync, Navigate);
                return ActivatorUtilities.CreateInstance<SettingsViewModel>(_services, context);
            case NavCatalog.ChangePassword:
                var change = ActivatorUtilities.CreateInstance<ChangePasswordViewModel>(_services);
                change.Changed += (_, _) => Navigate(NavCatalog.Dashboard);
                return change;
            case NavCatalog.Help:
                return new HelpViewModel();
            case NavCatalog.Gallery:
                return new GalleryViewModel(_dialogs, _toasts);
            default:
                if (PageTypes.TryGetValue(item.Key, out var pageType))
                {
                    var page = ActivatorUtilities.CreateInstance(_services, pageType);
                    if (page is ILoadable loadable) _ = loadable.LoadAsync();
                    return page;
                }

                return new PlaceholderViewModel(item, Navigate);
        }
    }

    /// <summary>Pages built through DI. Each loads its own data and handles its own errors.</summary>
    private static readonly Dictionary<string, Type> PageTypes = new()
    {
        ["accounts.chart"] = typeof(Accounting.ChartOfAccountsViewModel),
        ["accounts.vouchers"] = typeof(Accounting.VouchersViewModel),
        ["accounts.ledgers"] = typeof(Accounting.LedgerViewModel),
        ["accounts.trial-balance"] = typeof(Accounting.TrialBalanceViewModel),
        ["accounts.statements"] = typeof(Accounting.StatementsViewModel),
        ["accounts.opening"] = typeof(Accounting.OpeningBalancesViewModel),
        ["inventory.products"] = typeof(Inventory.ProductsViewModel),
        ["inventory.stock"] = typeof(Inventory.StockViewModel),
        ["sales.invoices"] = typeof(Billing.SalesBillingViewModel),
        ["sales.customers"] = typeof(Billing.CustomersViewModel),
        ["purchases.invoices"] = typeof(Billing.PurchaseBillingViewModel),
        ["purchases.suppliers"] = typeof(Billing.SuppliersViewModel),
    };

    private void RefreshPalette()
    {
        var query = PaletteQuery.Trim();
        var entries = NavItems
            .Select(i => new PaletteEntry(i.Label, i.IsAvailable ? i.Group : $"{i.Group} · Phase {i.Phase}", i.Glyph, () => Navigate(i.Key)))
            .Concat(
            [
                new PaletteEntry(IsDarkTheme ? "Switch to light theme" : "Switch to dark theme", "Ctrl+Shift+L", "", ToggleTheme),
                new PaletteEntry("Collapse or expand sidebar", "Ctrl+B", "", ToggleSidebar),
                new PaletteEntry("Sign out", "Session", "", SignOut),
            ])
            .Where(e => query.Length == 0
                || e.Label.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || e.Hint.Contains(query, StringComparison.CurrentCultureIgnoreCase));

        PaletteResults.Clear();
        foreach (var entry in entries) PaletteResults.Add(entry);
        SelectedPaletteEntry = PaletteResults.FirstOrDefault();
    }

    /// <summary>Picks up role or company changes made from Settings; signs out if access was removed.</summary>
    private async Task SessionChangedAsync()
    {
        if (_workspace.CurrentSession is not { } current)
        {
            SignedOut?.Invoke(this, EventArgs.Empty);
            return;
        }

        Session = current;
        NavItems = VisibleNavItems();
        await ReloadCompaniesAsync();
    }

    private IReadOnlyList<NavItem> VisibleNavItems() =>
        NavCatalog.Items.Where(i => i.Permission is null || _authorizer.Has(i.Permission)).ToList();

    private async Task SwitchCompanyAsync(CompanySummary company)
    {
        try
        {
            Session = await _workspace.SwitchCompanyAsync(company.Id);
            NavItems = VisibleNavItems();
            _toasts.Show($"Switched to {company.Name} as {Session.RoleName}.");
            Navigate(CurrentPageKey ?? NavCatalog.Dashboard);
        }
        catch (WorkspaceValidationException exception)
        {
            _toasts.Show(exception.Message, ToastKind.Error);
            await ReloadCompaniesAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError("Company switch failed; error type {ErrorType}", exception.GetType().FullName);
            _toasts.Show("The company could not be opened. Try again.", ToastKind.Error);
            await ReloadCompaniesAsync();
        }
    }

    private async Task SavePreferencesAsync()
    {
        try
        {
            await _workspace.SavePreferencesAsync(new WorkspacePreferences(_theme.Current, IsSidebarCollapsed));
        }
        catch (Exception exception)
        {
            // Preferences are a convenience; failing to save them must not interrupt work.
            _logger.LogWarning("Preferences not saved; error type {ErrorType}", exception.GetType().FullName);
        }
    }

}
