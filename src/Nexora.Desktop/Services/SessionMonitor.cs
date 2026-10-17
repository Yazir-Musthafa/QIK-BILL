using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Nexora.Application;
using Nexora.Application.Abstractions;
using Nexora.Desktop.ViewModels;

namespace Nexora.Desktop.Services;

/// <summary>Observes input across all application windows. Services enforce expiry even before the next UI tick.</summary>
public sealed class SessionMonitor(ISessionContext session, IWorkspaceService workspace, MainViewModel main,
    ILogger<SessionMonitor> logger, Nexora.Application.Licensing.LocalAdministrator admin) : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private SessionInfo? _observed;
    private long _lastRefresh = Environment.TickCount64;
    private bool _refreshing;

    public void Start()
    {
        InputManager.Current.PreProcessInput += OnInput;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnInput(object sender, PreProcessInputEventArgs e)
    {
        if (e.StagingItem.Input is not (KeyEventArgs or MouseEventArgs or TextCompositionEventArgs or TouchEventArgs)) return;
        admin.RecordActivity();
        session.RecordActivity();
        if (session.Current is null) main.HideSession();
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or SessionSwitchReason.RemoteDisconnect)
            Lock();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode is PowerModes.Suspend or PowerModes.Resume) Lock();
    }

    private void Lock()
    {
        // Invalidate service access immediately, even if the UI dispatcher is busy.
        session.Set(null);
        admin.SignOut();
        _timer.Dispatcher.BeginInvoke(main.HideSession);
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        var current = session.Current;
        if (current is null) { _observed = null; main.HideSession(); return; }
        if (_refreshing) return;
        _observed ??= current;
        if (Environment.TickCount64 - _lastRefresh < 60_000) return;
        _refreshing = true;
        try
        {
            await workspace.RefreshSessionAsync();
            if (session.Current is { } refreshed && !ReferenceEquals(_observed, refreshed))
            {
                _observed = refreshed;
                await main.RefreshIdentityAsync(refreshed);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("Session refresh failed; error type {ErrorType}", ex.GetType().FullName);
            Lock();
        }
        finally { _refreshing = false; _lastRefresh = Environment.TickCount64; }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
        InputManager.Current.PreProcessInput -= OnInput;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }
}
