using System.Text.Json;
using Nexora.Application.Abstractions;
using Nexora.Application.Security;
using Nexora.Domain.Audit;
using Nexora.Domain.Security;

namespace Nexora.Application.Audit;

public sealed record AuditRow(long Id, DateTime OccurredUtc, string UserName, string Action, string EntityType, string EntityId,
    string Summary, IReadOnlyList<AuditChange> Changes);

public sealed record AuditQuery(DateTime? FromUtc = null, DateTime? ToUtc = null, string? EntityType = null, int Skip = 0, int Take = 200);

/// <summary>Result of re-computing the hash chain. <see cref="FirstBrokenId"/> is the first entry that no longer matches.</summary>
public sealed record AuditVerification(bool IsIntact, int EntriesChecked, long? FirstBrokenId);

public sealed class AuditService(IUnitOfWorkFactory units, IAuthorizer authorizer)
{
    public async Task<IReadOnlyList<AuditRow>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.AuditView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var from = query.FromUtc ?? DateTime.MinValue;
        var to = query.ToUtc ?? DateTime.MaxValue;
        var type = query.EntityType;
        var entries = await unit.ListAsync<AuditEntry, long>(
            e => e.OccurredUtc >= from && e.OccurredUtc <= to && (type == null || e.EntityType == type),
            e => e.Id, descending: true, Math.Max(0, query.Skip), Math.Clamp(query.Take, 1, 1000), cancellationToken);
        return entries.Select(e => new AuditRow(e.Id, e.OccurredUtc, e.UserName, e.Action, e.EntityType, e.EntityId, e.Summary,
            JsonSerializer.Deserialize<List<AuditChange>>(e.ChangesJson) ?? [])).ToList();
    }

    public async Task<AuditVerification> VerifyAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.AuditView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var entries = await unit.ListAsync<AuditEntry, long>(null, e => e.Sequence, descending: false, 0, int.MaxValue, cancellationToken);
        string? previous = null;
        long expected = 1;
        foreach (var entry in entries)
        {
            if (!entry.IsIntact(expected, previous)) return new AuditVerification(false, entries.Count, entry.Id);
            previous = entry.Hash;
            expected++;
        }

        return new AuditVerification(true, entries.Count, null);
    }
}
