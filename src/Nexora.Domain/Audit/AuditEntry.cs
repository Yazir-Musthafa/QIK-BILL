using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nexora.Domain.Common;

namespace Nexora.Domain.Audit;

/// <summary>One changed property: old and new values as display text.</summary>
public sealed record AuditChange(string Property, string? OldValue, string? NewValue);

/// <summary>
/// An append-only audit record. Entries for a company form a hash chain: each entry's hash covers its
/// content and the previous entry's hash, so editing or deleting a row is detectable.
/// The chain shows tampering; it does not prevent someone with file access from rewriting the whole chain.
/// </summary>
public sealed class AuditEntry : ICompanyOwned
{
    public const string Created = "Created";
    public const string Updated = "Updated";
    public const string Deleted = "Deleted";

    private AuditEntry()
    {
        UserName = Action = EntityType = EntityId = Summary = ChangesJson = Hash = string.Empty;
    }

    public AuditEntry(Guid companyId, DateTime occurredUtc, Guid? userId, string userName, string action,
        string entityType, string entityId, string summary, string changesJson)
    {
        CompanyId = companyId;
        OccurredUtc = DateTime.SpecifyKind(occurredUtc, DateTimeKind.Utc);
        UserId = userId;
        UserName = userName;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        Summary = summary;
        ChangesJson = changesJson;
        Hash = string.Empty;
    }

    public long Id { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>Position in the company's chain, starting at 1. Gaps reveal deleted entries.</summary>
    public long Sequence { get; private set; }

    public DateTime OccurredUtc { get; private set; }

    public Guid? UserId { get; private set; }

    /// <summary>Snapshot of the user's name at the time, so the record survives renames.</summary>
    public string UserName { get; private set; }

    public string Action { get; private set; }

    public string EntityType { get; private set; }

    public string EntityId { get; private set; }

    public string Summary { get; private set; }

    /// <summary>JSON array of <see cref="AuditChange"/>.</summary>
    public string ChangesJson { get; private set; }

    public string? PreviousHash { get; private set; }

    public string Hash { get; private set; }

    /// <summary>Links the entry to the chain. Called once, just before the entry is stored.</summary>
    public void Seal(long sequence, string? previousHash)
    {
        if (Hash.Length > 0) throw new InvalidOperationException("Audit entry is already sealed.");
        if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
        Sequence = sequence;
        PreviousHash = previousHash;
        Hash = ComputeHash();
    }

    public bool IsIntact(long expectedSequence, string? expectedPreviousHash) =>
        Sequence == expectedSequence && PreviousHash == expectedPreviousHash && Hash == ComputeHash();

    private string ComputeHash()
    {
        var canonical = string.Join('\u001F',
            PreviousHash ?? string.Empty,
            Sequence.ToString(CultureInfo.InvariantCulture),
            CompanyId.ToString("N"),
            OccurredUtc.Ticks.ToString(CultureInfo.InvariantCulture),
            UserId?.ToString("N") ?? string.Empty,
            UserName, Action, EntityType, EntityId, Summary, ChangesJson);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

/// <summary>An explicit business event to audit, for example a sign-in or a voucher posting.</summary>
public sealed record AuditEvent(string Action, string EntityType, string EntityId, string Summary, Guid? CompanyId = null);
