using Microsoft.EntityFrameworkCore;
using Nexora.Application;
using Nexora.Application.Abstractions;
using Nexora.Application.Audit;
using Nexora.Application.Companies;
using Nexora.Domain.Audit;
using Nexora.Domain.Companies;
using Nexora.Infrastructure.Persistence;
using Nexora.Tests.Infrastructure;
using Nexora.Tests.Workspace;

namespace Nexora.Tests.Companies;

public sealed class AuditTrailTests : IAsyncLifetime
{
    private readonly SqliteTestDatabase _db = new();

    private AuditService Audit => _db.Get<AuditService>();

    private CompanyService Companies => _db.Get<CompanyService>();

    public async Task InitializeAsync()
    {
        await _db.Workspace.InitializeAsync();
        await _db.Workspace.CompleteSetupAsync(WorkspaceServiceTests.ValidRequest());
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Setup_is_recorded_with_the_administrator_as_actor()
    {
        var entries = await Audit.QueryAsync(new AuditQuery());

        Assert.Contains(entries, e => e.Action == AuditEntry.Created && e.EntityType == nameof(Company));
        Assert.Contains(entries, e => e.EntityType == "Role" && e.Summary.Contains("Administrator", StringComparison.Ordinal));
        Assert.All(entries, e => Assert.Equal("asha", e.UserName));
    }

    [Fact]
    public async Task Changes_record_who_what_when_with_old_and_new_values()
    {
        _db.Clock.UtcNow = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
        await Companies.UpdateProfileAsync(new CompanyProfile("Acme Trading Co", "", "27", "", "", "Pune", "411001", "", "", "", ""));

        var entry = (await Audit.QueryAsync(new AuditQuery(EntityType: nameof(Company)))).First();

        Assert.Equal(AuditEntry.Updated, entry.Action);
        Assert.Equal("asha", entry.UserName);
        Assert.Equal(_db.Clock.UtcNow, entry.OccurredUtc);
        Assert.Contains(entry.Changes, c => c is { Property: "Name", OldValue: "Acme Traders", NewValue: "Acme Trading Co" });
        Assert.Contains(entry.Changes, c => c is { Property: "Pincode", OldValue: "", NewValue: "411001" });
        Assert.DoesNotContain(entry.Changes, c => c.Property == "StateCode");
    }

    [Fact]
    public async Task Password_changes_are_recorded_without_the_hash()
    {
        await _db.Workspace.ChangePasswordAsync("quiet ledger 42", "brand new phrase 7");

        var entries = await Audit.QueryAsync(new AuditQuery(EntityType: "User"));
        var update = entries.First(e => e.Action == AuditEntry.Updated);
        var hash = update.Changes.Single(c => c.Property == "PasswordHash");

        Assert.Equal("(hidden)", hash.OldValue);
        Assert.Equal("(changed)", hash.NewValue);
        Assert.Contains(entries, e => e.Action == "PasswordChanged");
        Assert.DoesNotContain(entries.SelectMany(e => e.Changes), c => c.NewValue?.Contains("PBKDF2", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Sign_in_is_recorded()
    {
        _db.Workspace.SignOut();
        await _db.Workspace.SignInAsync("asha", "quiet ledger 42");

        Assert.Contains(await Audit.QueryAsync(new AuditQuery()), e => e.Action == "SignedIn");
    }

    [Fact]
    public async Task Untouched_chain_verifies()
    {
        await Companies.SaveBranchAsync(new BranchInput(null, "HO", "Head office", "27", ""));
        var result = await Audit.VerifyAsync();

        Assert.True(result.IsIntact);
        Assert.True(result.EntriesChecked > 10);
    }

    [Fact]
    public async Task Editing_a_stored_entry_is_detected()
    {
        await Companies.SaveBranchAsync(new BranchInput(null, "HO", "Head office", "27", ""));
        var target = (await Audit.QueryAsync(new AuditQuery())).Last();

        using (var connection = _db.OpenRawConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE AuditEntries SET Summary = 'nothing happened' WHERE Id = $id";
            command.Parameters.AddWithValue("$id", target.Id);
            Assert.Equal(1, command.ExecuteNonQuery());
        }

        var result = await Audit.VerifyAsync();
        Assert.False(result.IsIntact);
        Assert.Equal(target.Id, result.FirstBrokenId);
    }

    [Fact]
    public async Task Deleting_a_stored_entry_is_detected()
    {
        var entries = await Audit.QueryAsync(new AuditQuery());
        var middle = entries[entries.Count / 2];

        using (var connection = _db.OpenRawConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM AuditEntries WHERE Id = $id";
            command.Parameters.AddWithValue("$id", middle.Id);
            command.ExecuteNonQuery();
        }

        Assert.False((await Audit.VerifyAsync()).IsIntact);
    }

    [Fact]
    public async Task Audit_entries_cannot_be_changed_through_the_application()
    {
        var factory = _db.Get<IDbContextFactory<NexoraDbContext>>();
        await using var context = await factory.CreateDbContextAsync();
        var entry = await context.AuditEntries.FirstAsync();
        context.Remove(entry);

        await Assert.ThrowsAsync<ImmutableRecordException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Failed_saves_leave_no_audit_entries()
    {
        var before = (await Audit.VerifyAsync()).EntriesChecked;
        await Assert.ThrowsAnyAsync<Exception>(() => Companies.SaveBranchAsync(new BranchInput(null, "bad code!", "X", "27", "")));

        Assert.Equal(before, (await Audit.VerifyAsync()).EntriesChecked);
    }
}

public sealed class UpgradeTests
{
    /// <summary>A database created by Phase 1 has companies and users but no roles; access must survive the upgrade.</summary>
    [Fact]
    public async Task Phase_one_companies_get_roles_and_their_users_stay_administrators()
    {
        await using var db = new SqliteTestDatabase();
        await db.Workspace.InitializeAsync();
        var hasher = new Nexora.Application.Security.Pbkdf2PasswordHasher(1_000);

        using (var connection = db.OpenRawConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "INSERT INTO Companies (Id, Name, StateCode, FinancialYearStartYear, IsDemo, CreatedUtc, LegalName, AddressLine1, AddressLine2, City, Pincode, Phone, Email, Pan, Gstin) " +
                "VALUES ('11111111-1111-1111-1111-111111111111', 'Legacy Co', '27', 2025, 0, '2026-01-01 00:00:00', '', '', '', '', '', '', '', '', '');" +
                "INSERT INTO Users (Id, Username, NormalizedUsername, DisplayName, PasswordHash, IsActive, FailedSignInCount, CreatedUtc, MustChangePassword, PasswordChangedUtc) " +
                "VALUES ('22222222-2222-2222-2222-222222222222', 'old', 'OLD', 'Old Admin', $hash, 1, 0, '2026-01-01 00:00:00', 0, '2026-01-01 00:00:00');";
            command.Parameters.AddWithValue("$hash", hasher.Hash("legacy password 1"));
            command.ExecuteNonQuery();
        }

        await db.Workspace.InitializeAsync();
        var result = await db.Workspace.SignInAsync("old", "legacy password 1");

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("Administrator", result.Session!.RoleName);
        Assert.Equal(6, (await db.Get<Nexora.Application.Users.RoleService>().ListAsync()).Count);
        Assert.NotEmpty(await db.Get<CompanyService>().ListNumberingAsync());
        Assert.True((await db.Get<AuditService>().VerifyAsync()).IsIntact);

        // Running the upgrade again changes nothing.
        await db.Workspace.InitializeAsync();
        Assert.Equal(6, (await db.Get<Nexora.Application.Users.RoleService>().ListAsync()).Count);
    }
}
