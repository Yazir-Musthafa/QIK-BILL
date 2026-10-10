using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nexora.Application;
using Nexora.Domain.Companies;
using Nexora.Desktop.Services;

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
    private bool _suppressCompanySwitch;

    public ShellViewModel(IWorkspaceService workspace, IThemeService theme, DialogService dialogs, ToastService toasts,
        ILoggerFactory loggers, SessionInfo session, bool sidebarCollapsed)
    {
        _workspace = workspace;
        _theme = theme;
        _dialogs = dialogs;
        _toasts = toasts;
        _loggers = loggers;
        _logger = loggers.CreateLogger<ShellViewModel>();
        _session = session;
        _isSidebarCollapsed = sidebarCollapsed;
        NavItems = NavCatalog.Items;
    }

    public event EventHandler? SignedOut;

    public IReadOnlyList<NavItem> NavItems { get; }

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
        if (CurrentPage is SettingsViewModel settings) settings.IsDarkTheme = theme == ThemeService.Dark;
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
                var settings = new SettingsViewModel(_workspace, _dialogs, _toasts, _loggers.CreateLogger<SettingsViewModel>(),
                    _theme.Current, ReloadCompaniesAsync, t => { if (t != _theme.Current) SetTheme(t); });
                _ = LoadSafelyAsync(settings.LoadAsync);
                return settings;
            case NavCatalog.Help:
                return new HelpViewModel();
            case NavCatalog.Gallery:
                return new GalleryViewModel(_dialogs, _toasts);
            default:
                return new PlaceholderViewModel(item, Navigate);
        }
    }

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

    private async Task SwitchCompanyAsync(CompanySummary company)
    {
        try
        {
            Session = await _workspace.SwitchCompanyAsync(company.Id);
            _toasts.Show($"Switched to {company.Name}.");
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

    private async Task LoadSafelyAsync(Func<Task> load)
    {
        try
        {
            await load();
        }
        catch (Exception exception)
        {
            _logger.LogError("Page failed to load; error type {ErrorType}", exception.GetType().FullName);
            _toasts.Show("This page could not be loaded. Try again.", ToastKind.Error);
        }
    }
}
