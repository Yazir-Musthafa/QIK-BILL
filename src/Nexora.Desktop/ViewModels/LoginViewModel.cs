using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nexora.Application;

namespace Nexora.Desktop.ViewModels;

public sealed partial class LoginViewModel(IWorkspaceService workspace, ILogger<LoginViewModel> logger) : ObservableObject
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
            Error = "Nexora could not open the company database. Close other Nexora windows and try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSignIn() => !IsBusy;
}
