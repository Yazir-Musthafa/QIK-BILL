using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nexora.Application;
using Nexora.Application.Licensing;
using Nexora.Application.Security;

namespace Nexora.Infrastructure.Licensing;

public sealed class LocalActivationStore(string databasePath, IPasswordHasher hasher, TimeProvider? time = null,
    string? adminPasswordHash = null) : ILocalActivationStore, IActivationAccess
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly string _hash = adminPasswordHash ?? throw new ArgumentException("Supply the admin password verifier from private build configuration.", nameof(adminPasswordHash));
    private static readonly SemaphoreSlim Gate = new(1, 1);
    // Backup restore clears pooled business connections; activation checks must remain independent.
    private LocalActivationDb Open() => new(new DbContextOptionsBuilder<LocalActivationDb>()
        .UseSqlite(new SqliteConnectionStringBuilder { DataSource = databasePath, ForeignKeys = true, DefaultTimeout = 5, Pooling = false }.ToString()).Options);
    public bool IsActive
    {
        get
        {
            try { using var db = Open(); return db.Installations.AsNoTracking().SingleOrDefault(x => x.Id == 1)?.Status == "Active"; }
            catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException) { return false; }
        }
    }
    public void Demand()
    {
        if (!IsActive) throw new AccessDeniedException("This installation needs activation. Contact the admin to activate or reactivate your account.");
    }
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
            await using var db = Open();
            await db.Database.MigrateAsync(cancellationToken);
            if (!await db.Installations.AnyAsync(cancellationToken))
            {
                db.Installations.Add(new());
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        finally { Gate.Release(); }
    }
    public async Task<InstallationStatus> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var db = Open();
        return new((await db.Installations.AsNoTracking().SingleAsync(cancellationToken)).Status);
    }
    public async Task<bool> VerifyAdministratorAsync(string password, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = Open();
            var row = await db.Installations.SingleAsync(cancellationToken);
            var now = _time.GetUtcNow().UtcDateTime;
            if (row.AdminLockedUntilUtc > now) return false;
            var valid = password.Length <= 128 && hasher.Verify(password, _hash) != PasswordVerification.Failed;
            if (valid) { row.FailedAdminAttempts = 0; row.AdminLockedUntilUtc = null; }
            else if (++row.FailedAdminAttempts >= 5) { row.FailedAdminAttempts = 0; row.AdminLockedUntilUtc = now.AddMinutes(15); }
            await db.SaveChangesAsync(cancellationToken);
            return valid;
        }
        finally { Gate.Release(); }
    }
    public async Task ChangeAsync(string status, string note, string actor, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = Open();
            var row = await db.Installations.SingleAsync(cancellationToken);
            var previous = row.Status;
            row.Status = status;
            db.History.Add(new LocalActivationAudit { AtUtc = _time.GetUtcNow().UtcDateTime, Actor = actor,
                Action = status == "Active" ? "Activate" : "Suspend", Note = $"{previous} → {status}. {note}" });
            await db.SaveChangesAsync(cancellationToken);
        }
        finally { Gate.Release(); }
    }
    public async Task<IReadOnlyList<ActivationHistory>> HistoryAsync(CancellationToken cancellationToken = default)
    {
        await using var db = Open();
        return (await db.History.AsNoTracking().OrderByDescending(x => x.Id).Take(100).ToListAsync(cancellationToken))
            .Select(x => new ActivationHistory(new DateTimeOffset(DateTime.SpecifyKind(x.AtUtc, DateTimeKind.Utc)), x.Actor, x.Action, x.Note)).ToList();
    }
}
