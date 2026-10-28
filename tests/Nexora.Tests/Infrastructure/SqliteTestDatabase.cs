using Nexora.Reporting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexora.Accounting;
using Nexora.Application;
using Nexora.Application.Abstractions;
using Nexora.Application.Security;
using Nexora.Infrastructure;
using Nexora.Infrastructure.Persistence;
using Nexora.Infrastructure.Persistence.Providers;
using Nexora.Printing;

namespace Nexora.Tests.Infrastructure;

/// <summary>A real SQLite file in a unique temporary folder, wired through the production DI registrations.</summary>
public sealed class SqliteTestDatabase : IAsyncDisposable
{
    private readonly string _directory;

    public SqliteTestDatabase()
    {
        _directory = Path.Combine(Path.GetTempPath(), "qiksuite-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        DatabasePath = Path.Combine(_directory, "nexora.db");

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddNexoraApplication();
        services.AddNexoraAccounting();
        services.AddNexoraPrinting();
        services.AddNexoraReporting();
        services.AddSingleton<Nexora.Printing.IPrinterGateway>(Printers);
        services.AddNexoraInfrastructure(new SqliteDatabaseProvider(DatabasePath));
        services.AddSingleton<IPasswordHasher>(new Pbkdf2PasswordHasher(1_000));
        services.AddSingleton<IClock>(Clock);
        Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    public string DatabasePath { get; }

    public TestClock Clock { get; } = new();

    /// <summary>The fake printer layer used by the print centre in tests.</summary>
    public Nexora.Tests.Printing.FakePrinterGateway Printers { get; } = new();

    public ServiceProvider Services { get; }

    public IWorkspaceService Workspace => Services.GetRequiredService<IWorkspaceService>();

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    /// <summary>Opens a raw connection for database-level assertions that bypass the application.</summary>
    public SqliteConnection OpenRawConnection()
    {
        var connection = new SqliteConnection($"Data Source={DatabasePath};Foreign Keys=True;Pooling=False");
        connection.Open();
        return connection;
    }

    public async ValueTask DisposeAsync()
    {
        await ClearPoolAsync();
        await Services.DisposeAsync();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort; the OS temp folder is cleaned eventually.
        }
    }

    // Each test owns a database. Never invalidate another concurrently running test's native connections.
    public async Task ClearPoolAsync()
    {
        await using var context = await Get<IDbContextFactory<NexoraDbContext>>().CreateDbContextAsync();
        SqliteConnection.ClearPool((SqliteConnection)context.Database.GetDbConnection());
    }
}

public sealed class TestClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 10, 10, 9, 30, 0, DateTimeKind.Utc);
}
