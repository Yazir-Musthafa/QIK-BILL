using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nexora.Application;
using Nexora.Application.Licensing;

namespace Nexora.Desktop.ViewModels;

public sealed partial class LoginViewModel(IWorkspaceService workspace, ILogger<LoginViewModel> logger,
    ILocalActivationStore activation, LocalAdministrator admin) : ObservableObject
{
    public event EventHandler<SessionInfo>? SignedIn;
    public event EventHandler? AdminSignedIn;
    public event EventHandler? RegisterRequested;
    public event EventHandler? PasswordRejected;
    [ObservableProperty] private string _username = "";
    public string Password { get; set; } = "";
    [ObservableProperty] private string? _error;
    [ObservableProperty] private string _activationMessage = "";
    [ObservableProperty] private bool _canRegister;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    [NotifyCanExecuteChangedFor(nameof(RegisterCommand))]
    private bool _isBusy;

    [RelayCommand]
    private async Task InitializeAsync()
    {
        try
        {
            CanRegister = await workspace.NeedsSetupAsync();
            var status = await activation.GetAsync();
            ActivationMessage = CanRegister ? "New customer? Register your company and account below. The admin must activate it before you can open any company pages."
                : status.Status == "Active" ? "This installation is active. Sign in with your account to continue offline."
                : status.Status == "Suspended" ? "Your installation is suspended. Contact the admin to reactivate your account."
                : "Your account needs activation. Contact the admin before using QIK Suite.";
        }
        catch (Exception ex) { ReportError(ex); }
    }
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void Register() { if (CanRegister) RegisterRequested?.Invoke(this, EventArgs.Empty); }
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task SignInAsync()
    {
        IsBusy = true;
        Error = null;
        try
        {
            var username = Username;
            var password = Password;
            if (LocalAdministrator.IsAdminUsername(username) && await Task.Run(() => admin.SignInAsync(username, password)))
            { AdminSignedIn?.Invoke(this, EventArgs.Empty); return; }
            if (LocalAdministrator.IsAdminUsername(username) && !activation.IsActive)
            { Error = "The admin password is incorrect or sign-in is temporarily locked after repeated attempts."; return; }
            if (!activation.IsActive)
            { Error = "Your account needs activation. Contact the admin to activate or reactivate it."; return; }
            var result = await Task.Run(() => workspace.SignInAsync(username, password));
            if (result.Succeeded) SignedIn?.Invoke(this, result.Session!);
            else Error = result.Error;
        }
        catch (WorkspaceValidationException ex) { Error = ex.Message; }
        catch (Exception ex) { ReportError(ex); }
        finally { Password = ""; PasswordRejected?.Invoke(this, EventArgs.Empty); IsBusy = false; }
    }
    private bool IsIdle() => !IsBusy;
    private void ReportError(Exception ex)
    {
        logger.LogError("Sign-in failed; error type {ErrorType}", ex.GetType().FullName);
        Error = "QIK Suite could not read the local database. Close other copies and try again.";
    }
}
