using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Nexora.Application;
using Nexora.Infrastructure.Backup;

namespace Nexora.Desktop.Services;

/// <summary>Runs while the desktop is open, including at the sign-in screen. UI unverified at runtime.</summary>
public sealed partial class BackupMonitor(BackupService backups, IWorkspaceService workspace, ILogger<BackupMonitor> logger) : ObservableObject, IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(15) };
    private bool _running;
    [ObservableProperty] private string _statusText = "Backup status";
    [ObservableProperty] private string _notification = string.Empty;
    [ObservableProperty] private bool _needsAttention;
    [ObservableProperty] private bool _isRestoring;
    public void Start()
    {
        _timer.Tick += OnTick;
        _timer.Start();
        _ = CheckAsync();
    }
    private async void OnTick(object? sender, EventArgs e) => await CheckAsync();
    public async Task CheckAsync()
    {
        if (_running) return;
        _running = true;
        try
        {
            await Task.Run(() => backups.RunScheduledIfDueAsync());
            Refresh();
        }
        catch (Exception ex)
        {
            logger.LogError("Backup scheduler failed; error type {ErrorType}", ex.GetType().FullName);
            StatusText = "Backup failed";
            NeedsAttention = true;
            Notification = "Automatic backup could not run. Open Settings → Backup to check its folder and settings.";
        }
        finally { _running = false; }
    }
    public void Refresh()
    {
        if (workspace.CurrentSession is null) return;
        var status = backups.Status();
        NeedsAttention = status.IsOverdue || status.LastFailure is not null;
        StatusText = status.LastFailure is not null ? "Backup failed" : status.IsOverdue ? "Backup overdue" : status.Enabled ? "Backup current" : "Auto backup off";
        Notification = NeedsAttention ? status.Message + " Open Settings → Backup." : string.Empty;
    }
    public void Dispose() { _timer.Stop(); _timer.Tick -= OnTick; }
}
