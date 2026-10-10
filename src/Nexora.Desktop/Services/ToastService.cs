using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;

namespace Nexora.Desktop.Services;

public enum ToastKind
{
    Information,
    Success,
    Warning,
    Error,
}

public sealed record Toast(string Message, ToastKind Kind);

/// <summary>Short, non-blocking notifications. Must be used from the UI thread.</summary>
public sealed partial class ToastService
{
    private static readonly TimeSpan DisplayTime = TimeSpan.FromSeconds(4);

    public ObservableCollection<Toast> Items { get; } = [];

    public async void Show(string message, ToastKind kind = ToastKind.Information)
    {
        var toast = new Toast(message, kind);
        Items.Add(toast);
        while (Items.Count > 3) Items.RemoveAt(0);
        await Task.Delay(DisplayTime);
        Items.Remove(toast);
    }

    [RelayCommand]
    private void Dismiss(Toast toast) => Items.Remove(toast);
}
