using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexora.Application;
using Nexora.Desktop.Services;
using Nexora.Desktop.ViewModels.Settings;

namespace Nexora.Desktop.ViewModels;

/// <summary>Root state: first-run setup, sign-in or the signed-in shell. Owns the dialog and toast layers.</summary>
public sealed partial class MainViewModel(
    IWorkspaceService workspace,
    IThemeService theme,
    IServiceProvider services,
    ILoggerFactory loggers,
    DialogService dialogs,
    ToastService toasts) : ObservableObject
{
    private readonly ILogger<MainViewModel> _logger = loggers.CreateLogger<MainViewModel>();
    private WorkspacePreferences _preferences = new();

    public DialogService Dialogs { get; } = dialogs;

    public ToastService Toasts { get; } = toasts;

    [ObservableProperty]
    private object? _current;

    private ShellViewModel? Shell => Current as ShellViewModel;

    public async Task InitializeAsync()
    {
        _preferences = await workspace.GetPreferencesAsync();
        theme.Apply(_preferences.Theme);
        if (await workspace.NeedsSetupAsync()) ShowSetup();
        else ShowLogin();
    }

    private void ShowSetup()
    {
        var setup = services.GetRequiredService<SetupWizardViewModel>();
        setup.Completed += async (_, session) => await ShowShellAsync(session, welcome: true);
        Current = setup;
    }

    private void ShowLogin()
    {
        Dialogs.CancelActive();
        Toasts.Items.Clear();
        var login = services.GetRequiredService<LoginViewModel>();
        login.SignedIn += async (_, session) => await ShowShellAsync(session, welcome: false);
        Current = login;
    }

    private async Task ShowShellAsync(SessionInfo session, bool welcome)
    {
        try
        {
            if (session.MustChangePassword)
            {
                ShowForcedPasswordChange(welcome);
                return;
            }

            _preferences = await workspace.GetPreferencesAsync();
            var shell = new ShellViewModel(workspace, theme, Dialogs, Toasts, loggers, services,
                services.GetRequiredService<Application.Security.IAuthorizer>(), session, _preferences.SidebarCollapsed);
            shell.SignedOut += (_, _) => ShowLogin();
            await shell.InitializeAsync();
            if (!ReferenceEquals(workspace.CurrentSession, session)) return;
            Current = shell;
            if (welcome) Toasts.Show($"{session.CompanyName} is ready.", ToastKind.Success);
        }
        catch (Exception exception)
        {
            _logger.LogError("Opening the workspace failed; error type {ErrorType}", exception.GetType().FullName);
            workspace.SignOut();
            ShowLogin();
            Toasts.Show("The workspace could not be opened. Sign in again.", ToastKind.Error);
        }
    }

    public Task RefreshIdentityAsync(SessionInfo session) => ShowShellAsync(session, welcome: false);

    public void HideSession()
    {
        Dialogs.CancelActive();
        Toasts.Items.Clear();
        foreach (var window in System.Windows.Application.Current.Windows.Cast<System.Windows.Window>().ToArray())
            if (window != System.Windows.Application.Current.MainWindow) window.Close();
        if (services.GetRequiredService<BackupMonitor>().IsRestoring) Current = null;
        else if (Current is not (LoginViewModel or SetupWizardViewModel)) ShowLogin();
    }

    [RelayCommand]
    private void LockSession()
    {
        workspace.SignOut();
        HideSession();
    }

    /// <summary>A temporary password must be replaced before the workspace opens.</summary>
    private void ShowForcedPasswordChange(bool welcome)
    {
        var change = services.GetRequiredService<ChangePasswordViewModel>();
        change.Changed += async (_, _) =>
        {
            if (workspace.CurrentSession is { } session) await ShowShellAsync(session, welcome);
        };
        change.Cancelled += (_, _) =>
        {
            workspace.SignOut();
            ShowLogin();
        };
        Current = change;
    }

    // Window-level shortcuts. They act only when the shell is showing and no dialog is open.
    [RelayCommand]
    private void OpenPalette() => WhenShellReady(s => s.OpenPaletteCommand.Execute(null));

    [RelayCommand]
    private void ToggleSidebar() => WhenShellReady(s => s.ToggleSidebarCommand.Execute(null));

    [RelayCommand]
    private void ToggleTheme()
    {
        if (Shell is { } shell) shell.ToggleTheme();
        else theme.Apply(theme.Current == ThemeService.Dark ? ThemeService.Light : ThemeService.Dark);
    }

    [RelayCommand]
    private void OpenHelp() => WhenShellReady(s => s.OpenHelpCommand.Execute(null));

    [RelayCommand]
    private void OpenGallery() => WhenShellReady(s => s.OpenGalleryCommand.Execute(null));

    [RelayCommand]
    private void GoToDashboard() => WhenShellReady(s => s.GoToDashboardCommand.Execute(null));

    [RelayCommand]
    private void Escape()
    {
        if (!Dialogs.CancelActive()) Shell?.CloseOverlay();
    }

    private void WhenShellReady(Action<ShellViewModel> action)
    {
        if (Shell is { } shell && Dialogs.Active is null) action(shell);
    }
}
