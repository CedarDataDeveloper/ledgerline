using Ledgerline.Domain;

namespace Ledgerline.Db.Tests;

// The shape of the ledger (blueprint 7.1 and 7.2): every account has exactly
// one ledger account, the chart of accounts is seeded, the business calendar
// allows one open date, and an entry's date always matches its journal's.
// Each refusal names the constraint or index that must refuse it.
public class LedgerStructureTests(BankCoreDatabase db) : IClassFixture<BankCoreDatabase>, IAsyncLifetime
{
    public Task InitializeAsync() => db.EnsureOpenBusinessDateAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // --- Ledger accounts for customer accounts -------------------------------------

    [Fact]
    public async Task A_new_account_gets_exactly_one_ledger_account()
    {
        var customerId = await db.InsertCustomerAsync();
        var accountId = await db.OpenAccountAsync(customerId, "SAVINGS_AED");

        Assert.Equal(1, await CustomerLedgerAccountsAsync(accountId));
    }

    // A trigger fires once per statement, so it must handle every row.
    [Fact]
    public async Task A_multi_row_insert_gives_each_account_its_own_ledger_account()
    {
        var customerId = await db.InsertCustomerAsync();
        var firstId = await db.NextAccountIdAsync();
        var secondId = await db.NextAccountIdAsync();

        await db.ExecuteAsync(
            $"""
            INSERT INTO core.account (account_id, customer_id, product_code, iban, currency_code, status, opened_on)
            VALUES ({firstId},  {customerId}, 'SAVINGS_AED', '{SyntheticIban.Generate(firstId)}',  'AED', 'OPEN', '2026-11-09'),
                   ({secondId}, {customerId}, 'SAVINGS_AED', '{SyntheticIban.Generate(secondId)}', 'AED', 'OPEN', '2026-11-09');
            """);

        Assert.Equal(1, await CustomerLedgerAccountsAsync(firstId));
        Assert.Equal(1, await CustomerLedgerAccountsAsync(secondId));
    }

    // --- The chart of accounts (blueprint 7.2) -----------------------------------

    [Fact]
    public async Task The_five_gl_accounts_are_seeded()
    {
        var accounts = await db.StringsAsync(
            "SELECT gl_code + '|' + name + '|' + account_type FROM ledger.gl_account ORDER BY gl_code;");

        Assert.Equal(new[]
        {
            "1010|Cash at central bank|ASSET",
            "1250|Outbound payments clearing|CLEARING",
            "3000|Share capital|EQUITY",
            "4100|Fee income|INCOME",
            "5100|Interest expense|EXPENSE",
        }, accounts);
    }

    [Fact]
    public async Task Each_gl_account_has_exactly_one_gl_ledger_account()
    {
        var ledgerAccounts = await db.StringsAsync(
            "SELECT gl_code FROM ledger.ledger_account WHERE kind = 'GL' ORDER BY gl_code;");

        Assert.Equal(new[] { "1010", "1250", "3000", "4100", "5100" }, ledgerAccounts);
    }

    // 9000 is free and LIABILITY fits VARCHAR(10), so only the check can refuse it.
    [Fact]
    public Task An_unknown_gl_account_type_is_refused() =>
        BankCoreDatabase.AssertRefusedAsync(
            () => db.ExecuteAsync(
                "INSERT INTO ledger.gl_account (gl_code, name, account_type) VALUES ('9000', 'Test', 'LIABILITY');"),
            547, "ck_gl_account_account_type");

    // --- Ledger account rules ----------------------------------------------------
    // Rows with neither an account nor a GL code, so the two unique indexes
    // cannot fire first.

    [Fact]
    public Task An_unknown_ledger_account_kind_is_refused() =>
        BankCoreDatabase.AssertRefusedAsync(
            () => db.ExecuteAsync("INSERT INTO ledger.ledger_account (kind) VALUES ('SUSPENSE');"),
            547, "ck_ledger_account_kind");

    [Theory]
    [InlineData("CUSTOMER")]
    [InlineData("GL")]
    public Task A_ledger_account_without_its_link_is_refused(string kind) =>
        BankCoreDatabase.AssertRefusedAsync(
            () => db.ExecuteAsync($"INSERT INTO ledger.ledger_account (kind) VALUES ('{kind}');"),
            547, "ck_ledger_account_link");

    [Fact]
    public async Task A_second_ledger_account_for_the_same_account_is_refused()
    {
        var customerId = await db.InsertCustomerAsync();
        var accountId = await db.OpenAccountAsync(customerId, "SAVINGS_AED");

        await BankCoreDatabase.AssertRefusedAsync(
            () => db.ExecuteAsync($"INSERT INTO ledger.ledger_account (kind, account_id) VALUES ('CUSTOMER', {accountId});"),
            2601, "ux_ledger_account_account_id");
    }

    [Fact]
    public Task A_second_ledger_account_for_the_same_gl_code_is_refused() =>
        BankCoreDatabase.AssertRefusedAsync(
            () => db.ExecuteAsync("INSERT INTO ledger.ledger_account (kind, gl_code) VALUES ('GL', '1010');"),
            2601, "ux_ledger_account_gl_code");

    // --- Business calendar (INV-08) ------------------------------------------------

    [Fact]
    public Task A_second_open_business_date_is_refused() =>
        BankCoreDatabase.AssertRefusedAsync(
            () => db.ExecuteAsync("INSERT INTO ops.business_calendar (business_date, status) VALUES ('2026-11-10', 'OPEN');"),
            2601, "ux_business_calendar_one_open");

    // HOLIDAY fits VARCHAR(12), so only the check can refuse it.
    [Fact]
    public Task An_unknown_business_date_status_is_refused() =>
        BankCoreDatabase.AssertRefusedAsync(
            () => db.ExecuteAsync("INSERT INTO ops.business_calendar (business_date, status) VALUES ('2026-11-11', 'HOLIDAY');"),
            547, "ck_business_calendar_status");

    // --- Journals and entries ---------------------------------------------------

    // The composite foreign key on (journal_id, business_date) ties every
    // entry to its journal's date. Both lines balance, and the insert is
    // refused before the balancing trigger would run.
    [Fact]
    public async Task An_entry_dated_differently_from_its_journal_is_refused()
    {
        var cash = await db.GlLedgerAccountIdAsync("1010");
        var capital = await db.GlLedgerAccountIdAsync("3000");
        var journalId = await db.PostJournalAsync(BankCoreDatabase.NewKey(), "CAPITAL_INJECTION",
            new JournalLine(cash, 10.00m), new JournalLine(capital, -10.00m));

        await BankCoreDatabase.AssertRefusedAsync(
            () => db.ExecuteAsync(
                $"""
                INSERT INTO ledger.entry (journal_id, business_date, ledger_account_id, amount, currency_code)
                VALUES ({journalId}, '2026-11-08', {cash},  1.00, 'AED'),
                       ({journalId}, '2026-11-08', {capital}, -1.00, 'AED');
                """),
            547, "fk_entry_journal");
    }

    // A journal can be reversed at most once. Reversals arrive in LL-009;
    // the rule is in the table now.
    [Fact]
    public async Task A_second_reversal_of_the_same_journal_is_refused()
    {
        var cash = await db.GlLedgerAccountIdAsync("1010");
        var capital = await db.GlLedgerAccountIdAsync("3000");
        var original = await db.PostJournalAsync(BankCoreDatabase.NewKey(), "CAPITAL_INJECTION",
            new JournalLine(cash, 10.00m), new JournalLine(capital, -10.00m));

        string InsertReversal() =>
            $"""
            INSERT INTO ledger.journal (journal_type, business_date, posted_at_utc, idempotency_key, reverses_journal_id)
            VALUES ('REVERSAL', '{BankCoreDatabase.OpenDate}', SYSUTCDATETIME(), N'{BankCoreDatabase.NewKey()}', {original});
            """;

        await db.ExecuteAsync(InsertReversal());

        await BankCoreDatabase.AssertRefusedAsync(
            () => db.ExecuteAsync(InsertReversal()), 2601, "ux_journal_reverses_journal_id");
    }

    private Task<int> CustomerLedgerAccountsAsync(long accountId) =>
        db.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM ledger.ledger_account WHERE kind = 'CUSTOMER' AND account_id = {accountId};");
}
