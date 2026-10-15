using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexora.Application;
using Nexora.Application.Abstractions;
using Nexora.Application.Security;
using Nexora.Domain.Security;
using Nexora.Infrastructure.Persistence;
using Nexora.Infrastructure.Persistence.Providers;

namespace Nexora.Infrastructure.Backup;

public enum BackupKind
{
    Manual,
    Automatic,
    Safety,
}

public sealed record BackupRecord(DateTime Utc, BackupKind Kind, string FileName, bool Succeeded, string Message);

/// <summary>Automatic backup settings and history, kept in a small file next to the database so a restore never overwrites them.</summary>
public sealed record BackupSettings(bool Enabled = false, int IntervalHours = 24, string Folder = "", int KeepCount = 10, IReadOnlyList<BackupRecord>? History = null,
    bool EncryptAutomatic = false, string? ProtectedPassword = null)
{
    public IReadOnlyList<BackupRecord> Records => History ?? [];
}

public sealed record BackupStatus(bool Enabled, DateTime? LastSuccessUtc, BackupRecord? LastFailure, DateTime? NextDueUtc, bool IsOverdue, string Message);

public sealed record BackupVerification(BackupHeader Header, long SizeBytes, string Message);

public sealed record RestoreResult(string SafetyBackupPath, BackupHeader Restored, string Message);

/// <summary>
/// Backups of the whole QIK Suite database (every company on this computer), safe while the app runs (SQLite online
/// backup), versioned and checked (see <see cref="BackupFormat"/>), optionally encrypted. Creating, restoring and
/// changing settings need Backup: Manage in every company, because the file holds them all.
/// Restore verifies first, refuses backups from a newer version, takes a safety backup of the current data, and puts
/// that back if anything fails.
/// </summary>
public sealed class BackupService(SqliteDatabaseProvider provider, IDbContextFactory<NexoraDbContext> contexts, IWorkspaceStore store, IWorkspaceService workspace,
    IAuthorizer authorizer, IClock clock, ILogger<BackupService> logger, ISecretProtector secrets)
{
    public const string Extension = ".qikbak";
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private string DataFolder => Path.GetDirectoryName(provider.DatabasePath)!;

    private string SettingsPath => Path.Combine(DataFolder, "backup-settings.json");

    public string DefaultFolder => Path.Combine(DataFolder, "backups");

    public static string AppVersion => typeof(BackupService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0";

    // ----- Manual backup, verify and restore -----

    public async Task<string> CreateAsync(string folder, string? password = null, CancellationToken cancellationToken = default)
    {
        await DemandAsync(cancellationToken);
        if (password is { Length: < 10 }) throw new WorkspaceValidationException("Use a backup password of at least 10 characters, and keep it safe: without it the backup cannot be restored.");
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await DemandAsync(cancellationToken);
            var path = await CreateCoreAsync(folder, BackupKind.Manual, password, cancellationToken);
            Record(new BackupRecord(clock.UtcNow, BackupKind.Manual, Path.GetFileName(path), true, "Backup created."));
            return path;
        }
        catch (Exception ex) when (ex is not WorkspaceValidationException)
        {
            logger.LogError("Backup failed; error type {ErrorType}", ex.GetType().FullName);
            Record(new BackupRecord(clock.UtcNow, BackupKind.Manual, string.Empty, false, "The backup could not be written. Check the folder exists and has space."));
            throw new WorkspaceValidationException("The backup could not be written. Check the folder exists, is writable and has space.", ex);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Checks a backup completely (file hash, decryption, payload hash, SQLite integrity and schema) without restoring it.</summary>
    public async Task<BackupVerification> VerifyAsync(string path, string? password = null, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.BackupManage);
        var temp = TempDatabase();
        try
        {
            var header = await ExtractCheckedAsync(path, password, temp, cancellationToken);
            return new BackupVerification(header, new FileInfo(path).Length,
                $"The backup is complete and readable: {header.Companies.Count} compan{(header.Companies.Count == 1 ? "y" : "ies")}, made {header.CreatedUtc.ToLocalTime():dd-MM-yyyy HH:mm} by QIK Suite {header.AppVersion}.");
        }
        finally
        {
            DeleteDatabase(temp);
        }
    }

    public async Task<RestoreResult> RestoreAsync(string path, string? password = null, CancellationToken cancellationToken = default)
    {
        await DemandAsync(cancellationToken);
        await Gate.WaitAsync(cancellationToken);
        var temp = TempDatabase();
        string? safety = null;
        try
        {
            var header = await ExtractCheckedAsync(path, password, temp, cancellationToken);
            await DemandAsync(cancellationToken);
            // The restore password also protects its safety copy. Otherwise use the configured automatic password.
            var safetyPassword = password ?? AutomaticPassword(LoadSettings());
            safety = await CreateCoreAsync(Path.Combine(DefaultFolder, "safety"), BackupKind.Safety, safetyPassword, cancellationToken);
            Record(new BackupRecord(clock.UtcNow, BackupKind.Safety, Path.GetFileName(safety), true, "Safety backup before a restore."));
            try
            {
                await CopyDatabaseAsync(temp, provider.DatabasePath, cancellationToken);
                await store.MigrateAsync(cancellationToken);
                await workspace.InitializeAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError("Restore failed, putting the previous data back; error type {ErrorType}", ex.GetType().FullName);
                var back = TempDatabase();
                try
                {
                    BackupFormat.Extract(safety, safetyPassword, back);
                    await CopyDatabaseAsync(back, provider.DatabasePath, CancellationToken.None);
                    // Recovery must finish even when the caller cancelled the restore.
                    await store.MigrateAsync(CancellationToken.None);
                }
                finally
                {
                    DeleteDatabase(back);
                }

                throw new WorkspaceValidationException("The restore did not complete, so your previous data was put back unchanged. " +
                    (ex is WorkspaceValidationException w ? w.Message : "Try again, or use another backup."));
            }

            workspace.SignOut();
            logger.LogInformation("Backup restored; made {Created}; schema {Migration}", header.CreatedUtc, header.LatestMigration);
            return new RestoreResult(safety, header, "The backup was restored. Sign in again. Your data from before the restore is kept in a safety backup.");
        }
        finally
        {
            DeleteDatabase(temp);
            Gate.Release();
        }
    }

    // ----- Settings, status and automatic backups -----

    public BackupSettings Settings()
    {
        authorizer.Demand(Permissions.BackupManage);
        return LoadSettings() with { ProtectedPassword = null };
    }

    public async Task SaveSettingsAsync(bool enabled, int intervalHours, string folder, int keepCount, CancellationToken cancellationToken = default,
        bool encryptAutomatic = false, string? automaticPassword = null)
    {
        await DemandAsync(cancellationToken);
        if (intervalHours is < 1 or > 24 * 31) throw new WorkspaceValidationException("Back up every 1 to 744 hours.");
        if (keepCount is < 1 or > 365) throw new WorkspaceValidationException("Keep between 1 and 365 automatic backups.");
        var clean = (folder ?? string.Empty).Trim();
        if (clean.Length > 0 && !Path.IsPathRooted(clean)) throw new WorkspaceValidationException("Choose a full folder path, for example D:\\Backups\\QIK.");
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await DemandAsync(cancellationToken);
            var settings = LoadSettings();
            var protectedPassword = settings.ProtectedPassword;
            if (encryptAutomatic)
            {
                if (automaticPassword is { Length: > 0 })
                {
                    if (automaticPassword.Length is < 10 or > 256) throw new WorkspaceValidationException("Use a backup password of 10 to 256 characters.");
                    protectedPassword = secrets.Protect(automaticPassword);
                }
                if (string.IsNullOrEmpty(protectedPassword)) throw new WorkspaceValidationException("Enter a password before enabling encrypted automatic backups.");
                secrets.Unprotect(protectedPassword); // Fail before saving if this account cannot open the secret.
            }
            SaveSettings(settings with { Enabled = enabled, IntervalHours = intervalHours, Folder = clean, KeepCount = keepCount,
                EncryptAutomatic = encryptAutomatic, ProtectedPassword = encryptAutomatic ? protectedPassword : null });
        }
        finally { Gate.Release(); }
    }

    public BackupStatus Status()
    {
        authorizer.RequireSession();
        return StatusCore(LoadSettings());
    }

    /// <summary>
    /// Makes an automatic backup when one is due (called on start-up and by a timer) and deletes the oldest automatic
    /// backups beyond the kept number. Runs without a signed-in user; only an authorised user can switch it on.
    /// Optional encryption uses a password protected by Windows DPAPI. A failed unprotect never falls back to plaintext.
    /// </summary>
    public async Task<BackupRecord?> RunScheduledIfDueAsync(CancellationToken cancellationToken = default)
    {
        var settings = LoadSettings();
        if (!settings.Enabled || !File.Exists(provider.DatabasePath)) return null;
        var status = StatusCore(settings);
        if (status.NextDueUtc is { } due && due > clock.UtcNow) return null;
        if (!await Gate.WaitAsync(0, cancellationToken)) return null;
        try
        {
            settings = LoadSettings();
            if (!settings.Enabled || StatusCore(settings).NextDueUtc > clock.UtcNow) return null;
            var folder = settings.Folder.Length > 0 ? settings.Folder : DefaultFolder;
            var path = await CreateCoreAsync(folder, BackupKind.Automatic, AutomaticPassword(settings), cancellationToken);
            foreach (var old in Directory.GetFiles(folder, "QIKSuite-auto-*" + Extension).OrderByDescending(f => f, StringComparer.Ordinal).Skip(settings.KeepCount))
                File.Delete(old);
            var record = new BackupRecord(clock.UtcNow, BackupKind.Automatic, Path.GetFileName(path), true, "Automatic backup created.");
            Record(record);
            return record;
        }
        catch (Exception ex)
        {
            logger.LogError("Automatic backup failed; error type {ErrorType}", ex.GetType().FullName);
            var record = new BackupRecord(clock.UtcNow, BackupKind.Automatic, string.Empty, false,
                ex is WorkspaceValidationException w ? w.Message : "The automatic backup could not be written. Check the backup folder exists and has space.");
            Record(record);
            return record;
        }
        finally
        {
            Gate.Release();
        }
    }

    private BackupStatus StatusCore(BackupSettings settings)
    {
        var now = clock.UtcNow;
        var lastSuccess = settings.Records.Where(r => r.Succeeded && r.Kind != BackupKind.Safety).Select(r => (DateTime?)r.Utc).Max();
        // History is append-ordered; two attempts can have the same clock timestamp.
        var lastAutomatic = settings.Records.LastOrDefault(r => r.Kind == BackupKind.Automatic);
        var failure = lastAutomatic is { Succeeded: false } ? lastAutomatic : null;
        DateTime? next = settings.Enabled ? (lastSuccess ?? DateTime.MinValue).AddHours(settings.IntervalHours) : null;
        var overdue = settings.Enabled && (lastSuccess is null || now - lastSuccess.Value > TimeSpan.FromHours(settings.IntervalHours * 1.5));
        var message = !settings.Enabled ? "Automatic backups are off."
            : failure is not null ? $"The last automatic backup failed: {failure.Message}"
            : overdue ? lastSuccess is null ? "No backup has been made yet." : $"The last backup was on {lastSuccess.Value.ToLocalTime():dd-MM-yyyy HH:mm}; a backup is overdue."
            : $"Last backup {lastSuccess!.Value.ToLocalTime():dd-MM-yyyy HH:mm}.";
        return new BackupStatus(settings.Enabled, lastSuccess, failure, next, overdue || failure is not null, message);
    }

    // ----- Core -----

    private string? AutomaticPassword(BackupSettings settings)
    {
        if (!settings.EncryptAutomatic) return null;
        if (string.IsNullOrEmpty(settings.ProtectedPassword)) throw new WorkspaceValidationException("The automatic backup password is missing. Save it again in Backup settings.");
        return secrets.Unprotect(settings.ProtectedPassword);
    }

    private async Task<string> CreateCoreAsync(string folder, BackupKind kind, string? password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathRooted(folder)) throw new WorkspaceValidationException("Choose a full folder path for the backup.");
        Directory.CreateDirectory(folder);
        var now = clock.UtcNow;
        var name = kind switch
        {
            BackupKind.Automatic => $"QIKSuite-auto-{now:yyyyMMdd-HHmmss}",
            BackupKind.Safety => $"QIKSuite-before-restore-{now:yyyyMMdd-HHmmss}",
            _ => $"QIKSuite-{now:yyyyMMdd-HHmmss}",
        };
        var target = UniquePath(Path.Combine(folder, name + Extension));
        var copy = TempDatabase();
        var compressed = copy + ".gz";
        var partial = target + ".partial";
        try
        {
            // SQLite's online backup copies a consistent snapshot even while the app is writing.
            await using (var source = new SqliteConnection($"Data Source={provider.DatabasePath};Mode=ReadOnly;Pooling=False"))
            await using (var destination = new SqliteConnection($"Data Source={copy};Pooling=False"))
            {
                await source.OpenAsync(cancellationToken);
                await destination.OpenAsync(cancellationToken);
                await CopyPagesAsync(source, destination, cancellationToken);
            }

            var (migrations, companies) = await InspectAsync(copy, cancellationToken);
            BackupFormat.Compress(copy, compressed);
            var header = new BackupHeader(BackupFormat.Version, AppVersion, now, migrations.LastOrDefault() ?? string.Empty, migrations, companies,
                false, 0, string.Empty, string.Empty, BackupFormat.ChunkSize, string.Empty);
            await using (var output = File.Create(partial)) BackupFormat.Write(output, header, compressed, password);
            File.Move(partial, target);
            return target;
        }
        finally
        {
            DeleteDatabase(copy);
            File.Delete(compressed);
            File.Delete(partial);
        }
    }

    private async Task<BackupHeader> ExtractCheckedAsync(string path, string? password, string temp, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) throw new WorkspaceValidationException("The backup file was not found.");
        var header = BackupFormat.Extract(path, password, temp);
        var (migrations, _) = await InspectAsync(temp, cancellationToken);
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        var known = context.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
        if (migrations.Any(m => !known.Contains(m)))
            throw new WorkspaceValidationException("This backup was made by a newer version of QIK Suite (its database has changes this version does not know). Update QIK Suite, then restore it.");
        if (!migrations.SequenceEqual(header.Migrations) || header.LatestMigration != (migrations.LastOrDefault() ?? string.Empty))
            throw new WorkspaceValidationException("The backup's schema information does not match its database. Use another backup.");
        return header;
    }

    /// <summary>Opens a database copy, runs SQLite's integrity check, and reads its migrations and company names.</summary>
    private static async Task<(List<string> Migrations, List<string> Companies)> InspectAsync(string databasePath, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using (var check = connection.CreateCommand())
            {
                check.CommandText = "PRAGMA integrity_check";
                if (await check.ExecuteScalarAsync(cancellationToken) as string != "ok") throw new WorkspaceValidationException("The database inside the backup is damaged.");
                check.CommandText = "PRAGMA foreign_key_check";
                if (await check.ExecuteScalarAsync(cancellationToken) is not null) throw new WorkspaceValidationException("The database inside the backup has broken references.");
            }

            async Task<List<string>> Strings(string sql)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                var values = new List<string>();
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken)) values.Add(reader.GetString(0));
                return values;
            }

            return (await Strings("SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId"), await Strings("SELECT Name FROM Companies ORDER BY Name"));
        }
        catch (SqliteException)
        {
            throw new WorkspaceValidationException("The backup does not contain a QIK Suite database.");
        }
    }

    /// <summary>Overwrites the live database page by page with SQLite's backup API (one transaction), after closing pooled connections.</summary>
    private static async Task CopyDatabaseAsync(string source, string destination, CancellationToken cancellationToken)
    {
        SqliteConnection.ClearAllPools();
        using (var from = new SqliteConnection($"Data Source={source};Mode=ReadOnly;Pooling=False"))
        using (var to = new SqliteConnection($"Data Source={destination};Pooling=False"))
        {
            from.Open();
            to.Open();
            await CopyPagesAsync(from, to, cancellationToken);
        }

        SqliteConnection.ClearAllPools();
    }

    private static async Task CopyPagesAsync(SqliteConnection source, SqliteConnection destination, CancellationToken cancellationToken)
    {
        // BackupDatabase does not apply the SQL command busy timeout. WAL checkpoint/connection cleanup can
        // briefly hold the file lock, so retry the entire SQLite backup transaction on BUSY or LOCKED only.
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                source.BackupDatabase(destination);
                return;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6 && deadline.Elapsed < TimeSpan.FromSeconds(10))
            {
                await Task.Delay(50, cancellationToken);
            }
        }
    }

    private async Task DemandAsync(CancellationToken cancellationToken)
    {
        var session = authorizer.Demand(Permissions.BackupManage);
        var access = await store.GetAccessAsync(session.UserId, cancellationToken);
        var companies = await store.ListAllCompaniesAsync(cancellationToken);
        var allowed = access.Where(a => a.Role.Has(Permissions.BackupManage)).Select(a => a.Company.Id).ToHashSet();
        if (companies.Any(c => !allowed.Contains(c.Id)))
            throw new AccessDeniedException("A backup contains every company on this computer. You need the \"Backup: Manage\" permission in all of them to back up or restore.");
    }

    private BackupSettings LoadSettings()
    {
        try
        {
            return File.Exists(SettingsPath) ? JsonSerializer.Deserialize<BackupSettings>(File.ReadAllText(SettingsPath)) ?? new BackupSettings() : new BackupSettings();
        }
        catch (JsonException)
        {
            logger.LogWarning("Backup settings file is unreadable; using defaults");
            return new BackupSettings();
        }
    }

    private void SaveSettings(BackupSettings settings)
    {
        var temp = SettingsPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, SettingsPath, overwrite: true);
    }

    private void Record(BackupRecord record)
    {
        var settings = LoadSettings();
        SaveSettings(settings with { History = settings.Records.Append(record).TakeLast(50).ToList() });
    }

    private string TempDatabase() => Path.Combine(DataFolder, $"restore-{Guid.NewGuid():N}.tmp.db");

    private static void DeleteDatabase(string path)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            try
            {
                File.Delete(path + suffix);
            }
            catch (IOException)
            {
                // Best effort for temporary files.
            }
        }
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var stem = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path));
        for (var i = 2; ; i++)
            if (!File.Exists($"{stem}-{i}{Extension}")) return $"{stem}-{i}{Extension}";
    }
}
