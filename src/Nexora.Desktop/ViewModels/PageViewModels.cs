using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nexora.Application;
using Nexora.Application.Formatting;
using Nexora.Desktop.Services;

namespace Nexora.Desktop.ViewModels;

/// <summary>Destination for a module that is not built yet. States the phase plainly; never implies data was saved.</summary>
public sealed partial class PlaceholderViewModel(NavItem item, Action<string> navigate) : ObservableObject
{
    public string Title => item.Label;

    public string Glyph => item.Glyph;

    public string Description => item.Description;

    public string Availability => $"Not available yet. This module is planned for Phase {item.Phase}; nothing can be recorded here in this version.";

    [RelayCommand]
    private void GoToDashboard() => navigate(NavCatalog.Dashboard);
}

public sealed class HelpViewModel
{
    public string Title => "Help";

    public IReadOnlyList<Shortcut> Shortcuts { get; } =
    [
        new("Ctrl+K", "Open the command palette to search pages and actions"),
        new("Ctrl+B", "Collapse or expand the sidebar"),
        new("Ctrl+Shift+L", "Switch between light and dark theme"),
        new("Ctrl+1", "Go to the dashboard"),
        new("F1", "Open this help page"),
        new("Esc", "Close the palette, menu or dialog"),
        new("Tab / Shift+Tab", "Move between fields"),
        new("Enter", "Submit the current form"),
        new("Up / Down", "Move through lists and table rows"),
    ];

    public string Note =>
        "Billing function-key shortcuts (F2 new invoice, F4 receipt and similar) arrive with the sales module in Phase 5.";
}

public sealed record GalleryRow(string Number, string Party, DateOnly Date, decimal Amount, string Status)
{
    public string AmountText => IndianNumberFormat.Amount(Amount, 2);

    public string DateText => Date.ToString("dd MMM yyyy");
}

/// <summary>Hidden screen (Ctrl+Shift+G) showing every control and state in the design system.</summary>
public sealed partial class GalleryViewModel(DialogService dialogs, ToastService toasts) : ObservableObject
{
    public string Title => "Component gallery";

    public IReadOnlyList<GalleryRow> Rows { get; } =
    [
        new("INV-0001", "Sharma Traders", new DateOnly(2026, 10, 1), 48_260.00m, "Posted"),
        new("INV-0002", "Patel Hardware", new DateOnly(2026, 10, 2), 1_20_000.00m, "Draft"),
        new("INV-0003", "Kaveri Distributors", new DateOnly(2026, 10, 4), 2_16_400.50m, "Overdue"),
        new("INV-0004", "Nair Electricals", new DateOnly(2026, 10, 6), 9_875.25m, "Posted"),
        new("INV-0005", "Gupta and Sons", new DateOnly(2026, 10, 8), 64_300.00m, "Cancelled"),
    ];

    public string TotalText => IndianNumberFormat.Amount(Rows.Sum(r => r.Amount), 2);

    public IReadOnlyList<string> Options { get; } = ["Weighted average", "FIFO"];

    [ObservableProperty] private string _sampleText = "Sharma Traders";
    [ObservableProperty] private string _invalidText = "27AAAAA0000A1Z";
    [ObservableProperty] private bool _isChecked = true;
    [ObservableProperty] private string? _confirmationResult;

    public string InvalidMessage => "GSTIN must be 15 characters.";

    [RelayCommand]
    private void Toast(string kind) => toasts.Show(kind switch
    {
        "success" => "Changes saved.",
        "warning" => "Printer is offline. The document was not sent.",
        "error" => "The backup could not be written. Check free disk space.",
        _ => "Company switched to Acme Traders.",
    }, kind switch
    {
        "success" => ToastKind.Success,
        "warning" => ToastKind.Warning,
        "error" => ToastKind.Error,
        _ => ToastKind.Information,
    });

    [RelayCommand]
    private async Task ConfirmAsync(string kind)
    {
        var destructive = kind == "destructive";
        var result = await dialogs.ConfirmAsync(
            destructive ? "Cancel invoice INV-0003?" : "Switch company?",
            destructive ? "The invoice will be marked cancelled and a reversal recorded. This cannot be undone." : "Unsaved changes on this page will be lost.",
            destructive ? "Cancel invoice" : "Switch",
            destructive);
        ConfirmationResult = result ? "Confirmed" : "Dismissed";
    }
}
