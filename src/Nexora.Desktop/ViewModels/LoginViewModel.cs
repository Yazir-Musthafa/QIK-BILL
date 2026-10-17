using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nexora.Application;

namespace Nexora.Desktop.ViewModels;

public sealed partial class LoginViewModel(IWorkspaceService workspace, ILogger<LoginViewModel> logger,
    Nexora.Infrastructure.Licensing.ActivationClient activation) : ObservableObject
{
    public event EventHandler<SessionInfo>? SignedIn;

    /// <summary>Raised after a failed attempt so the view can clear and refocus the password box.</summary>
    public event EventHandler? PasswordRejected;

    [ObservableProperty]
    private string _username = string.Empty;

    public string Password { get; set; } = string.Empty;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    private bool _isBusy;

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync()
    {
        IsBusy = true;
        Error = null;
        try
        {
            var username = Username;
            var password = Password;
            var result = await Task.Run(() => workspace.SignInAsync(username, password));
            Password = string.Empty;
            if (result.Succeeded)
            {
                SignedIn?.Invoke(this, result.Session!);
                return;
            }

            Error = result.Error;
            PasswordRejected?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            logger.LogError("Sign-in failed unexpectedly; error type {ErrorType}", exception.GetType().FullName);
            Error = "QIK Suite could not open the company database. Close other QIK Suite windows and try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSignIn() => !IsBusy;

    [RelayCommand]
    private void OpenAdmin()
    {
        if (activation.AdminUri is not { } uri) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { Error = $"Open {uri} in your browser to sign in as admin."; }
    }
}
