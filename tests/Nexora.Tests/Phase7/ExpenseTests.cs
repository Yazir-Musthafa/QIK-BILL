using Microsoft.Data.Sqlite;
using Nexora.Accounting;
using Nexora.Application;
using Nexora.Application.Companies;
using Nexora.Application.Expenses;
using Nexora.Application.Security;
using Nexora.Domain.Billing;
using Nexora.Domain.Expenses;
using Nexora.Domain.Numbering;
using Nexora.Tests.Billing;

namespace Nexora.Tests.Phase7;

/// <summary>Expenses on top of the billing fixture: Rent posts to the seeded Rent ledger (5305), GST 18% from the seeded tax category.</summary>
public abstract class ExpenseTestBase : BillingTestBase
{
    protected ExpenseService Expenses => Db.Get<ExpenseService>();

    protected Guid Rent { get; private set; }

    protected Guid Office { get; private set; }

    protected Guid RentLedger { get; private set; }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        var chart = await Db.Get<ChartOfAccountsService>().ListAsync();
        RentLedger = chart.Single(a => a.Name == "Rent").Id;
        Rent = await Expenses.SaveCategoryAsync(new ExpenseCategoryInput(null, "Rent", RentLedger, DefaultTaxCategoryId: Gst18));
        Office = await Expenses.SaveCategoryAsync(new ExpenseCategoryInput(null, "Office supplies", chart.Single(a => a.Name == "Office Expenses").Id));
    }

    protected Task<Guid> DraftAsync(decimal amount = 10_000, Guid? category = null, bool eligible = true, Guid? supplier = null, string department = "",
        PaymentMethod method = PaymentMethod.BankTransfer, DateOnly? date = null) =>
        Expenses.SaveDraftAsync(new ExpenseInput(null, date ?? October, CashAccount, [new ExpenseLineInput(category ?? Rent, amount, "October rent", InputTaxEligible: eligible)],
            method, supplier, Department: department));

    /// <summary>Creates and submits as the administrator, approves as an accountant, then signs the administrator back in.</summary>
    protected async Task<Guid> ApprovedAsync(decimal amount = 10_000)
    {
        var id = await DraftAsync(amount);
        await Expenses.SubmitAsync(id);
        await SignInAsAsync("anu" + Guid.NewGuid().ToString("N")[..6], "Accountant");
        await Expenses.ApproveAsync(id);
        await SignInAsAdministratorAsync();
        return id;
    }

    protected async Task<decimal> NetAsync(Guid accountId) =>
        (await Db.Get<LedgerService>().TrialBalanceAsync(October)).Rows.Where(r => r.AccountId == accountId).Sum(r => r.Net);
}

public sealed class ExpensePostingTests : ExpenseTestBase
{
    [Fact]
    public async Task Approved_expense_posts_ledger_input_gst_and_cash_and_the_books_balance()
    {
        var id = await ApprovedAsync();

        var number = await Expenses.PostAsync(id);

        Assert.Equal("EXP/2026-27/0001", number);
        var view = await Expenses.GetAsync(id);
        Assert.Equal((ExpenseStatus.Posted, 10_000m, 900m, 900m, 11_800m), (view.Status, view.Taxable, view.Cgst, view.Sgst, view.Total));
        Assert.Equal(10_000m, await NetAsync(RentLedger));
        Assert.Equal(900m, await LedgerAsync(SystemAccounts.InputCgst, October));
        Assert.Equal(-11_800m, await LedgerAsync(SystemAccounts.Cash, October));
        await AssertBooksBalanceAsync();
    }

    [Fact]
    public async Task Gst_that_cannot_be_claimed_is_added_to_the_expense()
    {
        var id = await DraftAsync(eligible: false);
        await Expenses.SubmitAsync(id);
        await SignInAsAsync("anu", "Accountant");
        await Expenses.ApproveAsync(id);
        await Expenses.PostAsync(id);

        Assert.Equal(11_800m, await NetAsync(RentLedger));
        Assert.Equal(0m, await LedgerAsync(SystemAccounts.InputCgst, October));
        Assert.Equal(1_800m, (await Expenses.GetAsync(id)).IneligibleTax);
    }

    [Fact]
    public async Task Inter_state_supplier_expenses_carry_igst()
    {
        var karnataka = await Parties.SaveAsync(new Application.Billing.PartyInput(null, PartyKind.Supplier, new PartyDetails("Mysuru Rentals", Gstin: Gstin("29", "AABCM1234C"))));

        var id = await DraftAsync(supplier: karnataka);

        var view = await Expenses.GetAsync(id);
        Assert.Equal((true, 0m, 1_800m), (view.IsInterState, view.Cgst, view.Igst));
    }

    [Fact]
    public async Task Posting_needs_approval_happens_once_and_a_posted_expense_is_fixed()
    {
        var draft = await DraftAsync();
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Expenses.PostAsync(draft));

        var id = await ApprovedAsync();
        await Expenses.PostAsync(id);

        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Expenses.PostAsync(id));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Expenses.SaveDraftAsync(new ExpenseInput(id, October, CashAccount, [new ExpenseLineInput(Rent, 1)])));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Expenses.DeleteDraftAsync(id));
        Assert.Equal(-11_800m, await LedgerAsync(SystemAccounts.Cash, October));
    }

    [Fact]
    public async Task Reversal_restores_the_ledger_and_happens_once()
    {
        var id = await ApprovedAsync();
        await Expenses.PostAsync(id);

        await Expenses.ReverseAsync(id, October, "Wrong month");

        Assert.Equal(0m, await NetAsync(RentLedger));
        Assert.Equal(0m, await LedgerAsync(SystemAccounts.Cash, October));
        Assert.True((await Expenses.GetAsync(id)).IsReversed);
        Assert.Equal("Reversed", Assert.Single(await Expenses.ListAsync(new ExpenseFilter())).StatusLabel);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Expenses.ReverseAsync(id, October, "again"));
        await AssertBooksBalanceAsync();
    }

    [Fact]
    public async Task Categories_post_to_expense_ledgers_only_and_can_create_one()
    {
        var chart = await Db.Get<ChartOfAccountsService>().ListAsync();
        var indirect = chart.Single(a => a.Name == "Indirect Expenses").Id;

        var travel = await Expenses.SaveCategoryAsync(new ExpenseCategoryInput(null, "Travel", null, NewLedgerGroupId: indirect));

        var ledger = (await Db.Get<ChartOfAccountsService>().ListAsync()).Single(a => a.Name == "Travel");
        Assert.Equal(indirect, ledger.ParentId);
        Assert.Equal(ledger.Id, (await Expenses.CategoriesAsync()).Single(c => c.Id == travel).AccountId);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Expenses.SaveCategoryAsync(new ExpenseCategoryInput(null, "Cash?", CashAccount)));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Expenses.SaveCategoryAsync(new ExpenseCategoryInput(null, "Group?", indirect)));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Expenses.SaveCategoryAsync(new ExpenseCategoryInput(null, "Rent", RentLedger)));
    }

    [Fact]
    public async Task Expenses_must_be_paid_from_cash_or_bank()
    {
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Expenses.SaveDraftAsync(new ExpenseInput(null, October, RentLedger, [new ExpenseLineInput(Rent, 100)])));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Expenses.SaveDraftAsync(new ExpenseInput(null, October, CashAccount, [])));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Expenses.SaveDraftAsync(new ExpenseInput(null, October, CashAccount, [new ExpenseLineInput(Rent, 0)])));
    }

    [Fact]
    public async Task Totals_group_by_category_branch_department_method_and_month()
    {
        await DraftAsync(10_000, department: "Admin", method: PaymentMethod.BankTransfer);
        await DraftAsync(500, Office, department: "Sales", method: PaymentMethod.Cash);
        await DraftAsync(250, Office, department: "Sales", method: PaymentMethod.Cash, date: October.AddDays(-20));

        var byCategory = await Expenses.TotalsAsync(new ExpenseFilter(), ExpenseGrouping.Category);
        var byDepartment = await Expenses.TotalsAsync(new ExpenseFilter(), ExpenseGrouping.Department);
        var byMethod = await Expenses.TotalsAsync(new ExpenseFilter(), ExpenseGrouping.Method);
        var byMonth = await Expenses.TotalsAsync(new ExpenseFilter(), ExpenseGrouping.Month);

        Assert.Equal([("Rent", 11_800m), ("Office supplies", 750m)], byCategory.Select(t => (t.Group, t.Total)));
        Assert.Equal(750m, byDepartment.Single(t => t.Group == "Sales").Total);
        Assert.Equal((2, 750m), (byMethod.Single(t => t.Group == "Cash").Count, byMethod.Single(t => t.Group == "Cash").Total));
        Assert.Equal(["2026-09", "2026-10"], byMonth.Select(t => t.Group));
        Assert.Equal(2, (await Expenses.ListAsync(new ExpenseFilter(CategoryId: Office))).Count);
        Assert.Single(await Expenses.ListAsync(new ExpenseFilter(From: October.AddDays(-30), To: October.AddDays(-10))));
        Assert.Single(await Expenses.ListAsync(new ExpenseFilter(Department: "admin")));
    }
}

public sealed class ExpenseApprovalTests : ExpenseTestBase
{
    [Fact]
    public async Task You_cannot_approve_your_own_expense_unless_the_company_allows_it()
    {
        var id = await DraftAsync();
        await Expenses.SubmitAsync(id);

        var error = await Assert.ThrowsAsync<WorkspaceValidationException>(() => Expenses.ApproveAsync(id));
        Assert.Contains("you created or submitted", error.Message, StringComparison.Ordinal);
        Assert.False((await Expenses.GetAsync(id)).CanApprove);

        await Expenses.SetAllowSelfApprovalAsync(true);
        Assert.True((await Expenses.GetAsync(id)).CanApprove);
        await Expenses.ApproveAsync(id);
        Assert.Equal(ExpenseStatus.Approved, (await Expenses.GetAsync(id)).Status);
    }

    [Fact]
    public async Task Rejection_needs_a_reason_and_sends_it_back_for_editing()
    {
        var id = await DraftAsync();
        await Expenses.SubmitAsync(id);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Expenses.SaveDraftAsync(new ExpenseInput(id, October, CashAccount, [new ExpenseLineInput(Rent, 1)])));
        await SignInAsAsync("anu", "Accountant");

        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Expenses.RejectAsync(id, " "));
        await Expenses.RejectAsync(id, "Attach the rent receipt");

        var view = await Expenses.GetAsync(id);
        Assert.Equal((ExpenseStatus.Rejected, "Attach the rent receipt"), (view.Status, view.RejectionReason));
        await Expenses.SaveDraftAsync(new ExpenseInput(id, October, CashAccount, [new ExpenseLineInput(Rent, 9_000)]));
        Assert.Equal(ExpenseStatus.Draft, (await Expenses.GetAsync(id)).Status);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Expenses.ApproveAsync(id));     // not submitted
    }

    [Fact]
    public async Task Approving_needs_the_approve_permission_and_settings_need_company_manage()
    {
        var id = await DraftAsync();
        await Expenses.SubmitAsync(id);
        await SignInAsAsync("ivan", "Inventory Manager");
        await Assert.ThrowsAsync<AccessDeniedException>(() => Expenses.ApproveAsync(id));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Expenses.ListAsync(new ExpenseFilter()));

        await SignInAsAdministratorAsync();
        await SignInAsAsync("anu", "Accountant");
        await Assert.ThrowsAsync<AccessDeniedException>(() => Expenses.SetAllowSelfApprovalAsync(true));
        await SignInAsAdministratorAsync();
        await SignInAsAsync("ravi", "Read-only");
        Assert.Single(await Expenses.ListAsync(new ExpenseFilter()));
        await Assert.ThrowsAsync<AccessDeniedException>(() => DraftAsync());
    }
}

public sealed class RecurringExpenseTests : ExpenseTestBase
{
    [Fact]
    public async Task Generating_creates_one_draft_per_due_date_and_never_posts()
    {
        var id = await Expenses.SaveRecurringAsync(new RecurringExpenseInput(null, "Shop rent", Rent, 10_000, RecurrenceFrequency.Monthly, new DateOnly(2026, 8, 1), CashAccount));

        var created = await Expenses.GenerateDueAsync(October);
        var again = await Expenses.GenerateDueAsync(October);

        Assert.Equal(3, created.Count);
        Assert.Empty(again);
        var rows = await Expenses.ListAsync(new ExpenseFilter());
        Assert.Equal([new DateOnly(2026, 10, 1), new DateOnly(2026, 9, 1), new DateOnly(2026, 8, 1)], rows.Select(r => r.Date));
        Assert.All(rows, r => Assert.Equal((ExpenseStatus.Draft, 11_800m), (r.Status, r.Total)));
        Assert.Equal(new DateOnly(2026, 11, 1), (await Expenses.RecurringAsync()).Single(r => r.Id == id).NextDueDate);
        Assert.Equal(0m, await LedgerAsync(SystemAccounts.Cash, October));
    }

    [Fact]
    public async Task End_dates_and_inactive_templates_stop_generation()
    {
        await Expenses.SaveRecurringAsync(new RecurringExpenseInput(null, "Weekly cleaning", Office, 500, RecurrenceFrequency.Weekly, new DateOnly(2026, 9, 21), CashAccount,
            EndDate: new DateOnly(2026, 10, 5)));
        var paused = await Expenses.SaveRecurringAsync(new RecurringExpenseInput(null, "Paused", Office, 100, RecurrenceFrequency.Quarterly, new DateOnly(2026, 4, 1), CashAccount));
        await Expenses.SetRecurringActiveAsync(paused, false);

        var created = await Expenses.GenerateDueAsync(October);

        Assert.Equal(3, created.Count);                     // 21 Sep, 28 Sep, 5 Oct
        Assert.False((await Expenses.RecurringAsync()).Single(r => r.Name == "Weekly cleaning").IsActive);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Expenses.SaveRecurringAsync(new RecurringExpenseInput(null, "Bad", Office, 100,
            RecurrenceFrequency.Monthly, October, CashAccount, EndDate: October.AddDays(-1))));
    }
}

public sealed class ExpenseAttachmentTests : ExpenseTestBase
{
    private static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj\n<<>>\nendobj\n%%EOF"u8.ToArray();
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];

    [Fact]
    public async Task Receipts_are_validated_by_content_size_and_count()
    {
        var id = await DraftAsync();

        var file = await Expenses.AddAttachmentAsync(id, @"C:\Users\asha\Downloads\..\rent receipt.pdf", Pdf);
        await Expenses.AddAttachmentAsync(id, "photo.PNG", Png);

        var (name, type, content) = await Expenses.AttachmentAsync(file);
        Assert.Equal(("rent receipt.pdf", "application/pdf"), (name, type));
        Assert.Equal(Pdf, content);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Expenses.AddAttachmentAsync(id, "receipt.png", Pdf));         // name does not match content
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Expenses.AddAttachmentAsync(id, "virus.exe", "MZ\x90\x00"u8.ToArray()));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Expenses.AddAttachmentAsync(id, "invoice.pdf", "MZ\x90\x00 renamed program"u8.ToArray()));  // disguised file
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Expenses.AddAttachmentAsync(id, "empty.pdf", []));
        var big = new byte[ExpenseAttachment.MaxBytes + 1];
        Pdf.CopyTo(big, 0);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Expenses.AddAttachmentAsync(id, "big.pdf", big));
        for (var i = 0; i < 3; i++) await Expenses.AddAttachmentAsync(id, $"r{i}.pdf", Pdf);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Expenses.AddAttachmentAsync(id, "sixth.pdf", Pdf));
        Assert.Equal(5, (await Expenses.GetAsync(id)).Attachments.Count);
    }

    [Fact]
    public async Task Attachments_of_posted_expenses_cannot_be_added_or_removed()
    {
        var id = await DraftAsync();
        var file = await Expenses.AddAttachmentAsync(id, "rent.pdf", Pdf);
        await Expenses.SubmitAsync(id);
        await SignInAsAsync("anu", "Accountant");
        await Expenses.ApproveAsync(id);
        await Expenses.PostAsync(id);

        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Expenses.AddAttachmentAsync(id, "late.pdf", Pdf));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Expenses.RemoveAttachmentAsync(file));
        using var connection = Db.OpenRawConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ExpenseAttachments";
        Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
    }

    [Fact]
    public async Task Attachment_rules_detect_types_from_the_first_bytes()
    {
        Assert.Equal("application/pdf", AttachmentRules.Detect(Pdf));
        Assert.Equal("image/png", AttachmentRules.Detect(Png));
        Assert.Equal("image/jpeg", AttachmentRules.Detect([0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.Null(AttachmentRules.Detect("GIF89a"u8.ToArray()));
        Assert.Equal(("attachment.pdf", "application/pdf"), AttachmentRules.Validate(".pdf", Pdf));
        await Task.CompletedTask;
    }
}

public sealed class ExpenseIsolationTests : ExpenseTestBase
{
    [Fact]
    public async Task Another_companys_expenses_are_out_of_reach_and_posted_rows_are_protected()
    {
        var id = await ApprovedAsync();
        await Expenses.PostAsync(id);
        using (var connection = Db.OpenRawConnection())
        {
            foreach (var sql in new[] { "UPDATE Expenses SET TotalPaise = 1", "UPDATE ExpenseLines SET AmountPaise = 1", "DELETE FROM ExpenseReversals" })
            {
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                if (sql.StartsWith("DELETE", StringComparison.Ordinal)) command.ExecuteNonQuery();      // nothing to delete yet
                else Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
            }
        }

        var demo = (await Db.Workspace.GetCompaniesAsync()).Single(c => c.IsDemo);
        await Db.Workspace.SwitchCompanyAsync(demo.Id);
        Assert.Empty(await Expenses.ListAsync(new ExpenseFilter()));
        Assert.Empty(await Expenses.CategoriesAsync());
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Expenses.GetAsync(id));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Expenses.ReverseAsync(id, October, "x"));
    }

    [Fact]
    public async Task New_companies_get_expense_and_service_numbering_and_settings()
    {
        var numbering = await Db.Get<CompanyService>().ListNumberingAsync();

        Assert.Contains(numbering, r => r.DocumentType == ExpenseDocumentTypes.Expense && r.Example == "EXP/2026-27/0001");
        Assert.Contains(numbering, r => r.DocumentType == Nexora.Domain.Service.ServiceDocumentTypes.Job && r.Example == "JOB/2026-27/0001");
        Assert.False(await Expenses.AllowSelfApprovalAsync());
        Assert.NotNull(DocumentTypes.Find(ExpenseDocumentTypes.Expense));
    }
}
