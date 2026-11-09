using Microsoft.Data.SqlClient;

namespace Ledgerline.Db.Tests;

// ledger.usp_post_journal, the one door into the ledger (blueprint 7.3): what
// a valid posting writes, each refusal of bad input, and how the procedure
// behaves inside a caller's transaction. The class shares one database with
// one open business date.
public class PostJournalTests(BankCoreDatabase db) : IClassFixture<BankCoreDatabase>, IAsyncLifetime
{
    public Task InitializeAsync() => db.EnsureOpenBusinessDateAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // --- A valid posting --------------------------------------------------------

    [Fact]
    public async Task A_balanced_journal_creates_one_journal_and_its_entries_on_the_open_date()
    {
        var cash = await db.GlLedgerAccountIdAsync("1010");
        var customer = await db.NewCustomerLedgerAccountAsync();
        var key = BankCoreDatabase.NewKey();

        var journalId = await db.PostJournalAsync(key, "SALARY_CREDIT",
            new JournalLine(cash, 250.00m), new JournalLine(customer, -250.00m));

        // The output parameter carries the id of the journal just written.
        Assert.Equal(journalId, await db.ScalarAsync<long>(
            $"SELECT journal_id FROM ledger.journal WHERE idempotency_key = N'{key}';"));

        var journal = await db.StringsAsync(
            $"""
            SELECT CONCAT(journal_type, '|', business_date, '|', ISNULL(CAST(reverses_journal_id AS varchar(20)), 'none'))
            FROM ledger.journal WHERE journal_id = {journalId};
            """);
        Assert.Equal(new[] { $"SALARY_CREDIT|{BankCoreDatabase.OpenDate}|none" }, journal);

        // posted_at_utc is the server's UTC clock at the moment of posting.
        var secondsAgo = await db.ScalarAsync<int>(
            $"SELECT DATEDIFF(second, posted_at_utc, SYSUTCDATETIME()) FROM ledger.journal WHERE journal_id = {journalId};");
        Assert.InRange(secondsAgo, 0, 60);

        // Both entries, each stamped with the open business date.
        var entries = await db.StringsAsync(
            $"""
            SELECT CONCAT(ledger_account_id, '|', amount, '|', currency_code, '|', business_date)
            FROM ledger.entry WHERE journal_id = {journalId}
            ORDER BY amount DESC;
            """);
        Assert.Equal(new[]
        {
            $"{cash}|250.0000|AED|{BankCoreDatabase.OpenDate}",
            $"{customer}|-250.0000|AED|{BankCoreDatabase.OpenDate}",
        }, entries);
    }

    [Fact]
    public async Task A_transfer_moves_both_balances()
    {
        var from = await db.NewCustomerLedgerAccountAsync();
        var to = await db.NewCustomerLedgerAccountAsync();
        await db.FundAsync(from, 100.00m);

        // Dr the payer (their balance goes down), Cr the payee.
        await db.PostJournalAsync(BankCoreDatabase.NewKey(), "TRANSFER",
            new JournalLine(from, 30.00m), new JournalLine(to, -30.00m));

        Assert.Equal(70.00m, await db.CustomerBalanceAsync(from));
        Assert.Equal(30.00m, await db.CustomerBalanceAsync(to));
    }

    // --- INV-01: every journal balances -------------------------------------------

    [Fact]
    public async Task An_unbalanced_journal_is_refused_and_writes_nothing()
    {
        var cash = await db.GlLedgerAccountIdAsync("1010");
        var customer = await db.NewCustomerLedgerAccountAsync();
        var key = BankCoreDatabase.NewKey();

        await BankCoreDatabase.AssertRefusedAsync(
            () => db.PostJournalAsync(key, "SALARY_CREDIT",
                new JournalLine(cash, 100.00m), new JournalLine(customer, -99.99m)),
            50001);

        Assert.Equal(0, await JournalsWithKeyAsync(key));
    }

    // Balanced in total but not within each currency: still unbalanced.
    [Fact]
    public async Task A_journal_that_nets_to_zero_only_across_currencies_is_refused()
    {
        var cash = await db.GlLedgerAccountIdAsync("1010");
        var customer = await db.NewCustomerLedgerAccountAsync();

        await BankCoreDatabase.AssertRefusedAsync(
            () => db.PostJournalAsync(BankCoreDatabase.NewKey(), "SALARY_CREDIT",
                new JournalLine(cash, 10.00m, "AED"), new JournalLine(customer, -10.00m, "USD")),
            50001);
    }

    // --- Bad input ------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task A_journal_with_fewer_than_two_lines_is_refused(int lineCount)
    {
        var cash = await db.GlLedgerAccountIdAsync("1010");
        var lines = Enumerable.Repeat(new JournalLine(cash, 10.00m), lineCount).ToArray();

        await BankCoreDatabase.AssertRefusedAsync(
            () => db.PostJournalAsync(BankCoreDatabase.NewKey(), "SALARY_CREDIT", lines), 50003);
    }

    [Fact]
    public async Task A_zero_amount_is_refused()
    {
        var cash = await db.GlLedgerAccountIdAsync("1010");
        var fees = await db.GlLedgerAccountIdAsync("4100");
        var customer = await db.NewCustomerLedgerAccountAsync();

        await BankCoreDatabase.AssertRefusedAsync(
            () => db.PostJournalAsync(BankCoreDatabase.NewKey(), "SALARY_CREDIT",
                new JournalLine(cash, 10.00m), new JournalLine(customer, -10.00m), new JournalLine(fees, 0m)),
            50004);
    }

    // AED has 100 fils to the dirham, so a posting is in whole fils. The lines
    // balance, so only the fils rule can refuse them.
    [Fact]
    public async Task An_amount_finer_than_one_fils_is_refused()
    {
        var cash = await db.GlLedgerAccountIdAsync("1010");
        var customer = await db.NewCustomerLedgerAccountAsync();

        await BankCoreDatabase.AssertRefusedAsync(
            () => db.PostJournalAsync(BankCoreDatabase.NewKey(), "SALARY_CREDIT",
                new JournalLine(cash, 10.005m), new JournalLine(customer, -10.005m)),
            50004);
    }

    [Fact]
    public async Task An_unknown_ledger_account_is_refused()
    {
        var cash = await db.GlLedgerAccountIdAsync("1010");

        await BankCoreDatabase.AssertRefusedAsync(
            () => db.PostJournalAsync(BankCoreDatabase.NewKey(), "SALARY_CREDIT",
                new JournalLine(cash, 10.00m), new JournalLine(-1, -10.00m)),
            50005);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_missing_or_blank_idempotency_key_is_refused(string? key)
    {
        var cash = await db.GlLedgerAccountIdAsync("1010");
        var customer = await db.NewCustomerLedgerAccountAsync();

        await BankCoreDatabase.AssertRefusedAsync(
            () => db.PostJournalAsync(key, "SALARY_CREDIT",
                new JournalLine(cash, 10.00m), new JournalLine(customer, -10.00m)),
            50010);
    }

    // LOAN_DRAWDOWN fits VARCHAR(30), so only the check constraint can refuse it.
    [Fact]
    public async Task An_unknown_journal_type_is_refused_by_the_check_constraint()
    {
        var cash = await db.GlLedgerAccountIdAsync("1010");
        var customer = await db.NewCustomerLedgerAccountAsync();

        await BankCoreDatabase.AssertRefusedAsync(
            () => db.PostJournalAsync(BankCoreDatabase.NewKey(), "LOAN_DRAWDOWN",
                new JournalLine(cash, 10.00m), new JournalLine(customer, -10.00m)),
            547, "ck_journal_journal_type");
    }

    // v1 is AED only. The lines balance in USD, so the procedure lets them
    // through and the entry's check constraint refuses them.
    [Fact]
    public async Task A_currency_other_than_aed_is_refused_by_the_check_constraint()
    {
        var cash = await db.GlLedgerAccountIdAsync("1010");
        var customer = await db.NewCustomerLedgerAccountAsync();

        await BankCoreDatabase.AssertRefusedAsync(
            () => db.PostJournalAsync(BankCoreDatabase.NewKey(), "SALARY_CREDIT",
                new JournalLine(cash, 10.00m, "USD"), new JournalLine(customer, -10.00m, "USD")),
            547, "ck_entry_currency_code");
    }

    // --- Inside a caller's transaction ------------------------------------------

    [Fact]
    public async Task A_posting_inside_a_callers_transaction_is_undone_when_the_caller_rolls_back()
    {
        var (key, lines) = await SalaryCreditAsync();

        await using (var connection = await db.OpenAsync())
        await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync())
        {
            await BankCoreDatabase.PostJournalAsync(connection, transaction, key, "SALARY_CREDIT", lines);
            await transaction.RollbackAsync();
        }

        Assert.Equal(0, await JournalsWithKeyAsync(key));
    }

    [Fact]
    public async Task A_posting_inside_a_callers_transaction_is_kept_when_the_caller_commits()
    {
        var (key, lines) = await SalaryCreditAsync();

        await using (var connection = await db.OpenAsync())
        await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync())
        {
            await BankCoreDatabase.PostJournalAsync(connection, transaction, key, "SALARY_CREDIT", lines);
            await transaction.CommitAsync();
        }

        Assert.Equal(1, await JournalsWithKeyAsync(key));
    }

    // XACT_ABORT is on, so a refusal rolls back the whole transaction, the
    // caller's earlier work included. A half-done unit of work never commits.
    [Fact]
    public async Task A_refusal_inside_a_callers_transaction_rolls_back_the_callers_earlier_postings()
    {
        var (goodKey, goodLines) = await SalaryCreditAsync();
        var cash = await db.GlLedgerAccountIdAsync("1010");
        var customer = await db.NewCustomerLedgerAccountAsync();

        await using (var connection = await db.OpenAsync())
        await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync())
        {
            await BankCoreDatabase.PostJournalAsync(connection, transaction, goodKey, "SALARY_CREDIT", goodLines);

            await BankCoreDatabase.AssertRefusedAsync(
                () => BankCoreDatabase.PostJournalAsync(connection, transaction, BankCoreDatabase.NewKey(), "SALARY_CREDIT",
                    [new JournalLine(cash, 5.00m), new JournalLine(customer, -4.00m)]),
                50001);
        }

        Assert.Equal(0, await JournalsWithKeyAsync(goodKey));
    }

    // --- Locks held until the posting commits -----------------------------------
    // A posting is left open in a transaction, and a second connection tries
    // to change what the posting checked. It must wait, so with a one-second
    // lock timeout it gives up with error 1222. Both transactions are rolled
    // back when they are disposed, so nothing changes for the other tests.

    // INV-08: end of day cannot close a date while a posting to it is in flight.
    [Fact]
    public async Task End_of_day_cannot_move_the_open_date_while_a_posting_is_in_flight()
    {
        var (key, lines) = await SalaryCreditAsync();

        await using var poster = await db.OpenAsync();
        await using var posting = (SqlTransaction)await poster.BeginTransactionAsync();
        await BankCoreDatabase.PostJournalAsync(poster, posting, key, "SALARY_CREDIT", lines);

        var error = await BlockedChangeAsync("UPDATE ops.business_calendar SET status = 'EOD_RUNNING' WHERE status = 'OPEN';");

        Assert.Equal(1222, error?.Number);
    }

    // INV-05: a freeze cannot slip in between the status check and the insert.
    [Fact]
    public async Task An_account_cannot_be_frozen_while_a_posting_to_it_is_in_flight()
    {
        var cash = await db.GlLedgerAccountIdAsync("1010");
        var customer = await db.NewCustomerLedgerAccountAsync();

        await using var poster = await db.OpenAsync();
        await using var posting = (SqlTransaction)await poster.BeginTransactionAsync();
        await BankCoreDatabase.PostJournalAsync(poster, posting, BankCoreDatabase.NewKey(), "SALARY_CREDIT",
            [new JournalLine(cash, 10.00m), new JournalLine(customer, -10.00m)]);

        var error = await BlockedChangeAsync(
            $"""
            UPDATE a SET status = 'FROZEN'
            FROM core.account AS a
            JOIN ledger.ledger_account AS la ON la.account_id = a.account_id
            WHERE la.ledger_account_id = {customer};
            """);

        Assert.Equal(1222, error?.Number);
    }

    // Runs the change in its own transaction with a one-second lock timeout
    // and always rolls it back. Returns the error, or null if it went through.
    private async Task<SqlException?> BlockedChangeAsync(string sql)
    {
        await using var connection = await db.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            await using var command = new SqlCommand("SET LOCK_TIMEOUT 1000;\n" + sql, connection, transaction);
            await command.ExecuteNonQueryAsync();
            return null;
        }
        catch (SqlException error)
        {
            return error;
        }
        finally
        {
            await transaction.RollbackAsync();
            await using var reset = new SqlCommand("SET LOCK_TIMEOUT -1;", connection);
            await reset.ExecuteNonQueryAsync();
        }
    }

    // A valid salary credit to a new customer, with a fresh key.
    private async Task<(string Key, JournalLine[] Lines)> SalaryCreditAsync()
    {
        var cash = await db.GlLedgerAccountIdAsync("1010");
        var customer = await db.NewCustomerLedgerAccountAsync();
        return (BankCoreDatabase.NewKey(), [new JournalLine(cash, 40.00m), new JournalLine(customer, -40.00m)]);
    }

    private Task<int> JournalsWithKeyAsync(string key) =>
        db.ScalarAsync<int>($"SELECT COUNT(*) FROM ledger.journal WHERE idempotency_key = N'{key}';");
}
