using System.Security.Cryptography;
using Nexora.Application.Abstractions;
using Nexora.Application.Security;
using Nexora.Domain.Accounting;
using Nexora.Domain.Audit;
using Nexora.Domain.Billing;
using Nexora.Domain.Common;
using Nexora.Domain.Companies;
using Nexora.Domain.Expenses;
using Nexora.Domain.Inventory;
using Nexora.Domain.Security;
using Nexora.Domain.Users;

namespace Nexora.Application.Expenses;

/// <summary>A category: either an existing expense ledger, or a new ledger created under an expense group.</summary>
public sealed record ExpenseCategoryInput(Guid? Id, string Name, Guid? AccountId, Guid? NewLedgerGroupId = null, Guid? DefaultTaxCategoryId = null);

public sealed record ExpenseCategoryRow(Guid Id, string Name, Guid AccountId, string Account, Guid? DefaultTaxCategoryId, bool IsActive);

public sealed record ExpenseLineInput(Guid CategoryId, decimal Amount, string Description = "", Guid? TaxCategoryId = null, bool InputTaxEligible = true);

public sealed record ExpenseInput(Guid? Id, DateOnly Date, Guid PaidFromAccountId, IReadOnlyList<ExpenseLineInput> Lines, PaymentMethod Method = PaymentMethod.Cash,
    Guid? SupplierId = null, Guid? EmployeeUserId = null, string EmployeeName = "", Guid? BranchId = null, string Department = "", string Reference = "",
    string Narration = "");

public sealed record ExpenseFilter(DateOnly? From = null, DateOnly? To = null, ExpenseStatus? Status = null, Guid? CategoryId = null, Guid? BranchId = null,
    string? Department = null, PaymentMethod? Method = null, string? Search = null);

public sealed record ExpenseRow(Guid Id, string Number, DateOnly Date, ExpenseStatus Status, bool IsReversed, string Categories, string Supplier,
    string Employee, string PaidFrom, PaymentMethod Method, string Branch, string Department, decimal Taxable, decimal Tax, decimal Total, string CreatedBy)
{
    public string StatusLabel => IsReversed ? "Reversed" : Status switch
    {
        ExpenseStatus.PendingApproval => "Pending approval",
        _ => Status.ToString(),
    };
}

public sealed record ExpenseLineView(Guid Id, int LineNumber, Guid CategoryId, string Category, string Description, decimal Amount, Guid? TaxCategoryId,
    decimal TaxRatePercent, decimal Cgst, decimal Sgst, decimal Igst, bool InputTaxEligible);

public sealed record AttachmentRow(Guid Id, string FileName, string ContentType, long SizeBytes, DateTime AddedUtc);

public sealed record ExpenseView(Guid Id, string? Number, DateOnly Date, ExpenseStatus Status, bool IsReversed, string? ReversalReason, Guid PaidFromAccountId,
    string PaidFrom, PaymentMethod Method, Guid? SupplierId, string Supplier, Guid? EmployeeUserId, string EmployeeName, Guid? BranchId, string Department,
    string Reference, string Narration, bool IsInterState, decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst, decimal IneligibleTax, decimal Total,
    string CreatedBy, string SubmittedBy, string DecidedBy, string RejectionReason, IReadOnlyList<ExpenseLineView> Lines, IReadOnlyList<AttachmentRow> Attachments,
    bool CanApprove);

/// <summary>Totals of the filtered expenses grouped one way (category, branch, department, payment method or month).</summary>
public sealed record ExpenseTotalRow(string Group, int Count, decimal Taxable, decimal Tax, decimal Total);

public enum ExpenseGrouping
{
    Category,
    Branch,
    Department,
    Method,
    Month,
}

public sealed record RecurringExpenseInput(Guid? Id, string Name, Guid CategoryId, decimal Amount, RecurrenceFrequency Frequency, DateOnly NextDueDate,
    Guid PaidFromAccountId, string Description = "", Guid? TaxCategoryId = null, bool InputTaxEligible = true, Guid? SupplierId = null,
    PaymentMethod Method = PaymentMethod.Cash, Guid? BranchId = null, string Department = "", DateOnly? EndDate = null);

public sealed record RecurringExpenseRow(Guid Id, string Name, Guid CategoryId, string Category, decimal Amount, RecurrenceFrequency Frequency,
    DateOnly NextDueDate, DateOnly? EndDate, Guid PaidFromAccountId, string PaidFrom, bool IsActive, Guid? TaxCategoryId, bool InputTaxEligible,
    Guid? SupplierId, PaymentMethod Method, Guid? BranchId, string Department, string Description);

/// <summary>
/// Expense vouchers with categories, GST per line, attachments, approval, recurring templates and reports.
/// Approval: Draft → submit (expenses.manage) → approve or reject (expenses.approve; not your own expense unless the
/// company allows it) → post (expenses.manage). Posting creates an Expense voucher through the accounting engine:
/// expense ledgers and eligible input GST are debited, the cash or bank account is credited. See docs/ACCOUNTING_MAP.md.
/// </summary>
public sealed class ExpenseService(IUnitOfWorkFactory units, IAuthorizer authorizer, ILedgerPoster ledger, IClock clock)
{
    internal const string SourceType = "expense";

    // ----- Settings and categories -----

    public async Task<bool> AllowSelfApprovalAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.ExpensesView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        return (await SettingsAsync(unit, cancellationToken)).AllowSelfApproval;
    }

    /// <summary>Changing the self-approval rule is a company policy: it needs company.manage as well as expenses.approve.</summary>
    public async Task SetAllowSelfApprovalAsync(bool allow, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.ExpensesApprove);
        authorizer.Demand(Permissions.CompanyManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        (await SettingsAsync(unit, cancellationToken)).Update(allow);
        await unit.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ExpenseCategoryRow>> CategoriesAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.ExpensesView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var accounts = (await unit.ListAsync<Account>(cancellationToken: cancellationToken)).ToDictionary(a => a.Id, a => a.Name);
        return (await unit.ListAsync<ExpenseCategory>(c => includeInactive || c.IsActive, cancellationToken))
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(c => new ExpenseCategoryRow(c.Id, c.Name, c.AccountId, accounts.GetValueOrDefault(c.AccountId, "?"), c.DefaultTaxCategoryId, c.IsActive))
            .ToList();
    }

    /// <summary>Expense ledgers that categories can post to (expense nature, not groups, active).</summary>
    public async Task<IReadOnlyList<(Guid Id, string Name, bool IsGroup)>> ExpenseAccountsAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.ExpensesView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        return (await unit.ListAsync<Account>(a => a.Nature == AccountNature.Expense && a.IsActive, cancellationToken))
            .Where(a => a.Kind != AccountKind.Inventory)
            .OrderBy(a => a.Code, StringComparer.Ordinal).Select(a => (a.Id, $"{a.Code} {a.Name}", a.IsGroup)).ToList();
    }

    public async Task<Guid> SaveCategoryAsync(ExpenseCategoryInput input, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.ExpensesManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var name = (input.Name ?? string.Empty).Trim();
        var selfId = input.Id ?? Guid.Empty;
        if (await unit.AnyAsync<ExpenseCategory>(c => c.Name == name && c.Id != selfId, cancellationToken))
            throw new WorkspaceValidationException($"A category named \"{name}\" already exists.");
        Guid accountId;
        if (input.NewLedgerGroupId is { } groupId)
        {
            // Creating a ledger is an accounts change, so it needs accounts.manage too.
            authorizer.Demand(Permissions.AccountsManage);
            var group = await unit.FindAsync<Account>(groupId, cancellationToken) ?? throw new WorkspaceValidationException("Choose the expense group for the new ledger.");
            if (!group.IsGroup || group.Nature != AccountNature.Expense) throw new WorkspaceValidationException("New expense ledgers go under an expense group.");
            if (await unit.AnyAsync<Account>(a => a.Name == name, cancellationToken)) throw new WorkspaceValidationException($"An account named \"{name}\" already exists. Choose it instead.");
            var codes = (await unit.ListAsync<Account>(cancellationToken: cancellationToken)).Select(a => a.Code).ToHashSet(StringComparer.Ordinal);
            var code = Enumerable.Range(1, 9999).Select(n => $"{group.Code}-{n:000}").First(c => !codes.Contains(c));
            var account = Guard(() => new Account(Guid.NewGuid(), session.CompanyId, code, name, AccountNature.Expense, AccountKind.General, group.Id, null));
            unit.Add(account);
            accountId = account.Id;
        }
        else
        {
            accountId = input.AccountId ?? throw new WorkspaceValidationException("Choose the expense ledger the category posts to.");
            var account = await unit.FindAsync<Account>(accountId, cancellationToken) ?? throw new WorkspaceValidationException("That account no longer exists.");
            if (account.IsGroup || account.Nature != AccountNature.Expense || !account.IsActive || account.Kind == AccountKind.Inventory)
                throw new WorkspaceValidationException($"{account.Name} cannot be used: choose an active expense ledger, not a group.");
        }

        if (input.DefaultTaxCategoryId is { } tax && await unit.FindAsync<TaxCategory>(tax, cancellationToken) is null)
            throw new WorkspaceValidationException("That tax category no longer exists.");
        ExpenseCategory category;
        if (input.Id is { } id)
        {
            category = await unit.FindAsync<ExpenseCategory>(id, cancellationToken) ?? throw new WorkspaceValidationException("That category no longer exists.");
            Guard(() => category.Update(name, accountId, input.DefaultTaxCategoryId));
        }
        else
        {
            category = Guard(() => new ExpenseCategory(Guid.NewGuid(), session.CompanyId, name, accountId, input.DefaultTaxCategoryId));
            unit.Add(category);
        }

        await unit.CommitAsync(cancellationToken);
        return category.Id;
    }

    public async Task SetCategoryActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.ExpensesManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var category = await unit.FindAsync<ExpenseCategory>(id, cancellationToken) ?? throw new WorkspaceValidationException("That category no longer exists.");
        category.SetActive(active);
        await unit.CommitAsync(cancellationToken);
    }

    // ----- Expenses -----

    public async Task<Guid> SaveDraftAsync(ExpenseInput input, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.ExpensesManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        Expense expense;
        if (input.Id is { } id)
        {
            expense = await unit.FindAsync<Expense>(id, cancellationToken) ?? throw new WorkspaceValidationException("That expense no longer exists.");
            Guard(expense.EnsureEditable);
            foreach (var line in await unit.ListAsync<ExpenseLine>(l => l.ExpenseId == id, cancellationToken)) unit.Remove(line);
        }
        else
        {
            expense = new Expense(Guid.NewGuid(), session.CompanyId, input.Date, clock.UtcNow, session.UserId);
            unit.Add(expense);
        }

        await ComposeAsync(unit, session, expense, input, cancellationToken);
        await unit.CommitAsync(cancellationToken);
        return expense.Id;
    }

    public async Task DeleteDraftAsync(Guid id, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.ExpensesManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var expense = await unit.FindAsync<Expense>(id, cancellationToken) ?? throw new WorkspaceValidationException("That expense no longer exists.");
        Guard(expense.EnsureEditable);
        foreach (var line in await unit.ListAsync<ExpenseLine>(l => l.ExpenseId == id, cancellationToken)) unit.Remove(line);
        foreach (var file in await unit.ListAsync<ExpenseAttachment>(a => a.ExpenseId == id, cancellationToken)) unit.Remove(file);
        unit.Remove(expense);
        await unit.CommitAsync(cancellationToken);
    }

    public async Task SubmitAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.ExpensesManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var expense = await unit.FindAsync<Expense>(id, cancellationToken) ?? throw new WorkspaceValidationException("That expense no longer exists.");
        Guard(() => expense.Submit(clock.UtcNow, session.UserId));
        unit.Record(new AuditEvent("Submitted", nameof(Expense), id.ToString(), $"Submitted expense of {expense.Total:N2} for approval"));
        await unit.CommitAsync(cancellationToken);
    }

    public Task ApproveAsync(Guid id, CancellationToken cancellationToken = default) => DecideAsync(id, true, string.Empty, cancellationToken);

    public Task RejectAsync(Guid id, string reason, CancellationToken cancellationToken = default) => DecideAsync(id, false, reason, cancellationToken);

    private async Task DecideAsync(Guid id, bool approve, string reason, CancellationToken cancellationToken)
    {
        var session = authorizer.Demand(Permissions.ExpensesApprove);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var expense = await unit.FindAsync<Expense>(id, cancellationToken) ?? throw new WorkspaceValidationException("That expense no longer exists.");
        var settings = await SettingsAsync(unit, cancellationToken);
        if (!settings.AllowSelfApproval && (expense.CreatedBy == session.UserId || expense.SubmittedBy == session.UserId))
            throw new WorkspaceValidationException("You cannot approve or reject an expense you created or submitted. Ask another approver, or an administrator can allow self-approval.");
        Guard(() => expense.Decide(approve, reason, clock.UtcNow, session.UserId));
        unit.Record(new AuditEvent(approve ? "Approved" : "Rejected", nameof(Expense), id.ToString(),
            approve ? $"Approved expense of {expense.Total:N2}" : $"Rejected expense of {expense.Total:N2}: {reason.Trim()}"));
        await unit.CommitAsync(cancellationToken);
    }

    /// <summary>Posts an approved expense: number, Expense voucher, lines locked, in one transaction.</summary>
    public async Task<string> PostAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.ExpensesManage);
        using var lockHandle = await ledger.AcquireAsync(cancellationToken);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var expense = await unit.FindAsync<Expense>(id, cancellationToken) ?? throw new WorkspaceValidationException("That expense no longer exists.");
        if (expense.Status == ExpenseStatus.Posted) throw new WorkspaceValidationException($"Expense {expense.Number} is already posted.");
        if (expense.Status != ExpenseStatus.Approved) throw new WorkspaceValidationException("Only approved expenses can be posted. Submit it for approval first.");
        var lines = (await unit.ListAsync<ExpenseLine>(l => l.ExpenseId == id, cancellationToken)).OrderBy(l => l.LineNumber).ToList();
        if (lines.Count == 0) throw new WorkspaceValidationException("The expense has no lines.");
        var categories = (await unit.ListAsync<ExpenseCategory>(cancellationToken: cancellationToken)).ToDictionary(c => c.Id);
        var paidFrom = await CashOrBankAsync(unit, expense.PaidFromAccountId!.Value, cancellationToken);

        var number = await NumberAllocator.NextAsync(unit, session.CompanyId, ExpenseDocumentTypes.Expense, FinancialYear.Containing(expense.Date),
            candidate => unit.AnyAsync<Expense>(e => e.Number == candidate, cancellationToken), cancellationToken);

        // Debits: each category's ledger (plus GST that cannot be claimed) and the claimable input GST. Credit: cash or bank.
        var debits = new Dictionary<Guid, decimal>();
        void Debit(Guid account, decimal amount)
        {
            if (amount != 0) debits[account] = debits.GetValueOrDefault(account) + amount;
        }

        foreach (var line in lines)
        {
            Debit(categories[line.CategoryId].AccountId, line.Amount + (line.InputTaxEligible ? 0 : line.Tax));
            if (!line.InputTaxEligible) continue;
            if (line.Cgst != 0) Debit(await ledger.AccountAsync(unit, LedgerAccountRole.InputCgst, cancellationToken), line.Cgst);
            if (line.Sgst != 0) Debit(await ledger.AccountAsync(unit, LedgerAccountRole.InputSgst, cancellationToken), line.Sgst);
            if (line.Igst != 0) Debit(await ledger.AccountAsync(unit, LedgerAccountRole.InputIgst, cancellationToken), line.Igst);
        }

        var postingLines = debits.Select(d => new LedgerPostingLine(d.Key, d.Value, 0)).Append(new LedgerPostingLine(paidFrom.Id, 0, expense.Total)).ToList();
        if (postingLines.Sum(l => l.Debit) != expense.Total) throw new InvalidOperationException("Expense lines do not add up to the total.");
        var narration = $"Expense {number}" + (expense.Narration.Length > 0 ? " · " + expense.Narration : string.Empty);
        var voucherId = await ledger.PostAsync(unit, new LedgerPosting(expense.Date, narration, SourceType, expense.Id, postingLines, VoucherType.Expense), cancellationToken);
        foreach (var line in lines) line.Lock();
        Guard(() => expense.MarkPosted(number, voucherId, clock.UtcNow, session.UserId));
        unit.Record(new AuditEvent("Posted", nameof(Expense), id.ToString(), $"Posted expense {number} for {expense.Total:N2}"));
        await unit.CommitAsync(cancellationToken);
        return number;
    }

    /// <summary>Reverses a posted expense and its voucher (the only way to correct it).</summary>
    public async Task ReverseAsync(Guid id, DateOnly date, string reason, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.ExpensesManage);
        using var lockHandle = await ledger.AcquireAsync(cancellationToken);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var expense = await unit.FindAsync<Expense>(id, cancellationToken) ?? throw new WorkspaceValidationException("That expense no longer exists.");
        if (expense.Status != ExpenseStatus.Posted || expense.VoucherId is not { } voucherId) throw new WorkspaceValidationException("Only posted expenses can be reversed.");
        if (await unit.AnyAsync<ExpenseReversal>(r => r.ExpenseId == id, cancellationToken)) throw new WorkspaceValidationException($"Expense {expense.Number} is already reversed.");
        if (date < expense.Date) throw new WorkspaceValidationException("A reversal cannot be dated before the expense.");
        var reversal = await ledger.ReverseAsync(unit, voucherId, date, $"Reversal of expense {expense.Number}", cancellationToken);
        Guard(() => unit.Add(new ExpenseReversal(Guid.NewGuid(), session.CompanyId, id, date, reason, reversal, clock.UtcNow, session.UserId)));
        unit.Record(new AuditEvent("Reversed", nameof(Expense), id.ToString(), $"Reversed expense {expense.Number}: {reason.Trim()}"));
        await unit.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ExpenseRow>> ListAsync(ExpenseFilter filter, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.ExpensesView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        return (await LoadRowsAsync(unit, filter, cancellationToken)).Select(r => r.Row).ToList();
    }

    /// <summary>Totals of the filtered expenses by category (from the lines), branch, department, payment method or month. Reversed expenses are left out.</summary>
    public async Task<IReadOnlyList<ExpenseTotalRow>> TotalsAsync(ExpenseFilter filter, ExpenseGrouping grouping, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.ExpensesView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var rows = (await LoadRowsAsync(unit, filter, cancellationToken)).Where(r => !r.Row.IsReversed).ToList();
        if (grouping == ExpenseGrouping.Category)
        {
            var categories = (await unit.ListAsync<ExpenseCategory>(cancellationToken: cancellationToken)).ToDictionary(c => c.Id, c => c.Name);
            return rows.SelectMany(r => r.Lines)
                .Where(l => filter.CategoryId is null || l.CategoryId == filter.CategoryId)
                .GroupBy(l => categories.GetValueOrDefault(l.CategoryId, "?"))
                .Select(g => new ExpenseTotalRow(g.Key, g.Select(l => l.ExpenseId).Distinct().Count(), g.Sum(l => l.Amount), g.Sum(l => l.Tax), g.Sum(l => l.Amount + l.Tax)))
                .OrderByDescending(t => t.Total).ToList();
        }

        string Key(ExpenseRow r) => grouping switch
        {
            ExpenseGrouping.Branch => r.Branch.Length > 0 ? r.Branch : "(no branch)",
            ExpenseGrouping.Department => r.Department.Length > 0 ? r.Department : "(no department)",
            ExpenseGrouping.Method => r.Method.ToString(),
            _ => r.Date.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture),
        };
        return rows.Select(r => r.Row).GroupBy(Key)
            .Select(g => new ExpenseTotalRow(g.Key, g.Count(), g.Sum(r => r.Taxable), g.Sum(r => r.Tax), g.Sum(r => r.Total)))
            .OrderBy(t => grouping == ExpenseGrouping.Month ? t.Group : string.Empty, StringComparer.Ordinal).ThenByDescending(t => t.Total).ToList();
    }

    public async Task<ExpenseView> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.ExpensesView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var e = await unit.FindAsync<Expense>(id, cancellationToken) ?? throw new WorkspaceValidationException("That expense no longer exists.");
        var lines = (await unit.ListAsync<ExpenseLine>(l => l.ExpenseId == id, cancellationToken)).OrderBy(l => l.LineNumber).ToList();
        var categories = (await unit.ListAsync<ExpenseCategory>(cancellationToken: cancellationToken)).ToDictionary(c => c.Id, c => c.Name);
        var accounts = (await unit.ListAsync<Account>(cancellationToken: cancellationToken)).ToDictionary(a => a.Id, a => a.Name);
        var users = (await unit.ListAsync<User>(cancellationToken: cancellationToken)).ToDictionary(u => u.Id, u => u.DisplayName);
        var supplier = e.SupplierId is { } s ? await unit.FindAsync<Party>(s, cancellationToken) : null;
        var reversal = (await unit.ListAsync<ExpenseReversal>(r => r.ExpenseId == id, cancellationToken)).SingleOrDefault();
        var files = (await unit.ListAsync<ExpenseAttachment>(a => a.ExpenseId == id, cancellationToken))
            .OrderBy(a => a.AddedUtc).Select(a => new AttachmentRow(a.Id, a.FileName, a.ContentType, a.SizeBytes, a.AddedUtc)).ToList();
        var settings = await SettingsAsync(unit, cancellationToken);
        var canApprove = e.Status == ExpenseStatus.PendingApproval && session.Has(Permissions.ExpensesApprove)
            && (settings.AllowSelfApproval || (e.CreatedBy != session.UserId && e.SubmittedBy != session.UserId));
        string Name(Guid? user) => user is { } u ? users.GetValueOrDefault(u, "?") : string.Empty;
        return new ExpenseView(e.Id, e.Number, e.Date, e.Status, reversal is not null, reversal?.Reason, e.PaidFromAccountId!.Value,
            accounts.GetValueOrDefault(e.PaidFromAccountId.Value, "?"), e.Method, e.SupplierId, supplier?.Name ?? string.Empty, e.EmployeeUserId,
            e.EmployeeUserId is { } emp ? users.GetValueOrDefault(emp, e.EmployeeName) : e.EmployeeName, e.BranchId, e.Department, e.Reference, e.Narration,
            e.IsInterState, e.Taxable, e.Cgst, e.Sgst, e.Igst, e.IneligibleTax, e.Total, Name(e.CreatedBy), Name(e.SubmittedBy), Name(e.DecidedBy), e.RejectionReason,
            lines.Select(l => new ExpenseLineView(l.Id, l.LineNumber, l.CategoryId, categories.GetValueOrDefault(l.CategoryId, "?"), l.Description, l.Amount,
                l.TaxCategoryId, l.TaxRatePercent, l.Cgst, l.Sgst, l.Igst, l.InputTaxEligible)).ToList(),
            files, canApprove);
    }

    // ----- Attachments -----

    /// <summary>
    /// Attaches a receipt or bill (PDF, PNG or JPEG, up to 5 MB, at most 5 per expense) while the expense can be edited.
    /// The type is checked from the file's content, not only its name; the file name is reduced to a safe name.
    /// </summary>
    public async Task<Guid> AddAttachmentAsync(Guid expenseId, string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.ExpensesManage);
        var (cleanName, contentType) = AttachmentRules.Validate(fileName, content);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var expense = await unit.FindAsync<Expense>(expenseId, cancellationToken) ?? throw new WorkspaceValidationException("That expense no longer exists.");
        Guard(expense.EnsureEditable);
        if ((await unit.ListAsync<ExpenseAttachment>(a => a.ExpenseId == expenseId, cancellationToken)).Count >= ExpenseAttachment.MaxPerExpense)
            throw new WorkspaceValidationException($"An expense can have at most {ExpenseAttachment.MaxPerExpense} attachments.");
        var attachment = new ExpenseAttachment(Guid.NewGuid(), session.CompanyId, expenseId, cleanName, contentType, content,
            Convert.ToHexString(SHA256.HashData(content)), clock.UtcNow, session.UserId);
        unit.Add(attachment);
        unit.Record(new AuditEvent("Attached", nameof(Expense), expenseId.ToString(), $"Attached {cleanName} ({content.Length:N0} bytes)"));
        await unit.CommitAsync(cancellationToken);
        return attachment.Id;
    }

    public async Task RemoveAttachmentAsync(Guid attachmentId, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.ExpensesManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var attachment = await unit.FindAsync<ExpenseAttachment>(attachmentId, cancellationToken) ?? throw new WorkspaceValidationException("That attachment no longer exists.");
        var expense = await unit.FindAsync<Expense>(attachment.ExpenseId, cancellationToken);
        if (expense is not null) Guard(expense.EnsureEditable);
        unit.Remove(attachment);
        unit.Record(new AuditEvent("Detached", nameof(Expense), attachment.ExpenseId.ToString(), $"Removed attachment {attachment.FileName}"));
        await unit.CommitAsync(cancellationToken);
    }

    public async Task<(string FileName, string ContentType, byte[] Content)> AttachmentAsync(Guid attachmentId, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.ExpensesView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var a = await unit.FindAsync<ExpenseAttachment>(attachmentId, cancellationToken) ?? throw new WorkspaceValidationException("That attachment no longer exists.");
        if (Convert.ToHexString(SHA256.HashData(a.Data)) != a.Sha256) throw new WorkspaceValidationException("The stored attachment does not match its checksum and may be damaged.");
        return (a.FileName, a.ContentType, a.Data);
    }

    // ----- Recurring expenses -----

    public async Task<IReadOnlyList<RecurringExpenseRow>> RecurringAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.ExpensesView);
        await using var unit = await units.BeginAsync(readOnly: true, cancellationToken);
        var categories = (await unit.ListAsync<ExpenseCategory>(cancellationToken: cancellationToken)).ToDictionary(c => c.Id, c => c.Name);
        var accounts = (await unit.ListAsync<Account>(cancellationToken: cancellationToken)).ToDictionary(a => a.Id, a => a.Name);
        return (await unit.ListAsync<RecurringExpense>(cancellationToken: cancellationToken)).OrderBy(r => r.NextDueDate)
            .Select(r => new RecurringExpenseRow(r.Id, r.Name, r.CategoryId, categories.GetValueOrDefault(r.CategoryId, "?"), r.Amount, r.Frequency, r.NextDueDate,
                r.EndDate, r.PaidFromAccountId!.Value, accounts.GetValueOrDefault(r.PaidFromAccountId.Value, "?"), r.IsActive, r.TaxCategoryId, r.InputTaxEligible,
                r.SupplierId, r.Method, r.BranchId, r.Department, r.Description))
            .ToList();
    }

    public async Task<Guid> SaveRecurringAsync(RecurringExpenseInput input, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.ExpensesManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var name = (input.Name ?? string.Empty).Trim();
        var selfId = input.Id ?? Guid.Empty;
        if (await unit.AnyAsync<RecurringExpense>(r => r.Name == name && r.Id != selfId, cancellationToken))
            throw new WorkspaceValidationException($"A recurring expense named \"{name}\" already exists.");
        _ = await ActiveCategoryAsync(unit, input.CategoryId, cancellationToken);
        await CashOrBankAsync(unit, input.PaidFromAccountId, cancellationToken);
        RecurringExpense recurring;
        if (input.Id is { } id)
        {
            recurring = await unit.FindAsync<RecurringExpense>(id, cancellationToken) ?? throw new WorkspaceValidationException("That recurring expense no longer exists.");
        }
        else
        {
            recurring = new RecurringExpense(Guid.NewGuid(), session.CompanyId);
            unit.Add(recurring);
        }

        Guard(() => recurring.Update(name, input.CategoryId, input.Description, input.Amount, input.TaxCategoryId, input.InputTaxEligible, input.SupplierId,
            input.PaidFromAccountId, input.Method, input.BranchId, input.Department, input.Frequency, input.NextDueDate, input.EndDate));
        await unit.CommitAsync(cancellationToken);
        return recurring.Id;
    }

    public async Task SetRecurringActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(Permissions.ExpensesManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var recurring = await unit.FindAsync<RecurringExpense>(id, cancellationToken) ?? throw new WorkspaceValidationException("That recurring expense no longer exists.");
        recurring.SetActive(active);
        await unit.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Creates a draft for every due date up to <paramref name="asOf"/> of every active recurring expense and moves their
    /// next due dates on. Drafts still need approval and posting; nothing is posted here. Returns the new drafts.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> GenerateDueAsync(DateOnly asOf, CancellationToken cancellationToken = default)
    {
        var session = authorizer.Demand(Permissions.ExpensesManage);
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        var created = new List<Guid>();
        foreach (var recurring in await unit.ListAsync<RecurringExpense>(r => r.IsActive && r.NextDueDate <= asOf, cancellationToken))
        {
            var recurringId = recurring.Id;
            foreach (var due in recurring.TakeDueDates(asOf))
            {
                var dueDate = due;
                if (await unit.AnyAsync<Expense>(e => e.RecurringExpenseId == recurringId && e.RecurringDueDate == dueDate, cancellationToken)) continue;
                var expense = new Expense(Guid.NewGuid(), session.CompanyId, due, clock.UtcNow, session.UserId);
                unit.Add(expense);
                await ComposeAsync(unit, session, expense, new ExpenseInput(expense.Id, due, recurring.PaidFromAccountId!.Value,
                    [new ExpenseLineInput(recurring.CategoryId, recurring.Amount, recurring.Description, recurring.TaxCategoryId, recurring.InputTaxEligible)],
                    recurring.Method, recurring.SupplierId, BranchId: recurring.BranchId, Department: recurring.Department,
                    Narration: $"{recurring.Name} due {due:dd-MM-yyyy}"), cancellationToken);
                expense.LinkRecurring(recurring.Id, due);
                created.Add(expense.Id);
            }
        }

        if (created.Count > 0) unit.Record(new AuditEvent("Generated", nameof(RecurringExpense), string.Empty, $"Generated {created.Count} recurring expense draft(s) up to {asOf:dd-MM-yyyy}"));
        await unit.CommitAsync(cancellationToken);
        return created;
    }

    // ----- Internals -----

    /// <summary>Validates the header, recalculates every line's GST from its tax category and sets the totals.</summary>
    private static async Task ComposeAsync(IUnitOfWork unit, SessionInfo session, Expense expense, ExpenseInput input, CancellationToken cancellationToken)
    {
        if (input.Lines.Count == 0) throw new WorkspaceValidationException("Add at least one line.");
        if (input.Lines.Count > 50) throw new WorkspaceValidationException("An expense can have at most 50 lines.");
        await CashOrBankAsync(unit, input.PaidFromAccountId, cancellationToken);
        var company = await unit.FindAsync<Company>(session.CompanyId, cancellationToken) ?? throw new WorkspaceValidationException("Sign in again.");
        Party? supplier = null;
        if (input.SupplierId is { } supplierId)
        {
            supplier = await unit.FindAsync<Party>(supplierId, cancellationToken) ?? throw new WorkspaceValidationException("That supplier no longer exists.");
            if (supplier.Kind != PartyKind.Supplier) throw new WorkspaceValidationException("Choose a supplier.");
        }

        if (input.EmployeeUserId is { } employee && await unit.FindAsync<User>(employee, cancellationToken) is null)
            throw new WorkspaceValidationException("That employee is not a user of this company.");
        if (input.BranchId is { } branch && await unit.FindAsync<Branch>(branch, cancellationToken) is null)
            throw new WorkspaceValidationException("That branch no longer exists.");
        var interState = supplier is { StateCode.Length: > 0 } && supplier.StateCode != company.StateCode;
        Guard(() => expense.UpdateDraft(input.Date, input.SupplierId, input.EmployeeUserId, input.EmployeeName, input.PaidFromAccountId, input.Method, input.BranchId,
            input.Department, input.Reference, input.Narration, interState));

        var taxes = (await unit.ListAsync<TaxCategory>(cancellationToken: cancellationToken)).ToDictionary(t => t.Id);
        decimal taxable = 0, cgst = 0, sgst = 0, igst = 0, ineligible = 0;
        for (var i = 0; i < input.Lines.Count; i++)
        {
            var entry = input.Lines[i];
            var number = i + 1;
            var category = await ActiveCategoryAsync(unit, entry.CategoryId, cancellationToken);
            var taxId = entry.TaxCategoryId ?? category.DefaultTaxCategoryId;
            TaxCategory? tax = null;
            if (taxId is { } t && !taxes.TryGetValue(t, out tax)) throw new WorkspaceValidationException($"Line {number}: that tax category no longer exists.");
            LineAmounts amounts;
            try
            {
                amounts = BillingCalculator.Line(1, entry.Amount, 0, 0, tax?.ReferenceRatePercent ?? 0, interState, pricesIncludeTax: false);
            }
            catch (DomainException ex)
            {
                throw new WorkspaceValidationException($"Line {number}: {ex.Message}");
            }

            var line = Guard(() => new ExpenseLine(Guid.NewGuid(), session.CompanyId, expense.Id, number, category.Id, entry.Description, entry.Amount, tax?.Id,
                tax?.ReferenceRatePercent ?? 0, amounts.Cgst, amounts.Sgst, amounts.Igst, entry.InputTaxEligible));
            unit.Add(line);
            taxable += line.Amount;
            if (entry.InputTaxEligible) (cgst, sgst, igst) = (cgst + line.Cgst, sgst + line.Sgst, igst + line.Igst);
            else ineligible += line.Tax;
        }

        Guard(() => expense.SetTotals(taxable, cgst, sgst, igst, ineligible));
    }

    private sealed record LoadedRow(ExpenseRow Row, IReadOnlyList<ExpenseLine> Lines);

    private static async Task<List<LoadedRow>> LoadRowsAsync(IUnitOfWork unit, ExpenseFilter filter, CancellationToken cancellationToken)
    {
        var from = filter.From ?? DateOnly.MinValue;
        var to = filter.To ?? DateOnly.MaxValue;
        var expenses = await unit.ListAsync<Expense>(e => e.Date >= from && e.Date <= to && (filter.Status == null || e.Status == filter.Status)
            && (filter.BranchId == null || e.BranchId == filter.BranchId) && (filter.Method == null || e.Method == filter.Method), cancellationToken);
        var ids = expenses.Select(e => e.Id).ToList();
        var lines = (await unit.ListAsync<ExpenseLine>(l => ids.Contains(l.ExpenseId), cancellationToken)).GroupBy(l => l.ExpenseId).ToDictionary(g => g.Key, g => g.ToList());
        var categories = (await unit.ListAsync<ExpenseCategory>(cancellationToken: cancellationToken)).ToDictionary(c => c.Id, c => c.Name);
        var accounts = (await unit.ListAsync<Account>(cancellationToken: cancellationToken)).ToDictionary(a => a.Id, a => a.Name);
        var parties = (await unit.ListAsync<Party>(p => p.Kind == PartyKind.Supplier, cancellationToken)).ToDictionary(p => p.Id, p => p.Name);
        var branches = (await unit.ListAsync<Branch>(cancellationToken: cancellationToken)).ToDictionary(b => b.Id, b => b.Name);
        var users = (await unit.ListAsync<User>(cancellationToken: cancellationToken)).ToDictionary(u => u.Id, u => u.DisplayName);
        var reversed = (await unit.ListAsync<ExpenseReversal>(cancellationToken: cancellationToken)).Select(r => r.ExpenseId).ToHashSet();
        var search = (filter.Search ?? string.Empty).Trim();
        var department = (filter.Department ?? string.Empty).Trim();
        return expenses
            .Where(e => filter.CategoryId is null || lines.GetValueOrDefault(e.Id, []).Any(l => l.CategoryId == filter.CategoryId))
            .Where(e => department.Length == 0 || string.Equals(e.Department, department, StringComparison.OrdinalIgnoreCase))
            .Select(e =>
            {
                var own = lines.GetValueOrDefault(e.Id, []);
                var row = new ExpenseRow(e.Id, e.Number ?? "(draft)", e.Date, e.Status, reversed.Contains(e.Id),
                    string.Join(", ", own.Select(l => categories.GetValueOrDefault(l.CategoryId, "?")).Distinct()),
                    e.SupplierId is { } s ? parties.GetValueOrDefault(s, "?") : string.Empty,
                    e.EmployeeUserId is { } u ? users.GetValueOrDefault(u, e.EmployeeName) : e.EmployeeName,
                    e.PaidFromAccountId is { } a ? accounts.GetValueOrDefault(a, "?") : string.Empty, e.Method,
                    e.BranchId is { } b ? branches.GetValueOrDefault(b, "?") : string.Empty, e.Department, e.Taxable, e.Total - e.Taxable, e.Total,
                    users.GetValueOrDefault(e.CreatedBy, "?"));
                return new LoadedRow(row, own);
            })
            .Where(r => search.Length == 0 || r.Row.Number.Contains(search, StringComparison.OrdinalIgnoreCase) || r.Row.Categories.Contains(search, StringComparison.OrdinalIgnoreCase)
                || r.Row.Supplier.Contains(search, StringComparison.OrdinalIgnoreCase) || r.Row.Employee.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Row.Date).ThenByDescending(r => r.Row.Number, StringComparer.Ordinal)
            .ToList();
    }

    private static async Task<ExpenseCategory> ActiveCategoryAsync(IUnitOfWork unit, Guid id, CancellationToken cancellationToken)
    {
        var category = await unit.FindAsync<ExpenseCategory>(id, cancellationToken) ?? throw new WorkspaceValidationException("Choose an expense category.");
        if (!category.IsActive) throw new WorkspaceValidationException($"Category {category.Name} is inactive.");
        return category;
    }

    private static async Task<Account> CashOrBankAsync(IUnitOfWork unit, Guid id, CancellationToken cancellationToken)
    {
        var account = await unit.FindAsync<Account>(id, cancellationToken) ?? throw new WorkspaceValidationException("Choose the cash or bank account it was paid from.");
        if (!account.IsCashOrBank || account.IsGroup || !account.IsActive) throw new WorkspaceValidationException("Choose an active cash or bank account.");
        return account;
    }

    private static async Task<ExpenseSettings> SettingsAsync(IUnitOfWork unit, CancellationToken cancellationToken) =>
        (await unit.ListAsync<ExpenseSettings>(cancellationToken: cancellationToken)).SingleOrDefault()
        ?? throw new WorkspaceValidationException("Expense settings are missing. Sign out and in again to finish the upgrade.");

    private static void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (DomainException ex)
        {
            throw new WorkspaceValidationException(ex.Message);
        }
    }

    private static T Guard<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (DomainException ex)
        {
            throw new WorkspaceValidationException(ex.Message);
        }
    }
}

/// <summary>Which attachments are accepted: PDF, PNG and JPEG recognised by their first bytes, up to 5 MB.</summary>
public static class AttachmentRules
{
    public static (string FileName, string ContentType) Validate(string fileName, byte[] content)
    {
        if (content is null || content.Length == 0) throw new WorkspaceValidationException("The file is empty.");
        if (content.Length > ExpenseAttachment.MaxBytes) throw new WorkspaceValidationException($"Attachments can be at most {ExpenseAttachment.MaxBytes / 1024 / 1024} MB.");
        var type = Detect(content) ?? throw new WorkspaceValidationException("Only PDF, PNG and JPEG files can be attached.");
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/'));
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        name = new string(name.Where(c => !invalid.Contains(c) && !char.IsControl(c)).ToArray()).Trim().TrimStart('.');
        var extension = Path.GetExtension(name).ToLowerInvariant();
        var expected = type switch { "application/pdf" => new[] { ".pdf" }, "image/png" => [".png"], _ => [".jpg", ".jpeg"] };
        if (!expected.Contains(extension)) throw new WorkspaceValidationException($"The file's content is {type}, but its name ends in \"{extension}\". Rename it or choose the right file.");
        if (name.Length > 120) name = name[..(120 - extension.Length)] + extension;
        if (Path.GetFileNameWithoutExtension(name).Length == 0) name = "attachment" + extension;
        return (name, type);
    }

    public static string? Detect(byte[] content)
    {
        if (content.Length >= 5 && content[0] == '%' && content[1] == 'P' && content[2] == 'D' && content[3] == 'F' && content[4] == '-') return "application/pdf";
        if (content.Length >= 8 && content.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "image/png";
        if (content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF) return "image/jpeg";
        return null;
    }
}
