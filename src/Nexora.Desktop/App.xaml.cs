using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexora.Accounting;
using Nexora.Application;
using Nexora.Desktop.Services;
using Nexora.Desktop.ViewModels;
using Nexora.Infrastructure;
using Nexora.Infrastructure.Persistence.Providers;
using Nexora.Infrastructure.Storage;
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
            _instanceMutex = new Mutex(true, "Local\\QIKSuite.Desktop", out _ownsMutex);
            if (!_ownsMutex)
            {
                MessageBox.Show("QIK Suite is already running. Switch to its open window to continue.",
                    "QIK Suite", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            // Creates %LocalAppData%\QIKSuite, copying data across from the pre-rename Nexora folder if needed.
            var data = DataDirectory.Prepare(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            var dataDirectory = data.Path;
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(new JsonFormatter(), Path.Combine(dataDirectory, "logs", "qiksuite-.jsonl"),
                    rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14,
                    fileSizeLimitBytes: 5 * 1024 * 1024, rollOnFileSizeLimit: true)
                .CreateLogger();

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            _services = ConfigureServices(data.DatabasePath).BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";
            Log.Information("Application starting; version {Version}; runtime {Runtime}", version, Environment.Version.ToString());
            if (data.MigratedFromLegacy) Log.Information("Data copied from the previous Nexora data folder; the old folder was left in place");
            var workspace = _services.GetRequiredService<IWorkspaceService>();
            await workspace.InitializeAsync();
            var window = _services.GetRequiredService<MainWindow>();
            MainWindow = window;
            await window.InitializeAsync();
            window.Show();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            Log.Information("Workspace initialization completed");
        }
        catch (Exception exception)
        {
            // Exception messages can contain paths, entered values or connection strings.
            // Log only the type and a controlled operation name, never credentials or business data.
            Log.Fatal("Startup failed; error type {ErrorType}", exception.GetType().FullName);
            MessageBox.Show("QIK Suite could not start. Check that your Windows user can write to its local application-data folder, " +
                "close other QIK Suite windows, and try again. Your existing company data has not been intentionally removed.",
                "QIK Suite could not start", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    /// <summary>Composition root. Public so the registration can be verified without starting WPF.</summary>
    public static IServiceCollection ConfigureServices(string databasePath)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog(dispose: false));
        services.AddNexoraApplication();
        services.AddNexoraAccounting();
        services.AddNexoraInfrastructure(new SqliteDatabaseProvider(databasePath));
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<DialogService>();
        services.AddSingleton<ToastService>();
        services.AddSingleton<MainViewModel>();
        services.AddTransient<SetupWizardViewModel>();
        services.AddTransient<LoginViewModel>();
        services.AddTransient<ViewModels.Settings.ChangePasswordViewModel>();
        services.AddSingleton<MainWindow>();
        return services;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled interface error; error type {ErrorType}", e.Exception.GetType().FullName);
        e.Handled = true;
        MessageBox.Show("QIK Suite encountered an unexpected error and must close. Reopen the application and retry your last action. " +
            "If the problem continues, contact your administrator with the time of the error.",
            "QIK Suite needs to close", MessageBoxButton.OK, MessageBoxImage.Error);
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
