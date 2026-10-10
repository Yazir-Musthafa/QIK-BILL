using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Nexora.Desktop.Services;

/// <summary>A confirmation shown in the main window's dialog layer.</summary>
public sealed partial class ConfirmationRequest : ObservableObject
{
    private readonly TaskCompletionSource<bool> _result = new();

    public ConfirmationRequest(string title, string message, string confirmText, bool isDestructive)
    {
        Title = title;
        Message = message;
        ConfirmText = confirmText;
        IsDestructive = isDestructive;
    }

    public string Title { get; }

    public string Message { get; }

    public string ConfirmText { get; }

    public bool IsDestructive { get; }

    public Task<bool> Result => _result.Task;

    [RelayCommand]
    private void Confirm() => _result.TrySetResult(true);

    [RelayCommand]
    private void Cancel() => _result.TrySetResult(false);
}

public sealed partial class DialogService : ObservableObject
{
    [ObservableProperty]
    private ConfirmationRequest? _active;

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText, bool isDestructive = false)
    {
        Active?.CancelCommand.Execute(null);
        var request = new ConfirmationRequest(title, message, confirmText, isDestructive);
        Active = request;
        try
        {
            return await request.Result;
        }
        finally
        {
            if (ReferenceEquals(Active, request)) Active = null;
        }
    }

    /// <summary>Closes the open dialog as cancelled, for example when Escape is pressed.</summary>
    public bool CancelActive()
    {
        if (Active is null) return false;
        Active.CancelCommand.Execute(null);
        return true;
    }
}
