using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nexora.Application;
using Nexora.Application.Abstractions;
using Nexora.Application.Audit;
using Nexora.Application.Billing;
using Nexora.Application.Security;
using Nexora.Infrastructure.Backup;
using Nexora.Infrastructure.Persistence;
using Nexora.Infrastructure.Persistence.Providers;

namespace Nexora.Tests.Phase8;

public sealed class BackupTests : ReportTestBase
{
    private BackupService Backups => Db.Get<BackupService>();
    private string Folder => Path.Combine(Path.GetDirectoryName(Db.DatabasePath)!, "backups");

    [Fact]
    public async Task Backups_remain_readable_during_connection_pool_cleanup()
    {
        using var stop = new CancellationTokenSource();
        var cleanup = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                await Task.Delay(1);
            }
        });
        try
        {
            for (var i = 0; i < 12; i++)
            {
                var path = await Backups.CreateAsync(Folder);
                Assert.Equal(2, (await Backups.VerifyAsync(path)).Header.Companies.Count);
            }
        }
        finally
        {
            stop.Cancel();
            await cleanup;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_recovers_data_audit_chain_and_reports_and_signs_out(bool encrypted)
    {
        var password = encrypted ? "a long backup password" : null;
        var before = await ReportSnapshotAsync();
        var audit = AuditHashes();
        var path = await Backups.CreateAsync(Folder, password);
        var verified = await Backups.VerifyAsync(path, password);
        Assert.Equal(encrypted, verified.Header.Encrypted);
        Assert.Equal(2, verified.Header.Companies.Count);
        await SellAsync(Local, new BillingLineInput(Led, 1));
        Assert.NotEqual(before, await ReportSnapshotAsync());

        var result = await Backups.RestoreAsync(path, password);

        Assert.Null(Db.Workspace.CurrentSession);
        Assert.True(File.Exists(result.SafetyBackupPath));
        Assert.Equal(audit, AuditHashes());
        await SignInAsAdministratorAsync();
        Assert.Equal(before, await ReportSnapshotAsync());
        Assert.True((await Db.Get<AuditService>().VerifyAsync()).IsIntact);
        Assert.Contains(Backups.Settings().Records, r => r.Kind == BackupKind.Safety && r.Succeeded);
    }

    [Theory]
    [InlineData("corrupt")]
    [InlineData("header")]
    [InlineData("truncated")]
    [InlineData("not-backup")]
    [InlineData("wrong-password")]
    [InlineData("authenticated-header")]
    public async Task Invalid_backups_are_refused_before_changing_live_data(string damage)
    {
        const string password = "a long backup password";
        var encrypted = damage is "wrong-password" or "authenticated-header";
        var path = await Backups.CreateAsync(Folder, encrypted ? password : null);
        var bytes = await File.ReadAllBytesAsync(path);
        switch (damage)
        {
            case "corrupt": bytes[^33] ^= 1; break; // gzip padding can remain readable: file checksum must catch this
            case "header": bytes[15] ^= 1; break;
            case "truncated": bytes = bytes[..^40]; break;
            case "not-backup": bytes = "this is not a backup file"u8.ToArray(); break;
            case "authenticated-header":
                var length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8, 4));
                var header = JsonSerializer.Deserialize<BackupHeader>(bytes.AsSpan(12, length))!;
                var replacement = JsonSerializer.SerializeToUtf8Bytes(header with { AppVersion = new string('9', header.AppVersion.Length) });
                Assert.Equal(length, replacement.Length);
                replacement.CopyTo(bytes, 12);
                SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)).CopyTo(bytes, bytes.Length - 32);
                break;
        }
        await File.WriteAllBytesAsync(path, bytes);
        await SellAsync(Local, new BillingLineInput(Led, 1));
        var before = await ReportSnapshotAsync();
        var audit = AuditHashes();

        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Backups.RestoreAsync(path,
            damage == "wrong-password" ? "a different password" : encrypted ? password : null));

        Assert.Equal(before, await ReportSnapshotAsync());
        Assert.Equal(audit, AuditHashes());
        Assert.NotNull(Db.Workspace.CurrentSession);
        Assert.DoesNotContain(Backups.Settings().Records, r => r.Kind == BackupKind.Safety);
    }

    [Fact]
    public async Task Newer_schema_is_refused_and_current_data_is_unchanged()
    {
        using (var connection = Db.OpenRawConnection())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO __EFMigrationsHistory VALUES ('20990101000000_FutureSchema', '10.0.0')";
            command.ExecuteNonQuery();
        }
        var path = await Backups.CreateAsync(Folder);
        using (var connection = Db.OpenRawConnection())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM __EFMigrationsHistory WHERE MigrationId = '20990101000000_FutureSchema'";
            command.ExecuteNonQuery();
        }
        await SellAsync(Local, new BillingLineInput(Led, 1));
        var before = await ReportSnapshotAsync();
        var error = await Assert.ThrowsAsync<WorkspaceValidationException>(() => Backups.RestoreAsync(path));
        Assert.Contains("newer version", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, await ReportSnapshotAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_after_replacement_rolls_back_even_when_cancelled(bool cancel)
    {
        var path = await Backups.CreateAsync(Folder);
        await SellAsync(Local, new BillingLineInput(Led, 1));
        var before = await ReportSnapshotAsync();
        var audit = AuditHashes();
        using var cancellation = new CancellationTokenSource();
        var store = DispatchProxy.Create<IWorkspaceStore, FailingMigrationStore>();
        var proxy = (FailingMigrationStore)store;
        proxy.Inner = Db.Get<IWorkspaceStore>();
        proxy.Fail = () =>
        {
            if (cancel) cancellation.Cancel();
            throw new InvalidOperationException("Forced migration failure after database replacement");
        };
        var failing = new BackupService(Db.Get<SqliteDatabaseProvider>(), Db.Get<IDbContextFactory<NexoraDbContext>>(), store,
            Db.Workspace, Db.Get<IAuthorizer>(), Db.Clock, NullLogger<BackupService>.Instance);

        var error = await Assert.ThrowsAsync<WorkspaceValidationException>(() => failing.RestoreAsync(path, cancellationToken: cancellation.Token));

        Assert.Contains("put back unchanged", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, await ReportSnapshotAsync());
        Assert.Equal(audit, AuditHashes());
        Assert.True((await Db.Get<AuditService>().VerifyAsync()).IsIntact);
    }

    [Theory]
    [InlineData("Read-only")]
    [InlineData("Administrator")]
    public async Task Backup_requires_permission_in_every_company(string role)
    {
        var path = await Backups.CreateAsync(Folder);
        await SignInAsAsync("limited", role); // this membership exists in the real company only
        await Assert.ThrowsAsync<AccessDeniedException>(() => Backups.CreateAsync(Folder));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Backups.RestoreAsync(path));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Backups.SaveSettingsAsync(true, 24, Folder, 3));
        if (role == "Read-only") await Assert.ThrowsAsync<AccessDeniedException>(() => Backups.VerifyAsync(path));
    }

    [Fact]
    public async Task Schedule_checks_due_time_keeps_newest_automatic_files_and_leaves_manual_backups()
    {
        await Backups.SaveSettingsAsync(true, 1, Folder, 2);
        Assert.True(Backups.Status().IsOverdue);
        var manual = await Backups.CreateAsync(Folder);
        Assert.Null(await Backups.RunScheduledIfDueAsync());
        string? oldest = null;
        for (var i = 0; i < 3; i++)
        {
            Db.Clock.UtcNow = Db.Clock.UtcNow.AddHours(1);
            var run = await Backups.RunScheduledIfDueAsync();
            Assert.True(run!.Succeeded);
            oldest ??= Path.Combine(Folder, run.FileName);
            Assert.Null(await Backups.RunScheduledIfDueAsync());
            Assert.False(Backups.Status().IsOverdue);
        }
        Assert.False(File.Exists(oldest));
        Assert.True(File.Exists(manual));
        Assert.Equal(2, Directory.GetFiles(Folder, "QIKSuite-auto-*.qikbak").Length);
        Db.Clock.UtcNow = Db.Clock.UtcNow.AddHours(2);
        Assert.True(Backups.Status().IsOverdue);
        await Backups.SaveSettingsAsync(false, 1, Folder, 2);
        Assert.Null(await Backups.RunScheduledIfDueAsync());
    }

    [Fact]
    public async Task Failed_automatic_backup_is_visible_and_a_retry_can_recover()
    {
        var file = Path.Combine(Path.GetDirectoryName(Db.DatabasePath)!, "not-a-directory");
        await File.WriteAllTextAsync(file, "occupied");
        await Backups.SaveSettingsAsync(true, 1, file, 2);
        var failed = await Backups.RunScheduledIfDueAsync();
        Assert.False(failed!.Succeeded);
        Assert.True(Backups.Status().IsOverdue);
        Assert.NotNull(Backups.Status().LastFailure);
        await Backups.SaveSettingsAsync(true, 1, Folder, 2);
        Assert.True((await Backups.RunScheduledIfDueAsync())!.Succeeded);
        Assert.Null(Backups.Status().LastFailure);
        Db.Workspace.SignOut();
        Db.Clock.UtcNow = Db.Clock.UtcNow.AddHours(1);
        Assert.True((await Backups.RunScheduledIfDueAsync())!.Succeeded);
    }

    private async Task<string> ReportSnapshotAsync()
    {
        var results = new List<object>();
        foreach (var key in new[] { "sales.register", "inventory.valuation", "accounts.trialbalance" })
        {
            var result = await RunAsync(key);
            results.Add(new { result.Rows, result.Totals });
        }
        return JsonSerializer.Serialize(results);
    }

    private string[] AuditHashes()
    {
        using var connection = Db.OpenRawConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Hash FROM AuditEntries ORDER BY CompanyId, Sequence";
        using var reader = command.ExecuteReader();
        var hashes = new List<string>();
        while (reader.Read()) hashes.Add(reader.GetString(0));
        return hashes.ToArray();
    }
}

public class FailingMigrationStore : DispatchProxy
{
    public IWorkspaceStore Inner { get; set; } = null!;
    public Action? Fail { get; set; }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod!.Name == nameof(IWorkspaceStore.MigrateAsync) && Fail is { } fail)
        {
            Fail = null;
            fail();
        }
        try { return targetMethod.Invoke(Inner, args); }
        catch (TargetInvocationException ex) { throw ex.InnerException!; }
    }
}
