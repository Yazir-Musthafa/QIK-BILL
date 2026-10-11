using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Nexora.Infrastructure.Persistence;
using Nexora.Tests.Workspace;

namespace Nexora.Tests.Infrastructure;

public sealed class MigrationTests
{
    [Fact]
    public async Task Migrations_create_the_schema_on_a_new_file()
    {
        await using var db = new SqliteTestDatabase();

        await db.Workspace.InitializeAsync();

        Assert.True(File.Exists(db.DatabasePath));
        var tables = await QueryStringsAsync(db.DatabasePath, "SELECT name FROM sqlite_master WHERE type = 'table'");
        Assert.Contains("Companies", tables);
        Assert.Contains("Users", tables);
        Assert.Contains("AppSettings", tables);
        Assert.Contains("__EFMigrationsHistory", tables);
    }

    [Fact]
    public async Task Migrating_twice_is_safe_and_keeps_data()
    {
        await using var db = new SqliteTestDatabase();
        await db.Workspace.InitializeAsync();
        await db.Workspace.CompleteSetupAsync(WorkspaceServiceTests.ValidRequest());

        await db.Workspace.InitializeAsync();

        Assert.False(await db.Workspace.NeedsSetupAsync());
    }

    [Fact]
    public async Task Model_has_no_changes_missing_from_migrations()
    {
        await using var db = new SqliteTestDatabase();
        var factory = db.Services.GetRequiredService<IDbContextFactory<NexoraDbContext>>();
        await using var context = await factory.CreateDbContextAsync();

        Assert.False(context.Database.HasPendingModelChanges(),
            "The EF model changed without a migration. Run 'dotnet ef migrations add <Name>'.");
    }

    [Fact]
    public async Task Database_uses_write_ahead_logging()
    {
        await using var db = new SqliteTestDatabase();
        await db.Workspace.InitializeAsync();

        var mode = await QueryStringsAsync(db.DatabasePath, "PRAGMA journal_mode");
        Assert.Equal("wal", Assert.Single(mode));
    }

    /// <summary>
    /// SQLite table rebuilds (which EF uses for some schema changes) silently drop a table's triggers. The migrations are
    /// applied one at a time, and every trigger that existed after any of them must still exist at the end.
    /// </summary>
    [Fact]
    public async Task Every_trigger_created_by_a_migration_survives_later_migrations()
    {
        await using var db = new SqliteTestDatabase();
        var factory = db.Services.GetRequiredService<IDbContextFactory<NexoraDbContext>>();
        await using var context = await factory.CreateDbContextAsync();
        var migrator = context.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var migration in context.Database.GetMigrations())
        {
            await migrator.MigrateAsync(migration);
            seen.UnionWith(await QueryStringsAsync(db.DatabasePath, "SELECT name FROM sqlite_master WHERE type = 'trigger'"));
        }

        var final = await QueryStringsAsync(db.DatabasePath, "SELECT name FROM sqlite_master WHERE type = 'trigger'");
        Assert.True(seen.Count > 20, "The migrations should define the immutability triggers.");
        Assert.Empty(seen.Except(final));
    }

    private static async Task<List<string>> QueryStringsAsync(string path, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) values.Add(reader.GetString(0));
        return values;
    }
}
