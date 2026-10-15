using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Nexora.Application;
using Nexora.Desktop.Services;
using Nexora.Infrastructure.Backup;
using Nexora.Reporting.Export;
using Nexora.Reporting.Import;

namespace Nexora.Desktop.ViewModels.Settings;

public sealed record ImportPreviewRow(int Row, string Summary, string Errors);

public sealed partial class ImportExportViewModel(DataTransferService transfer, DialogService dialogs, ToastService toasts,
    ILogger<ImportExportViewModel> logger) : PageViewModel(logger, toasts), ILoadable
{
    private byte[]? _content;
    public string Title => "Import/Export";
    public IReadOnlyList<ImportKind> Kinds { get; } = Enum.GetValues<ImportKind>();
    public IReadOnlyList<ExportFormat> Formats { get; } = [ExportFormat.Csv, ExportFormat.Excel];
    [ObservableProperty] private ImportKind _kind;
    [ObservableProperty] private ExportFormat _format;
    [ObservableProperty] private string _fileName = string.Empty;
    [ObservableProperty] private IReadOnlyList<ImportColumn> _columns = DataTransferService.Columns(ImportKind.Products);
    [ObservableProperty] private IReadOnlyList<ImportPreviewRow> _rows = [];
    [ObservableProperty] private string _summary = "Download a template, fill it in, then choose the file. Imports add new entries only.";
    [ObservableProperty] private bool _canImport;
    public Task LoadAsync() => Task.CompletedTask;
    partial void OnKindChanged(ImportKind value)
    {
        _content = null; FileName = string.Empty; Rows = []; CanImport = false;
        Columns = DataTransferService.Columns(value);
        Summary = "Choose a file for " + DataTransferService.Label(value) + ".";
    }
    [RelayCommand] private Task TemplateAsync() => RunAsync(() => DataFiles.SaveAsync(transfer.Template(Kind, Format)));
    [RelayCommand] private Task ExportAsync() => RunAsync(async () => await DataFiles.SaveAsync(await transfer.ExportAsync(Kind, Format)));
    [RelayCommand] private async Task ChooseFileAsync()
    {
        var dialog = new OpenFileDialog { Filter = "CSV or Excel (*.csv;*.xlsx)|*.csv;*.xlsx", CheckFileExists = true };
        if (dialog.ShowDialog() != true) return;
        CanImport = false; Rows = []; _content = null;
        await RunAsync(async () =>
        {
            FileName = Path.GetFileName(dialog.FileName);
            if (new FileInfo(dialog.FileName).Length > 10 * 1024 * 1024) throw new WorkspaceValidationException("Choose a file no larger than 10 MB.");
            _content = await File.ReadAllBytesAsync(dialog.FileName);
            Show(await transfer.PreviewAsync(Kind, _content, FileName));
        });
    }
    [RelayCommand] private Task PreviewAsync() => RunAsync(async () =>
    {
        CanImport = false;
        if (_content is null) throw new WorkspaceValidationException("Choose a file first.");
        Show(await transfer.PreviewAsync(Kind, _content, FileName));
    });
    private void Show(ImportPreview preview)
    {
        Rows = preview.Rows.Select(r => new ImportPreviewRow(r.RowNumber, r.Summary, string.Join("; ", r.Errors))).ToList();
        Summary = $"{preview.ValidRows} valid row(s), {preview.ErrorRows} with errors. " + string.Join(" ", preview.FileErrors);
        CanImport = preview.CanImport;
    }
    [RelayCommand] private async Task ImportAsync()
    {
        if (!CanImport || _content is null) return;
        if (!await dialogs.ConfirmAsync("Import these rows?", $"Add {Rows.Count} {DataTransferService.Label(Kind).ToLowerInvariant()} entries to this company? Every row is checked again before saving.", "Import")) return;
        await RunAsync(async () =>
        {
            try
            {
                Summary = (await transfer.ImportAsync(Kind, _content, FileName)).Summary;
                CanImport = false; _content = null;
                Toasts.Show(Summary, ToastKind.Success);
            }
            catch (ImportRejectedException ex) { Show(ex.Preview); throw; }
        });
    }
}

public sealed partial class BackupViewModel(BackupService backups, BackupMonitor monitor, DialogService dialogs, ShellContext shell,
    ToastService toasts, ILogger<BackupViewModel> logger) : PageViewModel(logger, toasts), ILoadable
{
    public string Title => "Backup";
    [ObservableProperty] private string _folder = string.Empty;
    [ObservableProperty] private string _backupPassword = string.Empty;
    [ObservableProperty] private string _backupFile = string.Empty;
    [ObservableProperty] private bool _automatic;
    [ObservableProperty] private string _intervalHours = "24";
    [ObservableProperty] private string _keepCount = "10";
    [ObservableProperty] private string _automaticFolder = string.Empty;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private string _verification = string.Empty;
    [ObservableProperty] private IReadOnlyList<BackupRecord> _history = [];
    public Task LoadAsync() => RunAsync(() =>
    {
        var settings = backups.Settings();
        Folder = backups.DefaultFolder;
        (Automatic, IntervalHours, KeepCount, AutomaticFolder) = (settings.Enabled, settings.IntervalHours.ToString(), settings.KeepCount.ToString(), settings.Folder);
        Refresh();
        return Task.CompletedTask;
    });
    private void Refresh()
    {
        Status = backups.Status().Message;
        History = backups.Settings().Records.Reverse().ToList();
        monitor.Refresh();
    }
    [RelayCommand] private void ChooseFolder() { if (DataFiles.Folder(Folder) is { } folder) Folder = folder; }
    [RelayCommand] private void ChooseAutomaticFolder() { if (DataFiles.Folder(AutomaticFolder) is { } folder) AutomaticFolder = folder; }
    [RelayCommand] private void ChooseFile()
    {
        var dialog = new OpenFileDialog { Filter = "QIK Suite backups (*.qikbak)|*.qikbak", CheckFileExists = true };
        if (dialog.ShowDialog() == true) { BackupFile = dialog.FileName; Verification = string.Empty; }
    }
    private string? Password => string.IsNullOrEmpty(BackupPassword) ? null : BackupPassword;
    [RelayCommand] private async Task CreateAsync()
    {
        var folder = Folder;
        var password = Password;
        await RunAsync(async () =>
        {
            BackupFile = await Task.Run(() => backups.CreateAsync(folder, password));
            Verification = "Created " + BackupFile;
            Refresh();
        });
    }
    [RelayCommand] private Task VerifyAsync() => RunAsync(async () =>
    {
        Verification = string.Empty;
        Verification = (await Task.Run(() => backups.VerifyAsync(BackupFile, Password))).Message;
    });
    [RelayCommand] private async Task RestoreAsync()
    {
        var path = BackupFile;
        var password = Password;
        BackupVerification? verified = null;
        if (!await RunAsync(async () => verified = await Task.Run(() => backups.VerifyAsync(path, password)))) return;
        if (!await dialogs.ConfirmAsync("Replace ALL companies on this computer?",
            $"Restore the backup made {verified!.Header.CreatedUtc.ToLocalTime():dd-MM-yyyy HH:mm} containing {string.Join(", ", verified.Header.Companies)}? All current companies, users, transactions and audit history will be replaced. A safety backup is saved first. You will be signed out.", "Replace all data", true)) return;
        monitor.IsRestoring = true;
        try
        {
            if (await RunAsync(async () =>
            {
                var result = await Task.Run(() => backups.RestoreAsync(path, password));
                BackupPassword = string.Empty;
                Toasts.Show(result.Message + " Safety copy: " + result.SafetyBackupPath, ToastKind.Success);
            })) await shell.CompaniesChanged();
        }
        finally { monitor.IsRestoring = false; }
    }
    [RelayCommand] private Task SaveSettingsAsync() => RunAsync(async () =>
    {
        if (!int.TryParse(IntervalHours, out var interval) || !int.TryParse(KeepCount, out var keep)) throw new WorkspaceValidationException("Enter whole numbers for interval and retention.");
        await backups.SaveSettingsAsync(Automatic, interval, AutomaticFolder, keep);
        Refresh();
    }, "Automatic backup settings saved.");
    [RelayCommand] private async Task CheckScheduleAsync()
    {
        await RunAsync(async () => { await monitor.CheckAsync(); Refresh(); });
    }
}
