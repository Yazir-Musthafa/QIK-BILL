using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nexora.Application;
using Nexora.Application.Abstractions;
using Nexora.Application.Security;
using Nexora.Infrastructure.Backup;
using Nexora.Infrastructure.Persistence;
using Nexora.Infrastructure.Persistence.Providers;
using Nexora.Tests.Billing;

namespace Nexora.Tests.Phase9;

/// <summary>Stands in for Windows DPAPI on macOS: reversible, but never stores the password as written.</summary>
internal sealed class FakeProtector : ISecretProtector
{
    public string Protect(string secret) => "protected:" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(new string(secret.Reverse().ToArray())));

    public string Unprotect(string protectedSecret) =>
        new(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(protectedSecret["protected:".Length..])).Reverse().ToArray());
}

/// <summary>A protector that can no longer open the secret, as when the Windows account changes.</summary>
internal sealed class BrokenProtector : ISecretProtector
{
    public string Protect(string secret) => "protected:x";

    public string Unprotect(string protectedSecret) => throw new WorkspaceValidationException("The saved backup password cannot be opened by this Windows account.");
}

public sealed class EncryptedAutomaticBackupTests : BillingTestBase
{
    private const string Password = "automatic backup phrase";

    private string Folder => Path.Combine(Path.GetDirectoryName(Db.DatabasePath)!, "auto");

    private BackupService With(ISecretProtector protector) => new(Db.Get<SqliteDatabaseProvider>(), Db.Get<IDbContextFactory<NexoraDbContext>>(), Db.Get<IWorkspaceStore>(),
        Db.Workspace, Db.Get<IAuthorizer>(), Db.Clock, NullLogger<BackupService>.Instance, protector);

    [Fact]
    public async Task Automatic_backups_are_encrypted_with_the_protected_password()
    {
        var backups = With(new FakeProtector());
        await backups.SaveSettingsAsync(true, 1, Folder, 3, encryptAutomatic: true, automaticPassword: Password);

        var settingsFile = await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(Db.DatabasePath)!, "backup-settings.json"));
        Assert.DoesNotContain(Password, settingsFile, StringComparison.Ordinal);       // only the protected form is stored
        Assert.Null(backups.Settings().ProtectedPassword);                              // and it is never handed to the screen

        var run = await backups.RunScheduledIfDueAsync();
        Assert.True(run!.Succeeded);
        var path = Path.Combine(Folder, run.FileName);
        Assert.True(BackupFormat.ReadHeader(path).Encrypted);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => backups.VerifyAsync(path));
        Assert.Equal(2, (await backups.VerifyAsync(path, Password)).Header.Companies.Count);
    }

    [Fact]
    public async Task A_password_that_cannot_be_opened_never_falls_back_to_an_unencrypted_backup()
    {
        await With(new FakeProtector()).SaveSettingsAsync(true, 1, Folder, 3, encryptAutomatic: true, automaticPassword: Password);
        var broken = With(new BrokenProtector());

        var run = await broken.RunScheduledIfDueAsync();

        Assert.False(run!.Succeeded);
        Assert.False(Directory.Exists(Folder) && Directory.GetFiles(Folder, "*.qikbak").Length > 0);
        Assert.True(broken.Status().IsOverdue);
    }

    [Fact]
    public async Task Encryption_needs_a_long_enough_password_and_saving_checks_it_can_be_opened()
    {
        var backups = With(new FakeProtector());
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => backups.SaveSettingsAsync(true, 24, Folder, 3, encryptAutomatic: true, automaticPassword: "short"));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => backups.SaveSettingsAsync(true, 24, Folder, 3, encryptAutomatic: true));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => With(new BrokenProtector()).SaveSettingsAsync(true, 24, Folder, 3, encryptAutomatic: true, automaticPassword: Password));
    }

    [Fact]
    public async Task A_restore_protects_its_safety_copy_with_the_automatic_password()
    {
        var backups = With(new FakeProtector());
        await backups.SaveSettingsAsync(false, 24, Folder, 3, encryptAutomatic: true, automaticPassword: Password);
        var plain = await backups.CreateAsync(Folder);

        var result = await backups.RestoreAsync(plain);

        Assert.True(BackupFormat.ReadHeader(result.SafetyBackupPath).Encrypted);
    }
}
