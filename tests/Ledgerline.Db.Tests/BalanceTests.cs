namespace Ledgerline.Db.Tests;

// Balances are derived from the ledger through the indexed view
// ledger.v_account_balance, never stored (blueprint 7.4, ADR-007). The view
// must always agree with the entries, and the whole ledger must sum to zero.
public class BalanceTests(BankCoreDatabase db) : IClassFixture<BankCoreDatabase>, IAsyncLifetime
{
    public Task InitializeAsync() => db.EnsureOpenBusinessDateAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // Accounts where the view and a fresh sum of the entries disagree, or
    // where one side has a row and the other has none.
    private const string ViewDisagreementsSql =
        """
        SELECT COUNT(*)
        FROM (SELECT ledger_account_id, SUM(amount) AS total, COUNT_BIG(*) AS entries
              FROM ledger.entry
              GROUP BY ledger_account_id) AS e
        FULL JOIN ledger.v_account_balance AS v WITH (NOEXPAND)
            ON v.ledger_account_id = e.ledger_account_id
        WHERE e.ledger_account_id IS NULL
           OR v.ledger_account_id IS NULL
           OR v.net_amount <> e.total
           OR v.entry_count <> e.entries;
        """;

    [Fact]
    public async Task The_view_net_amount_equals_the_sum_of_entries_for_every_account()
    {
        var first = await db.NewCustomerLedgerAccountAsync();
        var second = await db.NewCustomerLedgerAccountAsync();
        await db.FundAsync(first, 300.00m);
        await db.PostJournalAsync(BankCoreDatabase.NewKey(), "TRANSFER",
            new JournalLine(first, 120.50m), new JournalLine(second, -120.50m));

        Assert.Equal(0, await db.ScalarAsync<int>(ViewDisagreementsSql));
        Assert.Equal(179.50m, await db.CustomerBalanceAsync(first));
        Assert.Equal(120.50m, await db.CustomerBalanceAsync(second));
    }

    // An account with no entries has no row in the view, and its balance is
    // zero: a debit is refused as an overdraft, and the first credit creates
    // the row.
    [Fact]
    public async Task A_new_account_has_no_view_row_and_reads_as_zero()
    {
        var customer = await db.NewCustomerLedgerAccountAsync();
        var clearing = await db.GlLedgerAccountIdAsync("1250");

        Assert.Equal(0, await ViewRowsAsync(customer));
        await BankCoreDatabase.AssertRefusedAsync(
            () => db.PostJournalAsync(BankCoreDatabase.NewKey(), "PAYMENT_OUT",
                new JournalLine(customer, 0.01m), new JournalLine(clearing, -0.01m)),
            50006);

        await db.FundAsync(customer, 0.01m);

        Assert.Equal(1, await ViewRowsAsync(customer));
        Assert.Equal(0.01m, await db.CustomerBalanceAsync(customer));
    }

    // Trial balance: after a few hundred random valid postings, every debit
    // has its credit, so the whole ledger sums to zero, every journal nets to
    // zero, the view agrees with the entries, and every customer balance is
    // what the test worked out on its own. The fixed seed makes a failure
    // repeatable.
    [Fact]
    public async Task After_many_random_postings_the_ledger_sums_to_zero_and_the_view_agrees()
    {
        var random = new Random(42);
        var cash = await db.GlLedgerAccountIdAsync("1010");
        var clearing = await db.GlLedgerAccountIdAsync("1250");
        var fees = await db.GlLedgerAccountIdAsync("4100");
        var interest = await db.GlLedgerAccountIdAsync("5100");

        var customers = new List<long>();
        var expected = new Dictionary<long, decimal>();
        for (var i = 0; i < 8; i++)
        {
            var customer = await db.NewCustomerLedgerAccountAsync();
            customers.Add(customer);
            expected[customer] = 0m;
        }

        for (var i = 0; i < 300; i++)
        {
            var customer = customers[random.Next(customers.Count)];
            var other = customers[random.Next(customers.Count)];

            // Whole fils, from 0.01 up to 500.00.
            var amount = random.Next(1, 50_001) / 100m;

            switch (random.Next(5))
            {
                case 0: // salary credit: Dr cash, Cr customer
                    await Post("SALARY_CREDIT", new JournalLine(cash, amount), new JournalLine(customer, -amount));
                    expected[customer] += amount;
                    break;

                case 1: // interest: Dr interest expense, Cr customer
                    amount = random.Next(1, 1_001) / 100m;
                    await Post("INTEREST_CAPITALISATION", new JournalLine(interest, amount), new JournalLine(customer, -amount));
                    expected[customer] += amount;
                    break;

                // The debits below stay within the balance, so none is refused.
                case 2 when other != customer && expected[customer] > 0m:
                    amount = Math.Min(amount, expected[customer]);
                    await Post("TRANSFER", new JournalLine(customer, amount), new JournalLine(other, -amount));
                    expected[customer] -= amount;
                    expected[other] += amount;
                    break;

                case 3 when expected[customer] > 0m:
                    amount = Math.Min(amount, expected[customer]);
                    await Post("PAYMENT_OUT", new JournalLine(customer, amount), new JournalLine(clearing, -amount));
                    expected[customer] -= amount;
                    break;

                case 4 when expected[customer] >= 1.00m:
                    await Post("FEE", new JournalLine(customer, 1.00m), new JournalLine(fees, -1.00m));
                    expected[customer] -= 1.00m;
                    break;

                default: // a debit the account cannot cover this time: settle 1250 against cash instead
                    await Post("CLEARING_SETTLEMENT", new JournalLine(clearing, amount), new JournalLine(cash, -amount));
                    break;
            }
        }

        Assert.Equal(0m, await db.ScalarAsync<decimal>("SELECT ISNULL(SUM(amount), 0) FROM ledger.entry;"));
        Assert.Equal(0, await db.ScalarAsync<int>(
            """
            SELECT COUNT(*) FROM (
                SELECT journal_id FROM ledger.entry
                GROUP BY journal_id, currency_code
                HAVING SUM(amount) <> 0) AS unbalanced;
            """));
        Assert.Equal(0, await db.ScalarAsync<int>(ViewDisagreementsSql));
        Assert.Equal(0m, await db.ScalarAsync<decimal>(
            "SELECT ISNULL(SUM(net_amount), 0) FROM ledger.v_account_balance WITH (NOEXPAND);"));

        foreach (var customer in customers)
        {
            Assert.Equal(expected[customer], await db.CustomerBalanceAsync(customer));
        }

        Task Post(string journalType, params JournalLine[] lines) =>
            db.PostJournalAsync(BankCoreDatabase.NewKey(), journalType, lines);
    }

    private Task<int> ViewRowsAsync(long ledgerAccountId) =>
        db.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM ledger.v_account_balance WITH (NOEXPAND) WHERE ledger_account_id = {ledgerAccountId};");
}
