using Nexora.Domain.Common;

namespace Nexora.Domain.Accounting;

public enum CostCentreKind
{
    /// <summary>Collects costs only, for example a department.</summary>
    Cost,

    /// <summary>Has its own income and costs, for example a branch or product line.</summary>
    Profit,
}

/// <summary>A cost or profit centre that income and expense voucher lines can be tagged with.</summary>
public sealed class CostCentre : ICompanyOwned, IAudited
{
    private CostCentre()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public CostCentre(Guid id, Guid companyId, string code, string name, CostCentreKind kind) : this()
    {
        Id = id;
        CompanyId = companyId;
        Update(code, name, kind);
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public CostCentreKind Kind { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(string code, string name, CostCentreKind kind)
    {
        var c = (code ?? string.Empty).Trim().ToUpperInvariant();
        var n = (name ?? string.Empty).Trim();
        if (c.Length is 0 or > 20) throw new DomainException("Enter a code of up to 20 characters.");
        if (n.Length is 0 or > 100) throw new DomainException("Enter a name of up to 100 characters.");
        (Code, Name, Kind) = (c, n, kind);
    }

    public void SetActive(bool active) => IsActive = active;
}

/// <summary>
/// Marks one bank-account ledger entry as cleared on the bank statement, for bank reconciliation. Removing the row
/// un-clears the entry; both are audited. The ledger entry itself never changes.
/// </summary>
public sealed class BankClearing : ICompanyOwned, IAudited
{
    private BankClearing()
    {
        StatementReference = string.Empty;
    }

    public BankClearing(Guid id, Guid companyId, Guid ledgerEntryId, Guid accountId, DateOnly clearedOn, string statementReference, DateTime clearedUtc, Guid clearedBy)
        : this()
    {
        var reference = (statementReference ?? string.Empty).Trim();
        if (reference.Length > 60) throw new DomainException("The statement reference can have at most 60 characters.");
        Id = id;
        CompanyId = companyId;
        LedgerEntryId = ledgerEntryId;
        AccountId = accountId;
        ClearedOn = clearedOn;
        StatementReference = reference;
        ClearedUtc = clearedUtc;
        ClearedBy = clearedBy;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid LedgerEntryId { get; private set; }

    /// <summary>The bank account of the entry (copied so a statement's clearings are found without a join).</summary>
    public Guid AccountId { get; private set; }

    /// <summary>The date the bank statement shows the amount.</summary>
    public DateOnly ClearedOn { get; private set; }

    public string StatementReference { get; private set; }

    public DateTime ClearedUtc { get; private set; }

    public Guid ClearedBy { get; private set; }
}
