namespace Ledgerline.Db.Tests;

// INV-04 (no customer account goes below zero, trap T6) and INV-05 (frozen
// or closed accounts cannot be debited, trap T5, BR-07). A customer account's
// balance is the negative of its entries, so a debit (positive amount)
// lowers it.
public class OverdraftAndStatusTests(BankCoreDatabase db) : IClassFixture<BankCoreDatabase>, IAsyncLifetime
{
    public Task InitializeAsync() => db.EnsureOpenBusinessDateAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // --- INV-04: no overdraft ---------------------------------------------------

    [Fact]
    public async Task A_debit_beyond_the_balance_is_refused()
    {
        var customer = await db.NewCustomerLedgerAccountAsync();
        await db.FundAsync(customer, 100.00m);

        await BankCoreDatabase.AssertRefusedAsync(() => PayOutAsync(customer, 100.01m), 50006);

        Assert.Equal(100.00m, await db.CustomerBalanceAsync(customer));
    }

    [Fact]
    public async Task Spending_down_to_exactly_zero_is_accepted()
    {
        var customer = await db.NewCustomerLedgerAccountAsync();
        await db.FundAsync(customer, 100.00m);

        await PayOutAsync(customer, 100.00m);

        Assert.Equal(0.00m, await db.CustomerBalanceAsync(customer));
    }

    // A new account has no row in the balance view. Its balance must read as
    // zero, not as unknown: an unknown balance could let the debit through.
    [Fact]
    public async Task A_debit_from_an_account_that_has_never_had_an_entry_is_refused()
    {
        var customer = await db.NewCustomerLedgerAccountAsync();

        await BankCoreDatabase.AssertRefusedAsync(() => PayOutAsync(customer, 0.01m), 50006);
    }

    [Fact]
    public async Task A_credit_is_always_accepted()
    {
        var customer = await db.NewCustomerLedgerAccountAsync();

        await db.FundAsync(customer, 0.01m);

        Assert.Equal(0.01m, await db.CustomerBalanceAsync(customer));
    }

    // The check is on the journal's net effect on each account, not line by
    // line: a debit of 150 and a credit of 100 to the same account is a net
    // debit of 50, more than the 40 it holds.
    [Fact]
    public async Task The_net_debit_of_all_lines_for_one_account_is_what_is_checked()
    {
        var customer = await db.NewCustomerLedgerAccountAsync();
        var clearing = await db.GlLedgerAccountIdAsync("1250");
        await db.FundAsync(customer, 40.00m);

        await BankCoreDatabase.AssertRefusedAsync(
            () => db.PostJournalAsync(BankCoreDatabase.NewKey(), "PAYMENT_OUT",
                new JournalLine(customer, 150.00m), new JournalLine(customer, -100.00m), new JournalLine(clearing, -50.00m)),
            50006);
    }

    // The bank's own accounts have no such limit: 4100 and 5100 can each go
    // either way.
    [Fact]
    public async Task Gl_accounts_may_go_either_way()
    {
        var fees = await db.GlLedgerAccountIdAsync("4100");
        var interest = await db.GlLedgerAccountIdAsync("5100");

        await db.PostJournalAsync(BankCoreDatabase.NewKey(), "FEE",
            new JournalLine(fees, 1_000_000.00m), new JournalLine(interest, -1_000_000.00m));
        await db.PostJournalAsync(BankCoreDatabase.NewKey(), "FEE",
            new JournalLine(fees, -3_000_000.00m), new JournalLine(interest, 3_000_000.00m));
    }

    // --- INV-05: frozen and closed accounts ---------------------------------------

    [Fact]
    public async Task A_frozen_account_refuses_a_debit()
    {
        var customer = await db.NewCustomerLedgerAccountAsync();
        await db.FundAsync(customer, 100.00m);
        await db.SetAccountStatusAsync(customer, "FROZEN");

        await BankCoreDatabase.AssertRefusedAsync(() => PayOutAsync(customer, 10.00m), 50007);

        Assert.Equal(100.00m, await db.CustomerBalanceAsync(customer));
    }

    // BR-07: a frozen account can still receive money, a salary for example.
    [Fact]
    public async Task A_frozen_account_accepts_a_credit()
    {
        var customer = await db.NewCustomerLedgerAccountAsync();
        await db.FundAsync(customer, 100.00m);
        await db.SetAccountStatusAsync(customer, "FROZEN");

        await db.FundAsync(customer, 10.00m);

        Assert.Equal(110.00m, await db.CustomerBalanceAsync(customer));
    }

    [Fact]
    public async Task A_closed_account_refuses_a_debit()
    {
        var customer = await db.NewCustomerLedgerAccountAsync();
        await db.FundAsync(customer, 100.00m);
        await db.SetAccountStatusAsync(customer, "CLOSED");

        await BankCoreDatabase.AssertRefusedAsync(() => PayOutAsync(customer, 10.00m), 50008);
    }

    [Fact]
    public async Task A_closed_account_refuses_a_credit()
    {
        var customer = await db.NewCustomerLedgerAccountAsync();
        await db.FundAsync(customer, 100.00m);
        await db.SetAccountStatusAsync(customer, "CLOSED");

        await BankCoreDatabase.AssertRefusedAsync(() => db.FundAsync(customer, 10.00m), 50008);

        Assert.Equal(100.00m, await db.CustomerBalanceAsync(customer));
    }

    // A payment out: Dr the customer, Cr outbound payments clearing (1250).
    private async Task PayOutAsync(long customer, decimal amount)
    {
        var clearing = await db.GlLedgerAccountIdAsync("1250");
        await db.PostJournalAsync(BankCoreDatabase.NewKey(), "PAYMENT_OUT",
            new JournalLine(customer, amount), new JournalLine(clearing, -amount));
    }
}
