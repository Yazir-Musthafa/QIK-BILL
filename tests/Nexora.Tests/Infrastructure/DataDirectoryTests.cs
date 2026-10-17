using Microsoft.Data.Sqlite;
using Nexora.Infrastructure.Storage;

namespace Nexora.Tests.Infrastructure;

/// <summary>Upgrade from the pre-rename data folder (%LocalAppData%\Nexora\BusinessSuite) to %LocalAppData%\QIKSuite.</summary>
public sealed class DataDirectoryTests : IDisposable
{
    private readonly string _localAppData = Path.Combine(Path.GetTempPath(), "qiksuite-tests", Guid.NewGuid().ToString("N"));

    public DataDirectoryTests() => Directory.CreateDirectory(_localAppData);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_localAppData, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Legacy => DataDirectory.LegacyPath(_localAppData);

    private string Target => Path.Combine(_localAppData, "QIKSuite");

    [Fact]
    public void Fresh_install_creates_the_new_folder_and_migrates_nothing()
    {
        var result = DataDirectory.Prepare(_localAppData);

        Assert.False(result.MigratedFromLegacy);
        Assert.Equal(Target, result.Path);
        Assert.Equal(Path.Combine(Target, "qiksuite.db"), result.DatabasePath);
        Assert.True(Directory.Exists(Target));
        Assert.False(Directory.Exists(Legacy));
    }

    [Fact]
    public void Legacy_database_is_copied_renamed_and_still_readable()
    {
        Directory.CreateDirectory(Legacy);
        var legacyDb = Path.Combine(Legacy, "nexora.db");
        using (var connection = new SqliteConnection($"Data Source={legacyDb};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE Companies (Name TEXT); INSERT INTO Companies VALUES ('Acme Traders');";
            command.ExecuteNonQuery();
        }

        var result = DataDirectory.Prepare(_localAppData);

        Assert.True(result.MigratedFromLegacy);
        Assert.True(File.Exists(result.DatabasePath));
        using var copy = new SqliteConnection($"Data Source={result.DatabasePath};Mode=ReadOnly;Pooling=False");
        copy.Open();
        using var query = copy.CreateCommand();
        query.CommandText = "SELECT Name FROM Companies";
        Assert.Equal("Acme Traders", query.ExecuteScalar());
    }

    [Fact]
    public void Write_ahead_log_and_logs_come_across_and_the_legacy_folder_is_kept()
    {
        Directory.CreateDirectory(Path.Combine(Legacy, "logs"));
        File.WriteAllText(Path.Combine(Legacy, "nexora.db"), "main");
        File.WriteAllText(Path.Combine(Legacy, "nexora.db-wal"), "wal");
        File.WriteAllText(Path.Combine(Legacy, "nexora.db-shm"), "shm");
        File.WriteAllText(Path.Combine(Legacy, "logs", "nexora-20261010.jsonl"), "log");

        DataDirectory.Prepare(_localAppData);

        Assert.Equal("main", File.ReadAllText(Path.Combine(Target, "qiksuite.db")));
        Assert.Equal("wal", File.ReadAllText(Path.Combine(Target, "qiksuite.db-wal")));
        Assert.False(File.Exists(Path.Combine(Target, "qiksuite.db-shm")));
        Assert.Equal("log", File.ReadAllText(Path.Combine(Target, "logs", "nexora-20261010.jsonl")));
        Assert.True(File.Exists(Path.Combine(Legacy, "nexora.db")), "The old folder must remain as a backup.");
    }

    [Fact]
    public void Existing_new_folder_is_never_overwritten()
    {
        Directory.CreateDirectory(Legacy);
        File.WriteAllText(Path.Combine(Legacy, "nexora.db"), "old");
        Directory.CreateDirectory(Target);
        File.WriteAllText(Path.Combine(Target, "qiksuite.db"), "new");

        var result = DataDirectory.Prepare(_localAppData);

        Assert.False(result.MigratedFromLegacy);
        Assert.Equal("new", File.ReadAllText(result.DatabasePath));
    }

    [Fact]
    public void Interrupted_earlier_migration_is_redone_from_the_legacy_data()
    {
        Directory.CreateDirectory(Legacy);
        File.WriteAllText(Path.Combine(Legacy, "nexora.db"), "complete");
        Directory.CreateDirectory(Target + ".migrating");
        File.WriteAllText(Path.Combine(Target + ".migrating", "qiksuite.db"), "half");

        var result = DataDirectory.Prepare(_localAppData);

        Assert.True(result.MigratedFromLegacy);
        Assert.Equal("complete", File.ReadAllText(result.DatabasePath));
        Assert.False(Directory.Exists(Target + ".migrating"));
    }

    [Fact]
    public void Running_twice_migrates_only_once()
    {
        Directory.CreateDirectory(Legacy);
        File.WriteAllText(Path.Combine(Legacy, "nexora.db"), "data");

        Assert.True(DataDirectory.Prepare(_localAppData).MigratedFromLegacy);
        Assert.False(DataDirectory.Prepare(_localAppData).MigratedFromLegacy);
    }
}
