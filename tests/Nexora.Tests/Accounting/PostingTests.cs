using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nexora.Accounting;
using Nexora.Application;
using Nexora.Application.Abstractions;
using Nexora.Application.Audit;
using Nexora.Application.Companies;
using Nexora.Application.Security;
using Nexora.Application.Users;
using Nexora.Domain.Accounting;
using Nexora.Domain.Common;
using Nexora.Infrastructure.Persistence;
using Nexora.Tests.Infrastructure;
using Nexora.Tests.Workspace;

namespace Nexora.Tests.Accounting;

/// <summary>Posting engine against a real SQLite database through the production services.</summary>
public sealed class PostingTests : IAsyncLifetime
{
    private static readonly DateOnly October = new(2026, 10, 10);
    private readonly SqliteTestDatabase _db = new();
    private Dictionary<string, Guid> _accounts = [];

    private VoucherService Vouchers => _db.Get<VoucherService>();

    private LedgerService Ledgers => _db.Get<LedgerService>();

    private Guid A(string key) => _accounts[key];

    public async Task InitializeAsync()
    {
        await _db.Workspace.InitializeAsync();
        await _db.Workspace.CompleteSetupAsync(WorkspaceServiceTests.ValidRequest(demo: true));
        _accounts = await LoadAccountsAsync(_db);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    internal static async Task<Dictionary<string, Guid>> LoadAccountsAsync(SqliteTestDatabase db)
    {
        var rows = await db.Get<ChartOfAccountsService>().ListAsync();
        return rows.Where(r => r.SystemKey is not null).ToDictionary(r => r.SystemKey!, r => r.Id)
            .Concat(rows.Where(r => r.SystemKey is null).Select(r => KeyValuePair.Create(r.Code, r.Id)))
            .ToDictionary(p => p.Key, p => p.Value);
    }

    private VoucherInput Receipt(decimal amount, DateOnly? date = null) =>
        new(null, VoucherType.Receipt, date ?? October, "Cash sale", [new(A(SystemAccounts.Cash), amount, 0), new(A(SystemAccounts.Sales), 0, amount)]);

    private VoucherInput Journal(decimal amount) =>
        new(null, VoucherType.Journal, October, "Accrue rent", [new(A("5305"), amount, 0), new(A(SystemAccounts.Capital), 0, amount)]);

    [Fact]
    public async Task New_company_has_a_chart_of_accounts_and_an_open_first_year()
    {
        Assert.Contains(SystemAccounts.Cash, _accounts.Keys);
        Assert.Contains(SystemAccounts.RetainedEarnings, _accounts.Keys);
        var year = Assert.Single(await Ledgers.ListYearsAsync());
        Assert.Equal(2026, year.StartYear);
        Assert.False(year.IsClosed);
    }

    [Fact]
    public async Task Posting_numbers_the_voucher_writes_the_ledger_and_audits_it()
    {
        var (id, number) = await Vouchers.SaveAndPostAsync(Receipt(1_180.50m));

        Assert.Equal("RV/2026-27/0001", number);
        var details = await Vouchers.GetAsync(id);
        Assert.Equal(VoucherStatus.Posted, details.Status);
        Assert.Equal(1_180.50m, details.Total);

        var trial = await Ledgers.TrialBalanceAsync(October);
        Assert.True(trial.IsBalanced);
        Assert.Equal(1_180.50m, trial.Rows.Single(r => r.AccountId == A(SystemAccounts.Cash)).Debit);

        var audit = await _db.Get<AuditService>().QueryAsync(new AuditQuery());
        Assert.Contains(audit, e => e.Action == "Posted" && e.Summary.Contains(number, StringComparison.Ordinal));
        Assert.True((await _db.Get<AuditService>().VerifyAsync()).IsIntact);
    }

    [Fact]
    public async Task Numbers_continue_per_voucher_type()
    {
        Assert.Equal("RV/2026-27/0001", (await Vouchers.SaveAndPostAsync(Receipt(10m))).Number);
        Assert.Equal("RV/2026-27/0002", (await Vouchers.SaveAndPostAsync(Receipt(20m))).Number);
        Assert.Equal("JV/2026-27/0001", (await Vouchers.SaveAndPostAsync(Journal(30m))).Number);
    }

    [Fact]
    public async Task Unbalanced_draft_can_be_saved_but_not_posted()
    {
        var id = await Vouchers.SaveDraftAsync(new VoucherInput(null, VoucherType.Journal, October, "Wrong",
            [new(A("5305"), 100m, 0), new(A(SystemAccounts.Capital), 0, 90m)]));

        var error = await Assert.ThrowsAsync<DomainException>(() => Vouchers.PostAsync(id));
        Assert.Contains("must balance", error.Message, StringComparison.Ordinal);
        Assert.Empty((await Ledgers.TrialBalanceAsync(October)).Rows);
        Assert.Equal(VoucherStatus.Draft, (await Vouchers.GetAsync(id)).Status);
    }

    [Fact]
    public async Task Draft_can_be_edited_and_deleted()
    {
        var id = await Vouchers.SaveDraftAsync(Journal(100m));
        await Vouchers.SaveDraftAsync(Journal(250m) with { Id = id });
        Assert.Equal(250m, (await Vouchers.GetAsync(id)).Total);
        Assert.Equal(2, (await Vouchers.GetAsync(id)).Lines.Count);

        await Vouchers.DeleteDraftAsync(id);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Vouchers.GetAsync(id));
    }

    [Fact]
    public async Task Posting_twice_is_refused()
    {
        var id = await Vouchers.SaveDraftAsync(Receipt(100m));
        await Vouchers.PostAsync(id);

        var error = await Assert.ThrowsAsync<WorkspaceValidationException>(() => Vouchers.PostAsync(id));
        Assert.Contains("already posted", error.Message, StringComparison.Ordinal);
        Assert.Equal(100m, (await Ledgers.TrialBalanceAsync(October)).TotalDebit);
    }

    [Fact]
    public async Task Concurrent_posting_of_the_same_draft_posts_it_once()
    {
        var id = await Vouchers.SaveDraftAsync(Receipt(100m));

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Capture(() => Vouchers.PostAsync(id))));

        Assert.Single(results, r => r.Error is null);
        Assert.All(results.Where(r => r.Error is not null), r => Assert.IsType<WorkspaceValidationException>(r.Error));
        Assert.Equal(100m, (await Ledgers.TrialBalanceAsync(October)).TotalDebit);
    }

    [Fact]
    public async Task Concurrent_posting_of_different_vouchers_gives_unique_gapless_numbers()
    {
        var ids = new List<Guid>();
        for (var i = 1; i <= 20; i++) ids.Add(await Vouchers.SaveDraftAsync(Receipt(i)));

        var numbers = await Task.WhenAll(ids.Select(id => Task.Run(() => Vouchers.PostAsync(id))));

        Assert.Equal(Enumerable.Range(1, 20).Select(n => $"RV/2026-27/{n:0000}"), numbers.Order());
        Assert.Equal(210m, (await Ledgers.TrialBalanceAsync(October)).TotalDebit);
    }

    [Fact]
    public async Task Posted_voucher_cannot_be_changed_or_deleted_through_the_service()
    {
        var (id, _) = await Vouchers.SaveAndPostAsync(Receipt(100m));

        await Assert.ThrowsAnyAsync<Exception>(() => Vouchers.SaveDraftAsync(Receipt(999m) with { Id = id }));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Vouchers.DeleteDraftAsync(id));
        Assert.Equal(100m, (await Vouchers.GetAsync(id)).Total);
    }

    [Fact]
    public async Task Posted_records_cannot_be_changed_through_the_data_context()
    {
        var (id, _) = await Vouchers.SaveAndPostAsync(Receipt(100m));
        var factory = _db.Get<IDbContextFactory<NexoraDbContext>>();

        await using (var context = await factory.CreateDbContextAsync())
        {
            context.Remove(await context.LedgerEntries.FirstAsync());
            await Assert.ThrowsAsync<ImmutableRecordException>(() => context.SaveChangesAsync());
        }

        await using (var context = await factory.CreateDbContextAsync())
        {
            var voucher = await context.Vouchers.SingleAsync(v => v.Id == id);
            context.Entry(voucher).Property(v => v.Narration).CurrentValue = "edited";
            await Assert.ThrowsAsync<ImmutableRecordException>(() => context.SaveChangesAsync());
        }

        await using (var context = await factory.CreateDbContextAsync())
        {
            var line = await context.VoucherLines.FirstAsync(l => l.VoucherId == id);
            context.Remove(line);
            await Assert.ThrowsAsync<ImmutableRecordException>(() => context.SaveChangesAsync());
        }
    }

    [Theory]
    [InlineData("UPDATE LedgerEntries SET DebitPaise = DebitPaise + 100")]
    [InlineData("DELETE FROM LedgerEntries")]
    [InlineData("UPDATE Vouchers SET Narration = 'edited' WHERE Status = 'Posted'")]
    [InlineData("DELETE FROM Vouchers WHERE Status = 'Posted'")]
    [InlineData("UPDATE VoucherLines SET CreditPaise = 1 WHERE IsLocked = 1")]
    [InlineData("DELETE FROM VoucherLines WHERE IsLocked = 1")]
    public async Task Database_triggers_block_direct_changes_to_posted_records(string sql)
    {
        await Vouchers.SaveAndPostAsync(Receipt(100m));

        using var connection = _db.OpenRawConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var error = Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
        Assert.Contains("cannot be", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reversal_cancels_the_effect_and_leaves_the_original_untouched()
    {
        var (id, number) = await Vouchers.SaveAndPostAsync(Receipt(500m));

        var reversalNumber = await Vouchers.ReverseAsync(id, October.AddDays(2), "Customer returned the goods");

        Assert.Equal("RJ/2026-27/0001", reversalNumber);
        var original = await Vouchers.GetAsync(id);
        Assert.Equal(VoucherStatus.Posted, original.Status);
        Assert.Equal(500m, original.Total);
        Assert.Equal(reversalNumber, original.ReversedByNumber);
        Assert.Empty((await Ledgers.TrialBalanceAsync(October.AddDays(2))).Rows);
        Assert.Equal(500m, (await Ledgers.TrialBalanceAsync(October)).TotalDebit);

        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Vouchers.ReverseAsync(id, October.AddDays(3), ""));
        Assert.Contains(await _db.Get<AuditService>().QueryAsync(new AuditQuery()), e => e.Action == "Reversed" && e.Summary.Contains(number, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Posting_rules_for_accounts_and_dates_are_enforced()
    {
        var group = (await _db.Get<ChartOfAccountsService>().ListAsync()).Single(r => r.Code == "5300").Id;
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Vouchers.SaveAndPostAsync(
            new VoucherInput(null, VoucherType.Journal, October, "", [new(group, 10m, 0), new(A(SystemAccounts.Capital), 0, 10m)])));

        await _db.Get<ChartOfAccountsService>().SetActiveAsync(A("5305"), false);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Vouchers.SaveAndPostAsync(Journal(10m)));
        await _db.Get<ChartOfAccountsService>().SetActiveAsync(A("5305"), true);

        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Vouchers.SaveAndPostAsync(Receipt(10m, new DateOnly(2026, 3, 31))));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Vouchers.SaveAndPostAsync(Receipt(10m, new DateOnly(2027, 4, 1))));
        await Assert.ThrowsAsync<DomainException>(() => Vouchers.SaveAndPostAsync(
            new VoucherInput(null, VoucherType.Payment, October, "", [new(A("5305"), 10m, 0), new(A(SystemAccounts.Capital), 0, 10m)])));
    }

    [Fact]
    public async Task Closed_year_rejects_postings_and_reversals_until_reopened()
    {
        var (id, _) = await Vouchers.SaveAndPostAsync(Receipt(100m));
        var draft = await Vouchers.SaveDraftAsync(Receipt(50m));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Ledgers.CloseYearAsync(2026));
        await Vouchers.DeleteDraftAsync(draft);

        await Ledgers.CloseYearAsync(2026);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Vouchers.SaveAndPostAsync(Receipt(10m)));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Vouchers.ReverseAsync(id, October, ""));

        await Ledgers.ReopenYearAsync(2026);
        await Vouchers.SaveAndPostAsync(Receipt(10m));
    }

    [Fact]
    public async Task Opening_balances_post_with_any_difference_to_the_suspense_account()
    {
        var number = await Vouchers.PostOpeningBalancesAsync(2026,
        [
            new(A(SystemAccounts.Cash), 25_000m, 0),
            new(A("1121"), 75_000m, 0),
            new(A(SystemAccounts.Capital), 0, 90_000m),
        ]);

        Assert.Equal("OB/2026-27/001", number);
        var trial = await Ledgers.TrialBalanceAsync(new DateOnly(2026, 4, 1));
        Assert.True(trial.IsBalanced);
        Assert.Equal(10_000m, trial.Rows.Single(r => r.AccountId == A(SystemAccounts.OpeningBalanceDifference)).Credit);

        await Assert.ThrowsAsync<WorkspaceValidationException>(() =>
            Vouchers.PostOpeningBalancesAsync(2026, [new(A(SystemAccounts.Sales), 0, 100m), new(A(SystemAccounts.Cash), 100m, 0)]));
    }

    [Fact]
    public async Task Next_year_carries_balances_forward_without_closing_entries()
    {
        await Vouchers.PostOpeningBalancesAsync(2026, [new(A(SystemAccounts.Cash), 100_000m, 0), new(A(SystemAccounts.Capital), 0, 100_000m)]);
        await Vouchers.SaveAndPostAsync(Receipt(40_000m));
        await Vouchers.SaveAndPostAsync(new VoucherInput(null, VoucherType.Payment, October, "Rent",
            [new(A("5305"), 15_000m, 0), new(A(SystemAccounts.Cash), 0, 15_000m)]));
        Assert.Equal(2027, await Ledgers.OpenNextYearAsync());
        await Vouchers.SaveAndPostAsync(Receipt(1_000m, new DateOnly(2027, 4, 5)));

        var april = new DateOnly(2027, 4, 30);
        var sheet = await Ledgers.BalanceSheetAsync(april);
        var profit = await Ledgers.ProfitAndLossAsync(new DateOnly(2027, 4, 1), april);

        Assert.True(sheet.IsBalanced);
        Assert.Equal(126_000m, sheet.Assets.Single(l => l.AccountId == A(SystemAccounts.Cash)).Amount);
        Assert.Equal(25_000m, sheet.Equity.Single(l => l.AccountId == A(SystemAccounts.RetainedEarnings)).Amount);
        Assert.Equal(1_000m, profit.NetProfit);
        Assert.Equal(1_000m, sheet.CurrentYearProfit);

        var retained = await Ledgers.StatementAsync(A(SystemAccounts.RetainedEarnings), new DateOnly(2027, 4, 1), april);
        Assert.Equal(-25_000m, retained.OpeningBalance);
    }

    /// <summary>
    /// Reconciliation: the trial balance, every ledger statement, the profit and loss and the balance sheet
    /// are all derived from the same postings and must agree with each other.
    /// </summary>
    [Fact]
    public async Task Reports_reconcile_to_the_same_postings()
    {
        await Vouchers.PostOpeningBalancesAsync(2026, [new(A(SystemAccounts.Cash), 50_000m, 0), new(A("1121"), 150_000m, 0), new(A(SystemAccounts.Capital), 0, 200_000m)]);
        await Vouchers.SaveAndPostAsync(Receipt(11_800m));
        await Vouchers.SaveAndPostAsync(new VoucherInput(null, VoucherType.Payment, October, "Rent and charges",
            [new(A("5305"), 12_000m, 0), new(A(SystemAccounts.BankCharges), 118m, 0), new(A("1121"), 0, 12_118m)]));
        await Vouchers.SaveAndPostAsync(new VoucherInput(null, VoucherType.Contra, October, "Cash deposited",
            [new(A("1121"), 20_000m, 0), new(A(SystemAccounts.Cash), 0, 20_000m)]));
        await Vouchers.SaveAndPostAsync(new VoucherInput(null, VoucherType.Journal, October, "Sale on credit with GST",
            [new(A(SystemAccounts.Capital), 1_180m, 0), new(A(SystemAccounts.Sales), 0, 1_000m), new(A(SystemAccounts.OutputCgst), 0, 90m), new(A(SystemAccounts.OutputSgst), 0, 90m)]));
        var (toReverse, _) = await Vouchers.SaveAndPostAsync(Receipt(3_000m));
        await Vouchers.ReverseAsync(toReverse, October, "");

        var asOf = new DateOnly(2027, 3, 31);
        var trial = await Ledgers.TrialBalanceAsync(asOf);
        var sheet = await Ledgers.BalanceSheetAsync(asOf);
        var profit = await Ledgers.ProfitAndLossAsync(new DateOnly(2026, 4, 1), asOf);

        Assert.True(trial.IsBalanced);
        Assert.True(sheet.IsBalanced);
        Assert.Equal(profit.NetProfit, sheet.CurrentYearProfit);
        Assert.Equal(11_800m + 1_000m - 12_000m - 118m, profit.NetProfit);

        foreach (var row in trial.Rows)
        {
            var statement = await Ledgers.StatementAsync(row.AccountId, new DateOnly(2026, 4, 1), asOf);
            Assert.Equal(row.Net, statement.ClosingBalance);
        }

        var incomeInTrial = trial.Rows.Where(r => r.Nature == AccountNature.Income).Sum(r => r.Credit - r.Debit);
        var expenseInTrial = trial.Rows.Where(r => r.Nature == AccountNature.Expense).Sum(r => r.Debit - r.Credit);
        Assert.Equal(profit.TotalIncome, incomeInTrial);
        Assert.Equal(profit.TotalExpenses, expenseInTrial);
    }

    [Fact]
    public async Task Demonstration_company_with_posted_vouchers_can_still_be_deleted()
    {
        var demo = (await _db.Workspace.GetCompaniesAsync()).Single(c => c.IsDemo);
        var real = _db.Workspace.CurrentSession!.CompanyId;
        await _db.Workspace.SwitchCompanyAsync(demo.Id);
        var demoAccounts = await LoadAccountsAsync(_db);
        await Vouchers.SaveAndPostAsync(new VoucherInput(null, VoucherType.Receipt, October, "Demo",
            [new(demoAccounts[SystemAccounts.Cash], 100m, 0), new(demoAccounts[SystemAccounts.Sales], 0, 100m)]));
        await _db.Workspace.SwitchCompanyAsync(real);

        await _db.Workspace.DeleteDemonstrationCompanyAsync(demo.Id);

        Assert.DoesNotContain(await _db.Workspace.GetCompaniesAsync(), c => c.IsDemo);
    }

    private static async Task<(string? Result, Exception? Error)> Capture(Func<Task<string>> action)
    {
        try
        {
            return (await Task.Run(action), null);
        }
        catch (Exception exception)
        {
            return (null, exception);
        }
    }
}

public sealed class AccountingAccessTests : IAsyncLifetime
{
    private static readonly DateOnly October = new(2026, 10, 10);
    private readonly SqliteTestDatabase _db = new();
    private Dictionary<string, Guid> _accounts = [];
    private Guid _companyA;

    public async Task InitializeAsync()
    {
        await _db.Workspace.InitializeAsync();
        _companyA = (await _db.Workspace.CompleteSetupAsync(WorkspaceServiceTests.ValidRequest())).CompanyId;
        _accounts = await PostingTests.LoadAccountsAsync(_db);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private VoucherInput Receipt(Dictionary<string, Guid> accounts) =>
        new(null, VoucherType.Receipt, October, "Sale", [new(accounts[SystemAccounts.Cash], 100m, 0), new(accounts[SystemAccounts.Sales], 0, 100m)]);

    private async Task SignInAsAsync(string username, string roleName)
    {
        var role = (await _db.Get<RoleService>().ListAsync()).Single(r => r.Name == roleName);
        await _db.Get<UserService>().CreateAsync(new NewUserRequest(username, username, "temporary pass 1", role.Id));
        _db.Workspace.SignOut();
        await _db.Workspace.SignInAsync(username, "temporary pass 1");
        await _db.Workspace.ChangePasswordAsync("temporary pass 1", "my own phrase 9");
    }

    [Fact]
    public async Task Read_only_users_can_view_ledgers_but_not_create_or_post()
    {
        await SignInAsAsync("ravi", "Read-only");

        await _db.Get<LedgerService>().TrialBalanceAsync(October);
        await _db.Get<VoucherService>().ListAsync(new VoucherQuery());
        await Assert.ThrowsAsync<AccessDeniedException>(() => _db.Get<VoucherService>().SaveDraftAsync(Receipt(_accounts)));
        await Assert.ThrowsAsync<AccessDeniedException>(() => _db.Get<ChartOfAccountsService>().SetActiveAsync(_accounts["5305"], false));
        await Assert.ThrowsAsync<AccessDeniedException>(() => _db.Get<LedgerService>().CloseYearAsync(2026));
    }

    [Fact]
    public async Task Billing_operators_cannot_see_vouchers_or_ledgers()
    {
        await SignInAsAsync("bina", "Billing Operator");

        await Assert.ThrowsAsync<AccessDeniedException>(() => _db.Get<VoucherService>().ListAsync(new VoucherQuery()));
        await Assert.ThrowsAsync<AccessDeniedException>(() => _db.Get<LedgerService>().TrialBalanceAsync(October));
    }

    [Fact]
    public async Task Accountants_can_post_and_reverse()
    {
        await SignInAsAsync("anu", "Accountant");

        var (id, _) = await _db.Get<VoucherService>().SaveAndPostAsync(Receipt(_accounts));
        await _db.Get<VoucherService>().ReverseAsync(id, October, "");
    }

    [Fact]
    public async Task Another_companys_vouchers_and_accounts_are_out_of_reach()
    {
        var (aVoucher, _) = await _db.Get<VoucherService>().SaveAndPostAsync(Receipt(_accounts));
        var b = await _db.Get<CompanyService>().CreateCompanyAsync(new NewCompanyRequest("Beta Stores", "29", 2026));
        await _db.Workspace.SwitchCompanyAsync(b.Id);

        Assert.Empty(await _db.Get<VoucherService>().ListAsync(new VoucherQuery()));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => _db.Get<VoucherService>().GetAsync(aVoucher));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => _db.Get<VoucherService>().ReverseAsync(aVoucher, October, ""));
        Assert.Empty((await _db.Get<LedgerService>().TrialBalanceAsync(October)).Rows);

        // Using company A's accounts while signed in to B: the accounts are not found, so nothing is posted.
        await Assert.ThrowsAnyAsync<Exception>(() => _db.Get<VoucherService>().SaveAndPostAsync(Receipt(_accounts)));
        Assert.Empty((await _db.Get<LedgerService>().TrialBalanceAsync(October)).Rows);
    }

    [Fact]
    public async Task Database_rejects_a_voucher_line_pointing_to_another_companys_account()
    {
        var draft = await _db.Get<VoucherService>().SaveDraftAsync(Receipt(_accounts));
        var b = await _db.Get<CompanyService>().CreateCompanyAsync(new NewCompanyRequest("Beta Stores", "29", 2026));
        await _db.Workspace.SwitchCompanyAsync(b.Id);
        var bCash = (await PostingTests.LoadAccountsAsync(_db))[SystemAccounts.Cash];

        using var connection = _db.OpenRawConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE VoucherLines SET AccountId = $account WHERE VoucherId = $voucher";
        command.Parameters.AddWithValue("$account", bCash.ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("$voucher", draft.ToString().ToUpperInvariant());

        Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
        Assert.NotEqual(_companyA, b.Id);
    }
}
