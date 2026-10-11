using Nexora.Accounting;
using Nexora.Application;
using Nexora.Application.Security;
using Nexora.Domain.Accounting;
using Nexora.Tests.Billing;

namespace Nexora.Tests.Phase8;

public abstract class AccountingAdditionsBase : BillingTestBase
{
    protected VoucherService Vouchers => Db.Get<VoucherService>();

    protected async Task<Guid> AccountAsync(string name) => (await Db.Get<ChartOfAccountsService>().ListAsync()).Single(a => a.Name == name).Id;

    protected Task<(Guid Id, string Number)> PostAsync(VoucherType type, DateOnly date, params VoucherLineInput[] lines) =>
        Vouchers.SaveAndPostAsync(new VoucherInput(null, type, date, $"{type} test", lines));
}

public sealed class CostCentreTests : AccountingAdditionsBase
{
    private CostCentreService Centres => Db.Get<CostCentreService>();

    [Fact]
    public async Task Income_and_expense_lines_carry_a_centre_and_reversals_keep_it()
    {
        var shop = await Centres.SaveAsync(null, "shop", "Shop floor", CostCentreKind.Profit);
        var rent = await AccountAsync("Rent");
        var (id, _) = await PostAsync(VoucherType.Payment, October, new VoucherLineInput(rent, 5_000, 0, CostCentreId: shop), new VoucherLineInput(CashAccount, 0, 5_000));

        Assert.Equal(shop, (await Vouchers.GetAsync(id)).Lines.Single(l => l.AccountId == rent).CostCentreId);
        Assert.Equal("SHOP", (await Centres.ListAsync()).Single().Code);

        await Vouchers.ReverseAsync(id, October, "wrong");
        var reversal = (await Vouchers.GetAsync(id)).ReversedById!.Value;
        Assert.Equal(shop, (await Vouchers.GetAsync(reversal)).Lines.Single(l => l.AccountId == rent).CostCentreId);
    }

    [Fact]
    public async Task Centres_are_refused_on_balance_sheet_lines_when_inactive_and_when_duplicated()
    {
        var shop = await Centres.SaveAsync(null, "SHOP", "Shop floor", CostCentreKind.Profit);
        var rent = await AccountAsync("Rent");

        var balanceSheet = await Assert.ThrowsAsync<WorkspaceValidationException>(() =>
            PostAsync(VoucherType.Payment, October, new VoucherLineInput(rent, 100, 0), new VoucherLineInput(CashAccount, 0, 100, CostCentreId: shop)));
        Assert.Contains("only income and expense", balanceSheet.Message, StringComparison.Ordinal);

        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Centres.SaveAsync(null, "shop", "Another", CostCentreKind.Cost));
        await Centres.SetActiveAsync(shop, false);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() =>
            PostAsync(VoucherType.Payment, October, new VoucherLineInput(rent, 100, 0, CostCentreId: shop), new VoucherLineInput(CashAccount, 0, 100)));
    }

    [Fact]
    public async Task Only_accounts_managers_change_centres()
    {
        await SignInAsAsync("bina", "Billing Operator");
        await Assert.ThrowsAsync<AccessDeniedException>(() => Centres.SaveAsync(null, "X", "X", CostCentreKind.Cost));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Centres.ListAsync());
    }
}

public sealed class BankReconciliationTests : AccountingAdditionsBase
{
    private BankReconciliationService Bank => Db.Get<BankReconciliationService>();

    [Fact]
    public async Task Uncleared_items_explain_the_difference_between_books_and_bank()
    {
        var bank = await AccountAsync("Primary Bank Account");
        var capital = await AccountAsync("Owner's Capital");
        var rent = await AccountAsync("Rent");
        await PostAsync(VoucherType.Receipt, October, new VoucherLineInput(bank, 5_000, 0), new VoucherLineInput(capital, 0, 5_000));
        await PostAsync(VoucherType.Payment, October, new VoucherLineInput(rent, 1_000, 0), new VoucherLineInput(bank, 0, 1_000));

        var before = await Bank.GetAsync(bank, October);
        Assert.Equal((4_000m, 5_000m, 1_000m, 0m), (before.BookBalance, before.UnclearedDeposits, before.UnclearedWithdrawals, before.BankBalance));
        Assert.Equal(2, before.Entries.Count);

        var deposit = before.Entries.Single(e => e.Deposit == 5_000);
        await Bank.ClearAsync([deposit.EntryId], October.AddDays(2), "STMT-10");

        var onStatement = await Bank.GetAsync(bank, October.AddDays(2));
        Assert.Equal((4_000m, 0m, 1_000m, 5_000m), (onStatement.BookBalance, onStatement.UnclearedDeposits, onStatement.UnclearedWithdrawals, onStatement.BankBalance));
        Assert.Single(onStatement.Entries);

        // As of the day before the statement, the deposit was still outstanding.
        Assert.Equal(0m, (await Bank.GetAsync(bank, October.AddDays(1))).BankBalance);
        var withCleared = await Bank.GetAsync(bank, October.AddDays(2), clearedFrom: October);
        Assert.Equal("STMT-10", withCleared.Entries.Single(e => e.IsCleared).StatementReference);

        await Bank.UnclearAsync([withCleared.Entries.Single(e => e.IsCleared).ClearingId!.Value]);
        Assert.Equal(0m, (await Bank.GetAsync(bank, October.AddDays(2))).BankBalance);
    }

    [Fact]
    public async Task Clearing_rules_are_enforced()
    {
        var bank = await AccountAsync("Primary Bank Account");
        var capital = await AccountAsync("Owner's Capital");
        await PostAsync(VoucherType.Receipt, October, new VoucherLineInput(bank, 500, 0), new VoucherLineInput(capital, 0, 500));
        var entry = (await Bank.GetAsync(bank, October)).Entries.Single();

        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Bank.ClearAsync([entry.EntryId], October.AddDays(-1), ""));   // before the entry
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Bank.GetAsync(CashAccount, October));                        // not a bank account
        await Bank.ClearAsync([entry.EntryId], October, "");
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Bank.ClearAsync([entry.EntryId], October, ""));              // already cleared

        await SignInAsAsync("bina", "Billing Operator");
        await Assert.ThrowsAsync<AccessDeniedException>(() => Bank.GetAsync(bank, October));
    }
}
