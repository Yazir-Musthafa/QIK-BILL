using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nexora.Application;
using Nexora.Application.Licensing;

namespace Nexora.Desktop.ViewModels;

public sealed partial class LocalAdminViewModel(LocalAdministrationService service, LocalAdministrator admin,
    ILogger<LocalAdminViewModel> logger) : ObservableObject
{
    public event EventHandler? SignedOut;
    [ObservableProperty] private string _status = "Loading…";
    [ObservableProperty] private string _note = "";
    [ObservableProperty] private string? _message;
    [ObservableProperty] private IReadOnlyList<RegisteredCustomer> _customers = [];
    [ObservableProperty] private IReadOnlyList<ActivationHistory> _history = [];
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ActivateCommand))]
    [NotifyCanExecuteChangedFor(nameof(SuspendCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isBusy;
    private bool IsIdle() => !IsBusy;
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task RefreshAsync() => RunAsync(LoadAsync);
    private async Task LoadAsync()
    {
        Status = (await service.StatusAsync()).Status;
        Customers = await service.CustomersAsync();
        History = await service.HistoryAsync();
    }
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task ActivateAsync() => ChangeAsync("Active");
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task SuspendAsync() => ChangeAsync("Suspended");
    private Task ChangeAsync(string status) => RunAsync(async () =>
    {
        await service.ChangeAsync(status, Note);
        Note = "";
        await LoadAsync();
        Message = status == "Active" ? "Activated. Sign out of the admin panel so the customer can sign in." : "Suspended. Company pages are locked for all staff on this installation.";
    });
    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true; Message = null;
        try { await action(); }
        catch (WorkspaceValidationException ex) { Message = ex.Message; }
        catch (Exception ex)
        {
            logger.LogError("Local activation administration failed; error type {ErrorType}", ex.GetType().FullName);
            Message = "The change could not be completed. Refresh to check the saved status, then try again.";
        }
        finally { IsBusy = false; }
    }
    [RelayCommand]
    private void SignOut() { admin.SignOut(); SignedOut?.Invoke(this, EventArgs.Empty); }
}
