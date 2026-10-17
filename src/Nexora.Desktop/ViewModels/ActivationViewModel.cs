using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexora.Infrastructure.Licensing;

namespace Nexora.Desktop.ViewModels;

public sealed partial class ActivationViewModel : ObservableObject
{
    private readonly ActivationClient _activation;
    public event EventHandler? Activated;
    public ActivationViewModel(ActivationClient activation)
    {
        _activation = activation;
        Username = activation.Username;
        Message = activation.Message;
        if (activation.HasAccount && !activation.IsActive)
            Message = "Internet is needed to check activation. Connect to the internet and click Check activation. If your account is pending or suspended, contact the admin.";
    }
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _customerName = "";
    [ObservableProperty] private string _contact = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RegisterCommand))]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand))]
    private bool _isBusy;
    public string Password { get; set; } = "";
    public string InstallationCode => _activation.InstallationId.ToString();
    public bool IsConfigured => _activation.IsConfigured;
    public event EventHandler? ClearPassword;
    private bool CanSubmit() => !IsBusy && IsConfigured;
    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private Task RegisterAsync() => RunAsync(() => _activation.RegisterAsync(Username, Password, CustomerName, Contact));
    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private Task SignInAsync() => RunAsync(() => _activation.SignInAsync(Username, Password));
    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private Task CheckAsync() => RunAsync(() => _activation.CheckAsync());
    [RelayCommand]
    private void OpenAdmin()
    {
        if (_activation.AdminUri is not { } uri) { Message = "The activation server has not been configured yet."; return; }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { Message = $"Open {uri} in your browser to sign in as admin."; }
    }
    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
            Message = _activation.Message;
            if (_activation.IsActive) Activated?.Invoke(this, EventArgs.Empty);
        }
        finally { Password = ""; ClearPassword?.Invoke(this, EventArgs.Empty); IsBusy = false; }
    }
}
