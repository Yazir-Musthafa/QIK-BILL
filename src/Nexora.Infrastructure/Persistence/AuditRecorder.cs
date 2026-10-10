using System.Collections;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Nexora.Application.Abstractions;
using Nexora.Domain.Audit;
using Nexora.Domain.Common;
using Nexora.Domain.Companies;

namespace Nexora.Infrastructure.Persistence;

/// <summary>
/// Turns tracked changes to <see cref="IAudited"/> entities, plus explicit <see cref="AuditEvent"/>s, into
/// sealed <see cref="AuditEntry"/> rows inside the same transaction as the change.
/// </summary>
internal sealed class AuditRecorder(NexoraDbContext context, ISessionContext session, IClock clock)
{
    // Operational counters that change on every sign-in; recording them would bury real changes.
    private static readonly HashSet<string> IgnoredProperties = ["FailedSignInCount", "LockoutEndUtc", "LastSignInUtc"];

    // Recorded as changed, never with their values.
    private static readonly HashSet<string> MaskedProperties = ["PasswordHash"];

    private readonly List<AuditEvent> _events = [];

    /// <summary>Company to file non-company entities (users) under when no session exists, for example during setup.</summary>
    public Guid? CompanyOverride { get; set; }

    /// <summary>Actor to record when no session exists, for example the administrator created during setup.</summary>
    public (Guid? Id, string Name)? ActorOverride { get; set; }

    public void Queue(AuditEvent auditEvent) => _events.Add(auditEvent);

    public async Task CaptureAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var (actorId, actorName) = ActorOverride ?? (session.Current?.UserId, session.Current?.Username ?? "system");
        var entries = new List<AuditEntry>();

        foreach (var entry in context.ChangeTracker.Entries().Where(e => e.Entity is IAudited).ToList())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
            var companyId = CompanyOf(entry);
            if (companyId == Guid.Empty) continue;

            var changes = Changes(entry);
            if (entry.State == EntityState.Modified && changes.Count == 0) continue;

            var action = entry.State switch
            {
                EntityState.Added => AuditEntry.Created,
                EntityState.Deleted => AuditEntry.Deleted,
                _ => AuditEntry.Updated,
            };
            var type = entry.Metadata.ClrType.Name;
            entries.Add(new AuditEntry(companyId, now, actorId, actorName, action, type, KeyOf(entry),
                $"{action} {Humanize(type)} {DisplayNameOf(entry)}".TrimEnd(), JsonSerializer.Serialize(changes)));
        }

        foreach (var auditEvent in _events)
        {
            var companyId = auditEvent.CompanyId ?? CompanyOverride ?? session.Current?.CompanyId ?? Guid.Empty;
            if (companyId == Guid.Empty) continue;
            entries.Add(new AuditEntry(companyId, now, actorId, actorName, auditEvent.Action, auditEvent.EntityType,
                auditEvent.EntityId, auditEvent.Summary, "[]"));
        }

        _events.Clear();
        if (entries.Count == 0) return;

        foreach (var group in entries.GroupBy(e => e.CompanyId))
        {
            var last = await context.AuditEntries.IgnoreQueryFilters()
                .Where(e => e.CompanyId == group.Key)
                .OrderByDescending(e => e.Sequence)
                .Select(e => new { e.Sequence, e.Hash })
                .FirstOrDefaultAsync(cancellationToken);
            var sequence = last?.Sequence ?? 0;
            var previous = last?.Hash;
            foreach (var entry in group)
            {
                entry.Seal(++sequence, previous);
                previous = entry.Hash;
            }
        }

        context.AuditEntries.AddRange(entries);
    }

    private Guid CompanyOf(EntityEntry entry) => entry.Entity switch
    {
        ICompanyOwned owned => owned.CompanyId,
        Company company => company.Id,
        _ => CompanyOverride ?? session.Current?.CompanyId ?? Guid.Empty,
    };

    private static List<AuditChange> Changes(EntityEntry entry)
    {
        var changes = new List<AuditChange>();
        foreach (var property in entry.Properties)
        {
            var name = property.Metadata.GetColumnName() ?? property.Metadata.Name;
            if (IgnoredProperties.Contains(name) || property.Metadata.IsShadowProperty()) continue;

            string? oldValue = entry.State == EntityState.Added ? null : Format(property.OriginalValue);
            string? newValue = entry.State == EntityState.Deleted ? null : Format(property.CurrentValue);
            if (entry.State == EntityState.Modified && (!property.IsModified || oldValue == newValue)) continue;

            if (MaskedProperties.Contains(name))
            {
                oldValue = oldValue is null ? null : "(hidden)";
                newValue = newValue is null ? null : "(changed)";
            }

            changes.Add(new AuditChange(name, oldValue, newValue));
        }

        return changes;
    }

    private static string KeyOf(EntityEntry entry) =>
        string.Join(",", entry.Metadata.FindPrimaryKey()!.Properties.Select(p => Format(entry.Property(p.Name).CurrentValue)));

    private static string DisplayNameOf(EntityEntry entry)
    {
        foreach (var candidate in new[] { "Number", "Code", "Username", "Name", "DocumentType" })
        {
            if (entry.Metadata.FindProperty(candidate) is not null && entry.Property(candidate).CurrentValue is string { Length: > 0 } text)
                return $"\"{text}\"";
        }

        return string.Empty;
    }

    private static string Humanize(string typeName) =>
        string.Concat(typeName.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

    private static string? Format(object? value) => value switch
    {
        null => null,
        string s => s,
        DateTime d => d.ToString("O", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        byte[] bytes => $"({bytes.Length} bytes)",
        IEnumerable<string> set => string.Join(", ", set.Order(StringComparer.Ordinal)),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        IEnumerable other => string.Join(", ", other.Cast<object>()),
        _ => value.ToString(),
    };
}
