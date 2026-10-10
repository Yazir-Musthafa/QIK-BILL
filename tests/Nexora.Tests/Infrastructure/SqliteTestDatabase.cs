using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexora.Accounting;
using Nexora.Application;
using Nexora.Application.Abstractions;
using Nexora.Application.Security;
using Nexora.Infrastructure;
using Nexora.Infrastructure.Persistence.Providers;

namespace Nexora.Tests.Infrastructure;

/// <summary>A real SQLite file in a unique temporary folder, wired through the production DI registrations.</summary>
public sealed class SqliteTestDatabase : IAsyncDisposable
{
    private readonly string _directory;

    public SqliteTestDatabase()
    {
        _directory = Path.Combine(Path.GetTempPath(), "nexora-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        DatabasePath = Path.Combine(_directory, "nexora.db");

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddNexoraApplication();
        services.AddNexoraAccounting();
        services.AddNexoraInfrastructure(new SqliteDatabaseProvider(DatabasePath));
        services.AddSingleton<IPasswordHasher>(new Pbkdf2PasswordHasher(1_000));
        services.AddSingleton<IClock>(Clock);
        Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    public string DatabasePath { get; }

    public TestClock Clock { get; } = new();

    public ServiceProvider Services { get; }

    public IWorkspaceService Workspace => Services.GetRequiredService<IWorkspaceService>();

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    /// <summary>Opens a raw connection for database-level assertions that bypass the application.</summary>
    public SqliteConnection OpenRawConnection()
    {
        var connection = new SqliteConnection($"Data Source={DatabasePath};Foreign Keys=True");
        connection.Open();
        return connection;
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort; the OS temp folder is cleaned eventually.
        }
    }
}

public sealed class TestClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 10, 10, 9, 30, 0, DateTimeKind.Utc);
}
