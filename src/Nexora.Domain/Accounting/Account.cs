using System.Text.RegularExpressions;
using Nexora.Domain.Common;

namespace Nexora.Domain.Accounting;

/// <summary>The five accounting natures. Assets and expenses normally carry debit balances; the rest credit.</summary>
public enum AccountNature
{
    Asset,
    Liability,
    Equity,
    Income,
    Expense,
}

/// <summary>Behaviour of a ledger account, used by voucher rules (for example, payments must credit cash or bank).</summary>
public enum AccountKind
{
    Group,
    General,
    Cash,
    Bank,
    Receivable,
    Payable,
    Tax,
    Inventory,
}

public static class AccountNatureExtensions
{
    public static bool IsDebitNormal(this AccountNature nature) => nature is AccountNature.Asset or AccountNature.Expense;

    /// <summary>Balance-sheet natures carry forward between financial years; income and expense restart at zero.</summary>
    public static bool IsBalanceSheet(this AccountNature nature) => nature is AccountNature.Asset or AccountNature.Liability or AccountNature.Equity;
}

/// <summary>
/// A node in the chart of accounts: either a group (for structure and reports) or a ledger (which receives postings).
/// Accounts with a <see cref="SystemKey"/> are referenced by posting rules (see docs/ACCOUNTING_MAP.md) and cannot be removed.
/// </summary>
public sealed partial class Account : ICompanyOwned, IAudited
{
    private Account()
    {
        Code = Name = string.Empty;
    }

    public Account(Guid id, Guid companyId, string code, string name, AccountNature nature, AccountKind kind, Guid? parentId, string? systemKey)
    {
        if (id == Guid.Empty || companyId == Guid.Empty) throw new ArgumentException("Ids are required.");
        Id = id;
        CompanyId = companyId;
        Nature = nature;
        Kind = kind;
        ParentId = parentId;
        SystemKey = systemKey;
        IsActive = true;
        Code = string.Empty;
        Name = string.Empty;
        Rename(code, name);
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public AccountNature Nature { get; private set; }

    public AccountKind Kind { get; private set; }

    public bool IsGroup => Kind == AccountKind.Group;

    /// <summary>Parent group. The database enforces that it belongs to the same company.</summary>
    public Guid? ParentId { get; private set; }

    public string? SystemKey { get; private set; }

    public bool IsSystem => SystemKey is not null;

    public bool IsActive { get; private set; }

    public bool IsCashOrBank => Kind is AccountKind.Cash or AccountKind.Bank;

    public void Rename(string code, string name)
    {
        var cleanCode = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (!CodePattern().IsMatch(cleanCode)) throw new DomainException("Account code must be 1 to 12 letters, digits, dots or hyphens.");
        var cleanName = (name ?? string.Empty).Trim();
        if (cleanName.Length == 0) throw new DomainException("Enter the account name.");
        if (cleanName.Length > 100) throw new DomainException("Account name must be 100 characters or fewer.");
        Code = cleanCode;
        Name = cleanName;
    }

    /// <summary>Moves the account under another group. The caller checks the parent is a group of the same nature.</summary>
    public void MoveTo(Account parent)
    {
        if (!parent.IsGroup) throw new DomainException("Accounts can only be placed under a group.");
        if (parent.Nature != Nature) throw new DomainException($"A {Nature.ToString().ToLowerInvariant()} account cannot be placed under a {parent.Nature.ToString().ToLowerInvariant()} group.");
        if (parent.Id == Id) throw new DomainException("An account cannot be its own parent.");
        ParentId = parent.Id;
    }

    /// <summary>Changes the ledger behaviour. Not allowed for groups or system accounts; the caller checks for postings.</summary>
    public void ChangeKind(AccountKind kind)
    {
        if (IsGroup || kind == AccountKind.Group) throw new DomainException("Groups and ledgers cannot be converted into each other.");
        if (IsSystem && kind != Kind) throw new DomainException("The type of a system account cannot be changed.");
        Kind = kind;
    }

    public void SetActive(bool active)
    {
        if (!active && IsSystem) throw new DomainException("System accounts are used by posting rules and cannot be deactivated.");
        IsActive = active;
    }

    [GeneratedRegex("^[A-Z0-9.-]{1,12}$")]
    private static partial Regex CodePattern();
}

/// <summary>A company's financial year (1 April to 31 March). Postings are allowed only in open years.</summary>
public sealed class FinancialYearPeriod : ICompanyOwned, IAudited
{
    private FinancialYearPeriod()
    {
    }

    public FinancialYearPeriod(Guid id, Guid companyId, int startYear)
    {
        Id = id;
        CompanyId = companyId;
        StartYear = new Companies.FinancialYear(startYear).StartYear;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public int StartYear { get; private set; }

    public bool IsClosed { get; private set; }

    public DateTime? ClosedUtc { get; private set; }

    public Companies.FinancialYear Year => new(StartYear);

    public void Close(DateTime nowUtc)
    {
        if (IsClosed) throw new DomainException($"FY {Year.Label} is already closed.");
        IsClosed = true;
        ClosedUtc = nowUtc;
    }

    public void Reopen()
    {
        if (!IsClosed) throw new DomainException($"FY {Year.Label} is already open.");
        IsClosed = false;
        ClosedUtc = null;
    }
}
