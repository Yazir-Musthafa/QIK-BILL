using System.IO;
using System.Windows;
using System.Windows.Documents;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Nexora.Application;
using Nexora.Domain.Printing;
using Nexora.Printing;
using Nexora.Desktop.Views.Printing;

namespace Nexora.Desktop.Services.Printing;

/// <summary>
/// Preview, print and PDF export for any screen. The rendering, permissions, reprint numbering and recording happen in
/// <see cref="PrintingService"/>; this class only shows windows and messages. UI unverified at runtime.
/// </summary>
public sealed class PrintActions(PrintingService printing, ToastService toasts, ILogger<PrintActions> logger)
{
    public async Task PreviewAsync(PrintSubject subject, PrintPurpose? purpose = null)
    {
        try
        {
            var document = await printing.PreviewAsync(subject, purpose);
            var model = new PrintPreviewModel(this, subject, purpose, WpfDocumentBuilder.Build(document), document.Title,
                $"{document.Pages.Count} page(s). Purpose: {purpose ?? PrintingService.DefaultPurpose(subject)}. The preview shows the next print, including any DUPLICATE mark.");
            var window = new PrintPreviewWindow { DataContext = model, Owner = System.Windows.Application.Current?.MainWindow };
            model.CloseRequested += window.Close;
            window.Show();
        }
        catch (WorkspaceValidationException ex)
        {
            toasts.Show(ex.Message, ToastKind.Error);
        }
    }

    /// <summary>Prints and reports the outcome. Failures show the printer's message with recovery steps.</summary>
    public async Task<bool> PrintAsync(PrintSubject subject, PrintPurpose? purpose = null, int? copies = null)
    {
        try
        {
            var result = await printing.PrintAsync(subject, purpose, copies);
            if (result.Succeeded)
            {
                toasts.Show(result.Message + " Check the printer for the paper.", ToastKind.Success);
                return true;
            }

            MessageBox.Show(result.Message + Environment.NewLine + Environment.NewLine + result.Guidance, "Printing did not complete", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        catch (WorkspaceValidationException ex)
        {
            toasts.Show(ex.Message, ToastKind.Error);
            return false;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogError("Printing failed; error type {ErrorType}", ex.GetType().FullName);
            MessageBox.Show("Printing failed unexpectedly. " + PrinterGuidance.For(PrintFailure.Unknown), "Printing did not complete", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    public async Task ExportPdfAsync(PrintSubject subject, PrintPurpose? purpose = null)
    {
        try
        {
            var pdf = await printing.ExportPdfAsync(subject, purpose);
            var dialog = new SaveFileDialog { FileName = pdf.FileName, DefaultExt = ".pdf", Filter = "PDF files (*.pdf)|*.pdf", AddExtension = true };
            if (dialog.ShowDialog() != true)
            {
                toasts.Show("PDF not saved. The export was still recorded in the print history.", ToastKind.Information);
                return;
            }

            await File.WriteAllBytesAsync(dialog.FileName, pdf.Content);
            toasts.Show($"Saved {Path.GetFileName(dialog.FileName)}.", ToastKind.Success);
        }
        catch (WorkspaceValidationException ex)
        {
            toasts.Show(ex.Message, ToastKind.Error);
        }
        catch (IOException ex)
        {
            toasts.Show("The PDF could not be saved: " + ex.Message, ToastKind.Error);
        }
    }
}

/// <summary>Backs the preview window: the rendered pages plus Print, Export PDF and Close.</summary>
public sealed partial class PrintPreviewModel(PrintActions actions, PrintSubject subject, PrintPurpose? purpose, FixedDocument document, string title, string summary)
    : ObservableObject
{
    public FixedDocument Document { get; } = document;

    public string Title { get; } = title;

    public string Summary { get; } = summary;

    [ObservableProperty] private string _copies = "1";
    [ObservableProperty] private string? _status;

    public event Action? CloseRequested;

    [RelayCommand]
    private async Task PrintAsync()
    {
        if (!int.TryParse(Copies, out var copies) || copies is < 1 or > 20)
        {
            Status = "Copies must be a number from 1 to 20.";
            return;
        }

        if (await actions.PrintAsync(subject, purpose, copies)) CloseRequested?.Invoke();
    }

    [RelayCommand]
    private Task ExportPdfAsync() => actions.ExportPdfAsync(subject, purpose);

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();
}
