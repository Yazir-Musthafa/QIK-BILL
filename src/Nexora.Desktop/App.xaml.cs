using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Nexora.Application;
using Nexora.Infrastructure;
using Serilog;
using Serilog.Formatting.Json;

namespace Nexora.Desktop;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;
    private Mutex? _instanceMutex;
    private bool _ownsMutex;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            _instanceMutex = new Mutex(true, "Local\\Nexora.BusinessSuite.Desktop", out _ownsMutex);
            if (!_ownsMutex)
            {
                MessageBox.Show("Nexora is already running. Switch to its open window to continue.",
                    "Nexora Business Suite", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Nexora", "BusinessSuite");
            Directory.CreateDirectory(dataDirectory);
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(new JsonFormatter(), Path.Combine(dataDirectory, "logs", "nexora-.jsonl"),
                    rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14,
                    fileSizeLimitBytes: 5 * 1024 * 1024, rollOnFileSizeLimit: true)
                .CreateLogger();

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            var services = new ServiceCollection();
            services.AddSingleton<IWorkspaceService>(_ => new SqliteWorkspaceService(Path.Combine(dataDirectory, "nexora.db")));
            services.AddSingleton<MainWindow>();
            _services = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

            Log.Information("Application starting; version {Version}; runtime {Runtime}", "0.1.0", Environment.Version.ToString());
            var workspace = _services.GetRequiredService<IWorkspaceService>();
            await workspace.InitializeAsync();
            var window = _services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            await window.InitializeAsync();
            Log.Information("Workspace initialization completed");
        }
        catch (Exception exception)
        {
            // Exception messages can contain paths, entered values or connection strings.
            // Log only the type and a controlled operation name, never credentials or business data.
            Log.Fatal("Startup failed; error type {ErrorType}", exception.GetType().FullName);
            MessageBox.Show("Nexora could not start. Check that your Windows user can write to its local application-data folder, " +
                "close other Nexora windows, and try again. Your existing company data has not been intentionally removed.",
                "Nexora could not start", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled interface error; error type {ErrorType}", e.Exception.GetType().FullName);
        e.Handled = true;
        MessageBox.Show("Nexora encountered an unexpected error and must close. Reopen the application and retry your last action. " +
            "If the problem continues, contact your administrator with the time of the error.",
            "Nexora needs to close", MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown(1);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error("Unobserved background error; error type {ErrorType}", e.Exception.GetType().FullName);
        e.SetObserved();
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Log.Fatal("Process terminated after an unhandled error; error type {ErrorType}",
            e.ExceptionObject.GetType().FullName);
        Log.CloseAndFlush();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        _services?.Dispose();
        Log.Information("Application stopped; exit code {ExitCode}", e.ApplicationExitCode);
        Log.CloseAndFlush();
        if (_ownsMutex) _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
