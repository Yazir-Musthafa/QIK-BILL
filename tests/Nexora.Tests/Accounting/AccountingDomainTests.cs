using Nexora.Accounting;
using Nexora.Domain.Accounting;
using Nexora.Domain.Common;

namespace Nexora.Tests.Accounting;

public sealed class MoneyTests
{
    [Theory]
    [InlineData("0.005", "0.01")]
    [InlineData("-0.005", "-0.01")]
    [InlineData("2.345", "2.35")]
    [InlineData("2.344", "2.34")]
    [InlineData("10.125", "10.13")]
    [InlineData("10.115", "10.12")]
    public void Rounds_half_away_from_zero_to_paise(string input, string expected) =>
        Assert.Equal(decimal.Parse(expected), Money.Round(decimal.Parse(input)));

    [Theory]
    [InlineData("-1")]
    [InlineData("1.001")]
    [InlineData("100000000000")]
    public void Rejects_invalid_amounts(string amount) =>
        Assert.Throws<DomainException>(() => Money.EnsureValid(decimal.Parse(amount)));

    [Theory]
    [InlineData("0")]
    [InlineData("0.01")]
    [InlineData("99999999999.99")]
    public void Converts_to_paise_and_back_exactly(string amount)
    {
        var value = decimal.Parse(amount);
        Assert.Equal(value, Money.FromPaise(Money.ToPaise(value)));
    }
}

public sealed class VoucherDomainTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid Cash = Guid.NewGuid();
    private static readonly Guid Sales = Guid.NewGuid();
    private static readonly Guid Rent = Guid.NewGuid();
    private static readonly DateOnly Date = new(2026, 10, 10);

    private static Voucher Draft(params VoucherLineInput[] lines)
    {
        var voucher = new Voucher(Guid.NewGuid(), CompanyId, VoucherType.Receipt, Date, "Test", DateTime.UtcNow, Guid.NewGuid());
        voucher.UpdateDraft(Date, "Test", lines);
        return voucher;
    }

    [Fact]
    public void Balanced_voucher_posts_and_produces_matching_ledger_entries()
    {
        var voucher = Draft(new(Cash, 1180m, 0), new(Sales, 0, 1000m), new(Rent, 0, 180m));

        var entries = voucher.Post("RV/2026-27/0001", 2026, DateTime.UtcNow, Guid.NewGuid());

        Assert.True(voucher.IsPosted);
        Assert.Equal(3, entries.Count);
        Assert.Equal(entries.Sum(e => e.Debit), entries.Sum(e => e.Credit));
        Assert.All(voucher.Lines, l => Assert.True(l.IsLocked));
    }

    [Fact]
    public void Unbalanced_voucher_cannot_post()
    {
        var voucher = Draft(new(Cash, 1000m, 0), new(Sales, 0, 999.99m));

        var error = Assert.Throws<DomainException>(() => voucher.Post("X", 2026, DateTime.UtcNow, Guid.NewGuid()));
        Assert.Contains("differ by 0.01", error.Message, StringComparison.Ordinal);
        Assert.False(voucher.IsPosted);
    }

    [Fact]
    public void Single_line_zero_total_and_one_account_vouchers_are_rejected()
    {
        Assert.Throws<DomainException>(() => Draft(new VoucherLineInput(Cash, 100m, 0)).EnsureBalanced());
        Assert.Throws<DomainException>(() => Draft(new(Cash, 100m, 0), new(Cash, 0, 100m)).EnsureBalanced());
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(0, 0)]
    [InlineData(-5, 0)]
    [InlineData(10.001, 0)]
    public void Each_line_must_have_exactly_one_valid_side(decimal debit, decimal credit) =>
        Assert.Throws<DomainException>(() => Draft(new(Cash, debit, credit), new(Sales, 0, 100m)));

    [Fact]
    public void Posted_voucher_cannot_be_edited_or_posted_again()
    {
        var voucher = Draft(new(Cash, 100m, 0), new(Sales, 0, 100m));
        voucher.Post("RV/1", 2026, DateTime.UtcNow, Guid.NewGuid());

        Assert.Throws<DomainException>(() => voucher.UpdateDraft(Date, "Changed", [new(Cash, 1m, 0), new(Sales, 0, 1m)]));
        Assert.Throws<DomainException>(() => voucher.Post("RV/2", 2026, DateTime.UtcNow, Guid.NewGuid()));
    }

    [Fact]
    public void Reversal_swaps_sides_and_links_to_the_original()
    {
        var voucher = Draft(new(Cash, 100m, 0), new(Sales, 0, 100m));
        voucher.Post("RV/1", 2026, DateTime.UtcNow, Guid.NewGuid());

        var reversal = voucher.CreateReversal(Guid.NewGuid(), Date.AddDays(1), "", DateTime.UtcNow, Guid.NewGuid());

        Assert.Equal(VoucherType.Reversal, reversal.Type);
        Assert.Equal(voucher.Id, reversal.ReversesVoucherId);
        Assert.Equal("Reversal of RV/1", reversal.Narration);
        Assert.Equal(100m, reversal.Lines.Single(l => l.AccountId == Cash).Credit);
        Assert.Equal(100m, reversal.Lines.Single(l => l.AccountId == Sales).Debit);
        reversal.EnsureBalanced();
    }

    [Fact]
    public void Drafts_reversals_and_backdated_reversals_are_refused()
    {
        var draft = Draft(new(Cash, 100m, 0), new(Sales, 0, 100m));
        Assert.Throws<DomainException>(() => draft.CreateReversal(Guid.NewGuid(), Date, "", DateTime.UtcNow, Guid.NewGuid()));

        draft.Post("RV/1", 2026, DateTime.UtcNow, Guid.NewGuid());
        Assert.Throws<DomainException>(() => draft.CreateReversal(Guid.NewGuid(), Date.AddDays(-1), "", DateTime.UtcNow, Guid.NewGuid()));

        var reversal = draft.CreateReversal(Guid.NewGuid(), Date, "", DateTime.UtcNow, Guid.NewGuid());
        reversal.Post("RJ/1", 2026, DateTime.UtcNow, Guid.NewGuid());
        Assert.Throws<DomainException>(() => reversal.CreateReversal(Guid.NewGuid(), Date, "", DateTime.UtcNow, Guid.NewGuid()));
    }

    [Fact]
    public void Reversal_type_cannot_be_created_directly() =>
        Assert.Throws<DomainException>(() => new Voucher(Guid.NewGuid(), CompanyId, VoucherType.Reversal, Date, "", DateTime.UtcNow, Guid.NewGuid()));
}

public sealed class AccountDomainTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public void Accounts_only_move_under_groups_of_the_same_nature()
    {
        var expenses = new Account(Guid.NewGuid(), CompanyId, "5000", "Expenses", AccountNature.Expense, AccountKind.Group, null, null);
        var assets = new Account(Guid.NewGuid(), CompanyId, "1000", "Assets", AccountNature.Asset, AccountKind.Group, null, null);
        var rent = new Account(Guid.NewGuid(), CompanyId, "5305", "Rent", AccountNature.Expense, AccountKind.General, expenses.Id, null);
        var cash = new Account(Guid.NewGuid(), CompanyId, "1111", "Cash", AccountNature.Asset, AccountKind.Cash, assets.Id, null);

        Assert.Throws<DomainException>(() => rent.MoveTo(assets));
        Assert.Throws<DomainException>(() => rent.MoveTo(cash));
        rent.MoveTo(expenses);
    }

    [Fact]
    public void System_accounts_cannot_be_deactivated_or_retyped()
    {
        var cash = new Account(Guid.NewGuid(), CompanyId, "1111", "Cash", AccountNature.Asset, AccountKind.Cash, null, SystemAccounts.Cash);
        Assert.Throws<DomainException>(() => cash.SetActive(false));
        Assert.Throws<DomainException>(() => cash.ChangeKind(AccountKind.Bank));
    }
}

public sealed class ChartOfAccountsTemplateTests
{
    [Fact]
    public void Codes_and_names_are_unique()
    {
        Assert.Equal(ChartOfAccountsTemplate.Nodes.Count, ChartOfAccountsTemplate.Nodes.Select(n => n.Code).Distinct().Count());
        Assert.Equal(ChartOfAccountsTemplate.Nodes.Count, ChartOfAccountsTemplate.Nodes.Select(n => n.Name).Distinct().Count());
    }

    [Fact]
    public void Every_parent_is_an_earlier_group_of_the_same_nature()
    {
        var seen = new Dictionary<string, ChartOfAccountsTemplate.Node>();
        foreach (var node in ChartOfAccountsTemplate.Nodes)
        {
            if (node.ParentCode is { } parentCode)
            {
                Assert.True(seen.TryGetValue(parentCode, out var parent), $"{node.Code} parent {parentCode} must come first");
                Assert.Equal(AccountKind.Group, parent!.Kind);
                Assert.Equal(parent.Nature, node.Nature);
            }

            seen[node.Code] = node;
        }
    }

    [Fact]
    public void Every_system_key_exists_exactly_once()
    {
        var keys = typeof(SystemAccounts).GetFields().Select(f => (string)f.GetValue(null)!).ToList();
        foreach (var key in keys) Assert.Single(ChartOfAccountsTemplate.Nodes, n => n.SystemKey == key);
    }

    [Theory]
    [InlineData(SystemAccounts.Cash, AccountNature.Asset, AccountKind.Cash)]
    [InlineData(SystemAccounts.Inventory, AccountNature.Asset, AccountKind.Inventory)]
    [InlineData(SystemAccounts.OutputCgst, AccountNature.Liability, AccountKind.Tax)]
    [InlineData(SystemAccounts.InputIgst, AccountNature.Asset, AccountKind.Tax)]
    [InlineData(SystemAccounts.Sales, AccountNature.Income, AccountKind.General)]
    [InlineData(SystemAccounts.CostOfGoodsSold, AccountNature.Expense, AccountKind.General)]
    [InlineData(SystemAccounts.RetainedEarnings, AccountNature.Equity, AccountKind.General)]
    [InlineData(SystemAccounts.SundryDebtors, AccountNature.Asset, AccountKind.Group)]
    [InlineData(SystemAccounts.SundryCreditors, AccountNature.Liability, AccountKind.Group)]
    public void Mapped_accounts_have_the_documented_nature(string key, AccountNature nature, AccountKind kind)
    {
        var node = ChartOfAccountsTemplate.Nodes.Single(n => n.SystemKey == key);
        Assert.Equal(nature, node.Nature);
        Assert.Equal(kind, node.Kind);
    }
}

public sealed class VoucherRulesTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Account Cash = new(Guid.NewGuid(), CompanyId, "1111", "Cash", AccountNature.Asset, AccountKind.Cash, null, null);
    private static readonly Account Rent = new(Guid.NewGuid(), CompanyId, "5305", "Rent", AccountNature.Expense, AccountKind.General, null, null);
    private static readonly Account Sales = new(Guid.NewGuid(), CompanyId, "4101", "Sales", AccountNature.Income, AccountKind.General, null, null);
    private static readonly Dictionary<Guid, Account> Accounts = new[] { Cash, Rent, Sales }.ToDictionary(a => a.Id);

    private static Voucher Make(VoucherType type, params VoucherLineInput[] lines)
    {
        var v = new Voucher(Guid.NewGuid(), CompanyId, type, new DateOnly(2026, 10, 10), "", DateTime.UtcNow, Guid.NewGuid());
        v.UpdateDraft(v.Date, "", lines);
        return v;
    }

    [Fact]
    public void Payment_must_credit_cash_or_bank()
    {
        VoucherRules.Check(Make(VoucherType.Payment, new(Rent.Id, 500m, 0), new(Cash.Id, 0, 500m)), Accounts);
        Assert.Throws<DomainException>(() => VoucherRules.Check(Make(VoucherType.Payment, new(Cash.Id, 500m, 0), new(Sales.Id, 0, 500m)), Accounts));
    }

    [Fact]
    public void Receipt_must_debit_cash_or_bank() =>
        Assert.Throws<DomainException>(() => VoucherRules.Check(Make(VoucherType.Receipt, new(Rent.Id, 500m, 0), new(Sales.Id, 0, 500m)), Accounts));

    [Fact]
    public void Journal_cannot_use_cash() =>
        Assert.Throws<DomainException>(() => VoucherRules.Check(Make(VoucherType.Journal, new(Cash.Id, 500m, 0), new(Sales.Id, 0, 500m)), Accounts));

    [Fact]
    public void Contra_uses_only_cash_and_bank() =>
        Assert.Throws<DomainException>(() => VoucherRules.Check(Make(VoucherType.Contra, new(Cash.Id, 500m, 0), new(Sales.Id, 0, 500m)), Accounts));
}

/// <summary>Report calculations on hand-made postings, independent of the database.</summary>
public sealed class LedgerCalculatorTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private readonly Account _cash = new(Guid.NewGuid(), CompanyId, "1111", "Cash", AccountNature.Asset, AccountKind.Cash, null, SystemAccounts.Cash);
    private readonly Account _capital = new(Guid.NewGuid(), CompanyId, "3101", "Capital", AccountNature.Equity, AccountKind.General, null, SystemAccounts.Capital);
    private readonly Account _retained = new(Guid.NewGuid(), CompanyId, "3201", "Retained", AccountNature.Equity, AccountKind.General, null, SystemAccounts.RetainedEarnings);
    private readonly Account _sales = new(Guid.NewGuid(), CompanyId, "4101", "Sales", AccountNature.Income, AccountKind.General, null, SystemAccounts.Sales);
    private readonly Account _rent = new(Guid.NewGuid(), CompanyId, "5305", "Rent", AccountNature.Expense, AccountKind.General, null, null);
    private readonly List<LedgerEntry> _entries = [];

    private Account[] Accounts => [_cash, _capital, _retained, _sales, _rent];

    private void Post(DateOnly date, Account debit, Account credit, decimal amount)
    {
        var voucher = Guid.NewGuid();
        var year = Nexora.Domain.Companies.FinancialYear.Containing(date).StartYear;
        _entries.Add(new LedgerEntry(Guid.NewGuid(), CompanyId, voucher, 1, debit.Id, date, year, amount, 0));
        _entries.Add(new LedgerEntry(Guid.NewGuid(), CompanyId, voucher, 2, credit.Id, date, year, 0, amount));
    }

    [Fact]
    public void Year_end_carries_forward_balance_sheet_accounts_and_resets_income_and_expense()
    {
        Post(new DateOnly(2026, 4, 1), _cash, _capital, 100_000m);
        Post(new DateOnly(2026, 6, 1), _cash, _sales, 50_000m);
        Post(new DateOnly(2026, 7, 1), _rent, _cash, 20_000m);
        Post(new DateOnly(2027, 4, 10), _cash, _sales, 5_000m);

        var lastYear = LedgerCalculator.TrialBalance(Accounts, _entries, new DateOnly(2027, 3, 31), _retained.Id);
        var thisYear = LedgerCalculator.TrialBalance(Accounts, _entries, new DateOnly(2027, 4, 30), _retained.Id);

        Assert.True(lastYear.IsBalanced);
        Assert.True(thisYear.IsBalanced);
        Assert.Equal(30_000m, lastYear.Rows.Single(r => r.AccountId == _sales.Id).Credit - 20_000m); // 50,000 sales less 20,000 rent
        Assert.Equal(135_000m, thisYear.Rows.Single(r => r.AccountId == _cash.Id).Debit);
        Assert.Equal(30_000m, thisYear.Rows.Single(r => r.AccountId == _retained.Id).Credit);
        Assert.Equal(5_000m, thisYear.Rows.Single(r => r.AccountId == _sales.Id).Credit);
        Assert.DoesNotContain(thisYear.Rows, r => r.AccountId == _rent.Id);
    }

    [Fact]
    public void Balance_sheet_balances_and_profit_matches_profit_and_loss()
    {
        Post(new DateOnly(2026, 4, 1), _cash, _capital, 100_000m);
        Post(new DateOnly(2026, 6, 1), _cash, _sales, 50_000m);
        Post(new DateOnly(2026, 7, 1), _rent, _cash, 20_000m);

        var asOf = new DateOnly(2026, 12, 31);
        var sheet = LedgerCalculator.BalanceSheet(Accounts, _entries, asOf, _retained.Id);
        var profit = LedgerCalculator.ProfitAndLoss(Accounts, _entries, new DateOnly(2026, 4, 1), asOf);

        Assert.True(sheet.IsBalanced);
        Assert.Equal(130_000m, sheet.TotalAssets);
        Assert.Equal(30_000m, profit.NetProfit);
        Assert.Equal(profit.NetProfit, sheet.CurrentYearProfit);
    }

    [Fact]
    public void Statement_running_balance_ends_at_the_trial_balance_figure()
    {
        Post(new DateOnly(2026, 4, 1), _cash, _capital, 100_000m);
        Post(new DateOnly(2026, 6, 1), _cash, _sales, 50_000m);
        Post(new DateOnly(2026, 7, 1), _rent, _cash, 20_000m);
        var vouchers = new Dictionary<Guid, Voucher>();
        foreach (var id in _entries.Select(e => e.VoucherId).Distinct())
        {
            var v = new Voucher(id, CompanyId, VoucherType.Journal, new DateOnly(2026, 4, 1), "", DateTime.UtcNow, Guid.NewGuid());
            vouchers[id] = v;
        }

        var statement = LedgerCalculator.Statement(_cash, _entries, vouchers, new DateOnly(2026, 5, 1), new DateOnly(2027, 3, 31));
        var trial = LedgerCalculator.TrialBalance(Accounts, _entries, new DateOnly(2027, 3, 31), _retained.Id);

        Assert.Equal(100_000m, statement.OpeningBalance);
        Assert.Equal(2, statement.Lines.Count);
        Assert.Equal(trial.Rows.Single(r => r.AccountId == _cash.Id).Net, statement.ClosingBalance);
        Assert.Throws<ArgumentException>(() =>
            LedgerCalculator.Statement(_cash, _entries, vouchers, new DateOnly(2026, 5, 1), new DateOnly(2027, 4, 30)));
    }
}
