namespace Ledgerline.Db.Tests;

// The ledger protects itself even from SQL that goes around the procedure:
// an AFTER INSERT trigger refuses any insert that leaves a journal
// unbalanced (INV-01 backstop), and INSTEAD OF triggers refuse every UPDATE
// and DELETE (INV-02, trap T8). Corrections are reversal journals. Ledger
// accounts are insert-only too, so an account's history cannot be moved.
public class LedgerImmutabilityTests(BankCoreDatabase db) : IClassFixture<BankCoreDatabase>, IAsyncLifetime
{
    public Task InitializeAsync() => db.EnsureOpenBusinessDateAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // --- INV-01 backstop ---------------------------------------------------------

    [Fact]
    public async Task A_direct_unbalanced_insert_is_refused_by_the_trigger_and_leaves_no_rows()
    {
        var journalId = await PostOneJournalAsync();
        var cash = await db.GlLedgerAccountIdAsync("1010");
        var before = await LedgerRowsAsync(journalId);

        await BankCoreDatabase.AssertRefusedAsync(
            () => db.ExecuteAsync(
                $"""
                INSERT INTO ledger.entry (journal_id, business_date, ledger_account_id, amount, currency_code)
                VALUES ({journalId}, '{BankCoreDatabase.OpenDate}', {cash}, 5.00, 'AED');
                """),
            50001);

        Assert.Equal(before, await LedgerRowsAsync(journalId));
    }

    // The two new lines cancel out overall, but each journal on its own would
    // no longer balance. The check is per journal.
    [Fact]
    public async Task A_direct_insert_that_balances_only_across_two_journals_is_refused()
    {
        var first = await PostOneJournalAsync();
        var second = await PostOneJournalAsync();
        var cash = await db.GlLedgerAccountIdAsync("1010");
        var before = await LedgerRowsAsync(first) + await LedgerRowsAsync(second);

        await BankCoreDatabase.AssertRefusedAsync(
            () => db.ExecuteAsync(
                $"""
                INSERT INTO ledger.entry (journal_id, business_date, ledger_account_id, amount, currency_code)
                VALUES ({first},  '{BankCoreDatabase.OpenDate}', {cash},  5.00, 'AED'),
                       ({second}, '{BankCoreDatabase.OpenDate}', {cash}, -5.00, 'AED');
                """),
            50001);

        Assert.Equal(before, await LedgerRowsAsync(first) + await LedgerRowsAsync(second));
    }

    // --- INV-02: nothing is ever changed or deleted ----------------------------------
    // Doubling both amounts keeps the journal balanced, so only the
    // immutability trigger can refuse it.

    [Theory]
    [InlineData("UPDATE ledger.entry SET amount = amount * 2 WHERE journal_id = {0};")]
    [InlineData("DELETE FROM ledger.entry WHERE journal_id = {0};")]
    [InlineData("UPDATE ledger.journal SET journal_type = 'FEE' WHERE journal_id = {0};")]
    [InlineData("DELETE FROM ledger.journal WHERE journal_id = {0};")]
    public async Task A_direct_change_to_the_ledger_is_refused_and_changes_nothing(string sqlTemplate)
    {
        var journalId = await PostOneJournalAsync();
        var before = await LedgerRowsAsync(journalId);

        await BankCoreDatabase.AssertRefusedAsync(
            () => db.ExecuteAsync(string.Format(sqlTemplate, journalId)), 50002);

        Assert.Equal(before, await LedgerRowsAsync(journalId));
    }

    // A ledger account's link is its identity: repointing it to another
    // account would move the first account's whole history. The new
    // accounts have no entries, so no foreign key could refuse the DELETE;
    // only the trigger can.
    [Fact]
    public async Task Repointing_a_ledger_account_to_another_account_is_refused_and_changes_nothing()
    {
        var ledgerAccount = await db.NewCustomerLedgerAccountAsync();
        var otherCustomer = await db.InsertCustomerAsync();
        var otherAccount = await db.OpenAccountAsync(otherCustomer, "SAVINGS_AED");
        var before = await LedgerAccountRowAsync(ledgerAccount);

        await BankCoreDatabase.AssertRefusedAsync(
            () => db.ExecuteAsync(
                $"UPDATE ledger.ledger_account SET account_id = {otherAccount} WHERE ledger_account_id = {ledgerAccount};"),
            50002);

        Assert.Equal(before, await LedgerAccountRowAsync(ledgerAccount));
    }

    [Fact]
    public async Task Deleting_a_ledger_account_is_refused_and_changes_nothing()
    {
        var ledgerAccount = await db.NewCustomerLedgerAccountAsync();
        var before = await LedgerAccountRowAsync(ledgerAccount);

        await BankCoreDatabase.AssertRefusedAsync(
            () => db.ExecuteAsync($"DELETE FROM ledger.ledger_account WHERE ledger_account_id = {ledgerAccount};"),
            50002);

        Assert.Equal(before, await LedgerAccountRowAsync(ledgerAccount));
    }

    // TRUNCATE skips triggers, but SQL Server refuses it on a table that an
    // indexed view is built on (3729) or that a foreign key points at (4712).
    [Theory]
    [InlineData("ledger.entry", 3729)]
    [InlineData("ledger.journal", 4712)]
    public async Task Truncating_a_ledger_table_is_refused(string table, int errorNumber)
    {
        await PostOneJournalAsync();

        await BankCoreDatabase.AssertRefusedAsync(
            () => db.ExecuteAsync($"TRUNCATE TABLE {table};"), errorNumber, table.Split('.')[1]);
    }

    // A salary credit of 75.00 to a new customer. Returns the journal_id.
    private async Task<long> PostOneJournalAsync()
    {
        var cash = await db.GlLedgerAccountIdAsync("1010");
        var customer = await db.NewCustomerLedgerAccountAsync();
        return await db.PostJournalAsync(BankCoreDatabase.NewKey(), "SALARY_CREDIT",
            new JournalLine(cash, 75.00m), new JournalLine(customer, -75.00m));
    }

    private async Task<string> LedgerAccountRowAsync(long ledgerAccountId) =>
        string.Join("\n", await db.StringsAsync(
            $"""
            SELECT CONCAT(ledger_account_id, '|', kind, '|', account_id, '|', gl_code)
            FROM ledger.ledger_account WHERE ledger_account_id = {ledgerAccountId};
            """));

    // The journal and its entries as one string, to compare before and after.
    // The key has a binary collation of its own, so it is converted to the
    // database's collation before UNION ALL puts it in one column with text
    // from ledger.entry; otherwise SQL Server reports a collation conflict.
    private async Task<string> LedgerRowsAsync(long journalId)
    {
        var rows = await db.StringsAsync(
            $"""
            SELECT CONCAT('journal|', journal_id, '|', journal_type, '|', business_date, '|', posted_at_utc, '|', idempotency_key COLLATE DATABASE_DEFAULT)
            FROM ledger.journal WHERE journal_id = {journalId}
            UNION ALL
            SELECT CONCAT('entry|', entry_id, '|', business_date, '|', ledger_account_id, '|', amount, '|', currency_code)
            FROM ledger.entry WHERE journal_id = {journalId};
            """);
        rows.Sort(StringComparer.Ordinal);
        return string.Join("\n", rows);
    }
}
