using Nexora.Domain.Accounting;
using Nexora.Domain.Billing;
using Nexora.Domain.Common;

namespace Nexora.Domain.Expenses;

public enum ExpenseStatus
{
    Draft,
    PendingApproval,
    Approved,
    Rejected,
    Posted,
}

public enum RecurrenceFrequency
{
    Weekly,
    Monthly,
    Quarterly,
    Yearly,
}

public static class ExpenseDocumentTypes
{
    public const string Expense = "expense.voucher";

    public static IReadOnlyList<Numbering.DocumentTypeInfo> All { get; } = [new(Expense, "Expense", "EXP/{FY}/", 4)];
}

/// <summary>An expense category, posted to one expense ledger account (for example Rent → the Rent ledger).</summary>
public sealed class ExpenseCategory : ICompanyOwned, IAudited
{
    private ExpenseCategory()
    {
        Name = string.Empty;
    }

    public ExpenseCategory(Guid id, Guid companyId, string name, Guid accountId, Guid? taxCategoryId) : this()
    {
        Id = id;
        CompanyId = companyId;
        IsActive = true;
        Update(name, accountId, taxCategoryId);
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Name { get; private set; }

    /// <summary>The expense ledger the category posts to.</summary>
    public Guid AccountId { get; private set; }

    /// <summary>Suggested GST category for new lines; each line keeps the rate it used.</summary>
    public Guid? DefaultTaxCategoryId { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(string name, Guid accountId, Guid? taxCategoryId)
    {
        var clean = (name ?? string.Empty).Trim();
        if (clean.Length is 0 or > 60) throw new DomainException("Enter a category name of up to 60 characters.");
        Name = clean;
        AccountId = accountId;
        DefaultTaxCategoryId = taxCategoryId;
    }

    public void SetActive(bool active) => IsActive = active;
}

/// <summary>
/// An expense voucher. It moves Draft → PendingApproval → Approved (or Rejected, which can be edited and resubmitted)
/// → Posted. Posting creates an Expense voucher through the accounting engine. Once posted, nothing changes; a
/// reversal (<see cref="ExpenseReversal"/>) corrects it. Amounts are recalculated by the service from the lines.
/// </summary>
public sealed class Expense : ICompanyOwned, IAudited
{
    private Expense()
    {
        Department = Reference = Narration = EmployeeName = RejectionReason = string.Empty;
    }

    public Expense(Guid id, Guid companyId, DateOnly date, DateTime createdUtc, Guid createdBy) : this()
    {
        Id = id;
        CompanyId = companyId;
        Date = date;
        Status = ExpenseStatus.Draft;
        CreatedUtc = DateTime.SpecifyKind(createdUtc, DateTimeKind.Utc);
        CreatedBy = createdBy;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>Null until posted.</summary>
    public string? Number { get; private set; }

    public DateOnly Date { get; private set; }

    public ExpenseStatus Status { get; private set; }

    /// <summary>The supplier the expense was bought from (optional, for reference and tax: the expense is always paid from cash or bank).</summary>
    public Guid? SupplierId { get; private set; }

    /// <summary>The employee who spent or claims the money (a company user), if any.</summary>
    public Guid? EmployeeUserId { get; private set; }

    /// <summary>A free-text employee name when the person has no user account.</summary>
    public string EmployeeName { get; private set; }

    /// <summary>Cash or bank account paid from (required before saving; nullable only for the private constructor).</summary>
    public Guid? PaidFromAccountId { get; private set; }

    public Billing.PaymentMethod Method { get; private set; }

    public Guid? BranchId { get; private set; }

    public string Department { get; private set; }

    /// <summary>The supplier's bill or receipt number.</summary>
    public string Reference { get; private set; }

    public string Narration { get; private set; }

    public bool IsInterState { get; private set; }

    public decimal Taxable { get; private set; }

    public decimal Cgst { get; private set; }

    public decimal Sgst { get; private set; }

    public decimal Igst { get; private set; }

    /// <summary>Tax that cannot be claimed as input credit and is added to the expense.</summary>
    public decimal IneligibleTax { get; private set; }

    public decimal Total { get; private set; }

    /// <summary>Created by a recurring expense for this due date (one draft per template per date).</summary>
    public Guid? RecurringExpenseId { get; private set; }

    public DateOnly? RecurringDueDate { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    public Guid CreatedBy { get; private set; }

    public Guid? SubmittedBy { get; private set; }

    public DateTime? SubmittedUtc { get; private set; }

    public Guid? DecidedBy { get; private set; }

    public DateTime? DecidedUtc { get; private set; }

    public string RejectionReason { get; private set; }

    public Guid? VoucherId { get; private set; }

    public DateTime? PostedUtc { get; private set; }

    public Guid? PostedBy { get; private set; }

    public bool IsEditable => Status is ExpenseStatus.Draft or ExpenseStatus.Rejected;

    public void UpdateDraft(DateOnly date, Guid? supplierId, Guid? employeeUserId, string employeeName, Guid? paidFromAccountId, Billing.PaymentMethod method,
        Guid? branchId, string department, string reference, string narration, bool interState)
    {
        EnsureEditable();
        if (paidFromAccountId is null) throw new DomainException("Choose the cash or bank account the expense was paid from.");
        Date = date;
        SupplierId = supplierId;
        EmployeeUserId = employeeUserId;
        EmployeeName = Text(employeeName, 80, "Employee name");
        PaidFromAccountId = paidFromAccountId;
        Method = method;
        BranchId = branchId;
        Department = Text(department, 60, "Department");
        Reference = Text(reference, 40, "Reference");
        Narration = Text(narration, 300, "Narration");
        IsInterState = interState;
        if (Status == ExpenseStatus.Rejected) Status = ExpenseStatus.Draft;
    }

    public void SetTotals(decimal taxable, decimal cgst, decimal sgst, decimal igst, decimal ineligibleTax)
    {
        EnsureEditable();
        (Taxable, Cgst, Sgst, Igst, IneligibleTax) = (taxable, cgst, sgst, igst, ineligibleTax);
        Total = taxable + cgst + sgst + igst + ineligibleTax;
        if (Total <= 0) throw new DomainException("An expense must be more than zero.");
        Money.EnsureValid(Total, "Total");
    }

    public void LinkRecurring(Guid recurringId, DateOnly dueDate)
    {
        EnsureEditable();
        RecurringExpenseId = recurringId;
        RecurringDueDate = dueDate;
    }

    public void Submit(DateTime nowUtc, Guid userId)
    {
        if (!IsEditable) throw new DomainException("Only draft or rejected expenses can be submitted.");
        Status = ExpenseStatus.PendingApproval;
        SubmittedBy = userId;
        SubmittedUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        RejectionReason = string.Empty;
    }

    /// <summary>Approve or reject a submitted expense. The caller checks the approver may approve this expense.</summary>
    public void Decide(bool approve, string reason, DateTime nowUtc, Guid userId)
    {
        if (Status != ExpenseStatus.PendingApproval) throw new DomainException("Only expenses waiting for approval can be approved or rejected.");
        var text = (reason ?? string.Empty).Trim();
        if (!approve && text.Length == 0) throw new DomainException("Enter the reason for rejecting.");
        Status = approve ? ExpenseStatus.Approved : ExpenseStatus.Rejected;
        RejectionReason = approve ? string.Empty : Text(text, 300, "Reason");
        DecidedBy = userId;
        DecidedUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
    }

    public void MarkPosted(string number, Guid voucherId, DateTime nowUtc, Guid userId)
    {
        if (Status != ExpenseStatus.Approved) throw new DomainException("Only approved expenses can be posted.");
        Status = ExpenseStatus.Posted;
        Number = number;
        VoucherId = voucherId;
        PostedUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        PostedBy = userId;
    }

    public void EnsureEditable()
    {
        if (!IsEditable) throw new DomainException(Status switch
        {
            ExpenseStatus.Posted => $"Expense {Number} is posted and cannot be changed. Reverse it instead.",
            ExpenseStatus.PendingApproval => "This expense is waiting for approval. It can be changed after it is rejected.",
            _ => "This expense is approved. Post it, or ask the approver to reject it so it can be changed.",
        });
    }

    private static string Text(string? value, int max, string what)
    {
        var clean = (value ?? string.Empty).Trim();
        if (clean.Length > max) throw new DomainException($"{what} can have up to {max} characters.");
        return clean;
    }
}

/// <summary>One line of an expense: category, amount before tax and the GST stored at the rate used.</summary>
public sealed class ExpenseLine : ICompanyOwned
{
    private ExpenseLine()
    {
        Description = string.Empty;
    }

    public ExpenseLine(Guid id, Guid companyId, Guid expenseId, int lineNumber, Guid categoryId, string description, decimal amount, Guid? taxCategoryId,
        decimal taxRatePercent, decimal cgst, decimal sgst, decimal igst, bool inputTaxEligible) : this()
    {
        if (lineNumber < 1) throw new DomainException("Line numbers start at 1.");
        if (amount <= 0) throw new DomainException("Each line needs an amount more than zero.");
        Money.EnsureValid(amount);
        var text = (description ?? string.Empty).Trim();
        if (text.Length > 200) throw new DomainException("Descriptions can have up to 200 characters.");
        Id = id;
        CompanyId = companyId;
        ExpenseId = expenseId;
        LineNumber = lineNumber;
        CategoryId = categoryId;
        Description = text;
        Amount = amount;
        TaxCategoryId = taxCategoryId;
        TaxRatePercent = taxRatePercent;
        (Cgst, Sgst, Igst) = (cgst, sgst, igst);
        InputTaxEligible = inputTaxEligible;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid ExpenseId { get; private set; }

    public int LineNumber { get; private set; }

    public Guid CategoryId { get; private set; }

    public string Description { get; private set; }

    /// <summary>Amount before GST.</summary>
    public decimal Amount { get; private set; }

    public Guid? TaxCategoryId { get; private set; }

    public decimal TaxRatePercent { get; private set; }

    public decimal Cgst { get; private set; }

    public decimal Sgst { get; private set; }

    public decimal Igst { get; private set; }

    /// <summary>False when the GST cannot be claimed (for example food or personal use); it is then added to the expense.</summary>
    public bool InputTaxEligible { get; private set; }

    public bool IsLocked { get; private set; }

    public decimal Tax => Cgst + Sgst + Igst;

    public void Lock() => IsLocked = true;
}

/// <summary>A receipt or bill attached to an expense. Contents are checked by type and size before they are stored.</summary>
public sealed class ExpenseAttachment : ICompanyOwned
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public const int MaxPerExpense = 5;

    private ExpenseAttachment()
    {
        FileName = ContentType = Sha256 = string.Empty;
        Data = [];
    }

    public ExpenseAttachment(Guid id, Guid companyId, Guid expenseId, string fileName, string contentType, byte[] data, string sha256, DateTime addedUtc,
        Guid addedBy) : this()
    {
        Id = id;
        CompanyId = companyId;
        ExpenseId = expenseId;
        FileName = fileName;
        ContentType = contentType;
        Data = data;
        SizeBytes = data.Length;
        Sha256 = sha256;
        AddedUtc = DateTime.SpecifyKind(addedUtc, DateTimeKind.Utc);
        AddedBy = addedBy;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid ExpenseId { get; private set; }

    public string FileName { get; private set; }

    public string ContentType { get; private set; }

    public long SizeBytes { get; private set; }

    public string Sha256 { get; private set; }

    public byte[] Data { get; private set; }

    public DateTime AddedUtc { get; private set; }

    public Guid AddedBy { get; private set; }
}

/// <summary>Reverses a posted expense and its voucher. Append-only; at most one per expense.</summary>
public sealed class ExpenseReversal : ICompanyOwned
{
    private ExpenseReversal()
    {
        Reason = string.Empty;
    }

    public ExpenseReversal(Guid id, Guid companyId, Guid expenseId, DateOnly date, string reason, Guid reversalVoucherId, DateTime reversedUtc, Guid reversedBy) : this()
    {
        var text = (reason ?? string.Empty).Trim();
        if (text.Length is 0 or > 300) throw new DomainException("Enter the reason for reversing (up to 300 characters).");
        Id = id;
        CompanyId = companyId;
        ExpenseId = expenseId;
        Date = date;
        Reason = text;
        ReversalVoucherId = reversalVoucherId;
        ReversedUtc = DateTime.SpecifyKind(reversedUtc, DateTimeKind.Utc);
        ReversedBy = reversedBy;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid ExpenseId { get; private set; }

    public DateOnly Date { get; private set; }

    public string Reason { get; private set; }

    public Guid ReversalVoucherId { get; private set; }

    public DateTime ReversedUtc { get; private set; }

    public Guid ReversedBy { get; private set; }
}

/// <summary>
/// A recurring expense template, for example monthly rent. "Generate due expenses" creates one draft for every due date
/// up to the chosen day and moves <see cref="NextDueDate"/> on. Nothing is ever posted automatically.
/// </summary>
public sealed class RecurringExpense : ICompanyOwned, IAudited
{
    private RecurringExpense()
    {
        Name = Description = Department = string.Empty;
    }

    public RecurringExpense(Guid id, Guid companyId) : this()
    {
        Id = id;
        CompanyId = companyId;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Name { get; private set; }

    public Guid CategoryId { get; private set; }

    public string Description { get; private set; }

    public decimal Amount { get; private set; }

    public Guid? TaxCategoryId { get; private set; }

    public bool InputTaxEligible { get; private set; }

    public Guid? SupplierId { get; private set; }

    public Guid? PaidFromAccountId { get; private set; }

    public Billing.PaymentMethod Method { get; private set; }

    public Guid? BranchId { get; private set; }

    public string Department { get; private set; }

    public RecurrenceFrequency Frequency { get; private set; }

    public DateOnly NextDueDate { get; private set; }

    public DateOnly? EndDate { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(string name, Guid categoryId, string description, decimal amount, Guid? taxCategoryId, bool inputTaxEligible, Guid? supplierId,
        Guid? paidFromAccountId, Billing.PaymentMethod method, Guid? branchId, string department, RecurrenceFrequency frequency, DateOnly nextDueDate,
        DateOnly? endDate)
    {
        var clean = (name ?? string.Empty).Trim();
        if (clean.Length is 0 or > 60) throw new DomainException("Enter a name of up to 60 characters.");
        if (amount <= 0) throw new DomainException("Enter an amount more than zero.");
        Money.EnsureValid(amount);
        if (paidFromAccountId is null) throw new DomainException("Choose the cash or bank account it is paid from.");
        if (endDate is { } end && end < nextDueDate) throw new DomainException("The end date must be on or after the next due date.");
        var text = (description ?? string.Empty).Trim();
        if (text.Length > 200) throw new DomainException("Descriptions can have up to 200 characters.");
        var dept = (department ?? string.Empty).Trim();
        if (dept.Length > 60) throw new DomainException("Department can have up to 60 characters.");
        (Name, CategoryId, Description, Amount, TaxCategoryId, InputTaxEligible) = (clean, categoryId, text, amount, taxCategoryId, inputTaxEligible);
        (SupplierId, PaidFromAccountId, Method, BranchId, Department) = (supplierId, paidFromAccountId, method, branchId, dept);
        (Frequency, NextDueDate, EndDate) = (frequency, nextDueDate, endDate);
    }

    public void SetActive(bool active) => IsActive = active;

    /// <summary>The due dates up to <paramref name="asOf"/> (at most 60), advancing <see cref="NextDueDate"/> past them.</summary>
    public IReadOnlyList<DateOnly> TakeDueDates(DateOnly asOf)
    {
        var dates = new List<DateOnly>();
        if (!IsActive) return dates;
        while (NextDueDate <= asOf && (EndDate is null || NextDueDate <= EndDate) && dates.Count < 60)
        {
            dates.Add(NextDueDate);
            NextDueDate = Next(NextDueDate, Frequency);
        }

        if (EndDate is { } end && NextDueDate > end) IsActive = false;
        return dates;
    }

    public static DateOnly Next(DateOnly date, RecurrenceFrequency frequency) => frequency switch
    {
        RecurrenceFrequency.Weekly => date.AddDays(7),
        RecurrenceFrequency.Monthly => date.AddMonths(1),
        RecurrenceFrequency.Quarterly => date.AddMonths(3),
        _ => date.AddYears(1),
    };
}

/// <summary>Expense options for a company. Also the seed marker for expense numbering on upgrade.</summary>
public sealed class ExpenseSettings : ICompanyOwned, IAudited
{
    private ExpenseSettings()
    {
    }

    public ExpenseSettings(Guid id, Guid companyId)
    {
        Id = id;
        CompanyId = companyId;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>Lets a user approve an expense they created or submitted. Off by default.</summary>
    public bool AllowSelfApproval { get; private set; }

    public void Update(bool allowSelfApproval) => AllowSelfApproval = allowSelfApproval;
}
