using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Nexora.Infrastructure.Persistence.Providers;

/// <summary>
/// Selects and configures the EF Core provider. Adding SQL Server or PostgreSQL means adding an
/// implementation plus a provider-specific migrations assembly; Domain and Application do not change.
/// </summary>
public interface IDatabaseProvider
{
    string Name { get; }

    void Configure(DbContextOptionsBuilder builder);

    /// <summary>Runs provider-specific steps after migrations, such as connection pragmas.</summary>
    Task AfterMigrateAsync(NexoraDbContext context, CancellationToken cancellationToken);
}

public sealed class SqliteDatabaseProvider : IDatabaseProvider
{
    private readonly string _connectionString;

    public SqliteDatabaseProvider(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
        }.ToString();
    }

    public string Name => "SQLite";

    public void Configure(DbContextOptionsBuilder builder) =>
        builder.UseSqlite(_connectionString, sqlite => sqlite.MigrationsAssembly(typeof(SqliteDatabaseProvider).Assembly.FullName));

    public async Task AfterMigrateAsync(NexoraDbContext context, CancellationToken cancellationToken)
    {
        // Write-ahead logging survives crashes better and lets readers proceed during writes.
        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
    }
}
