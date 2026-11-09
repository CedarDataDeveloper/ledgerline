namespace Ledgerline.Db.Tests;

// INV-03 and trap T1: a repeated idempotency key has no new effect. The
// second call returns the first call's journal_id and writes nothing, so a
// client can always retry safely (blueprint 7.6, ADR-009).
public class IdempotencyTests(BankCoreDatabase db) : IClassFixture<BankCoreDatabase>, IAsyncLifetime
{
    public Task InitializeAsync() => db.EnsureOpenBusinessDateAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_same_key_twice_gives_one_journal_and_the_same_id()
    {
        var cash = await db.GlLedgerAccountIdAsync("1010");
        var customer = await db.NewCustomerLedgerAccountAsync();
        var key = BankCoreDatabase.NewKey();
        JournalLine[] lines = [new JournalLine(cash, 100.00m), new JournalLine(customer, -100.00m)];

        var first = await db.PostJournalAsync(key, "SALARY_CREDIT", lines);
        var second = await db.PostJournalAsync(key, "SALARY_CREDIT", lines);

        Assert.Equal(first, second);
        Assert.Equal(1, await JournalsWithKeyAsync(key));
        Assert.Equal(100.00m, await db.CustomerBalanceAsync(customer)); // credited once, not twice
    }

    // The key alone decides: a repeat is not compared with the original.
    [Fact]
    public async Task A_repeat_with_different_lines_returns_the_original_and_writes_nothing()
    {
        var cash = await db.GlLedgerAccountIdAsync("1010");
        var customer = await db.NewCustomerLedgerAccountAsync();
        var key = BankCoreDatabase.NewKey();

        var first = await db.PostJournalAsync(key, "SALARY_CREDIT",
            new JournalLine(cash, 100.00m), new JournalLine(customer, -100.00m));
        var second = await db.PostJournalAsync(key, "SALARY_CREDIT",
            new JournalLine(cash, 999.00m), new JournalLine(customer, -999.00m));

        Assert.Equal(first, second);
        Assert.Equal(2, await db.ScalarAsync<int>($"SELECT COUNT(*) FROM ledger.entry WHERE journal_id = {first};"));
        Assert.Equal(100.00m, await db.CustomerBalanceAsync(customer));
    }

    // The key is checked before anything else, so a retry still gets its
    // original answer even if the retry's lines would now be refused.
    [Fact]
    public async Task A_repeat_whose_lines_would_be_refused_still_returns_the_original()
    {
        var cash = await db.GlLedgerAccountIdAsync("1010");
        var customer = await db.NewCustomerLedgerAccountAsync();
        var key = BankCoreDatabase.NewKey();

        var first = await db.PostJournalAsync(key, "SALARY_CREDIT",
            new JournalLine(cash, 100.00m), new JournalLine(customer, -100.00m));
        var second = await db.PostJournalAsync(key, "SALARY_CREDIT",
            new JournalLine(cash, 100.00m), new JournalLine(customer, -1.00m));

        Assert.Equal(first, second);
    }

    // @journal_id is an OUTPUT parameter, so a caller can pass a value in. A
    // stale value must not be taken for "this key already has a journal".
    [Fact]
    public async Task A_journal_id_passed_in_is_not_mistaken_for_an_existing_journal()
    {
        var cash = await db.GlLedgerAccountIdAsync("1010");
        var customer = await db.NewCustomerLedgerAccountAsync();
        var key = BankCoreDatabase.NewKey();

        var returned = await db.ScalarAsync<long>(
            $"""
            DECLARE @lines ledger.journal_line;
            INSERT INTO @lines (ledger_account_id, amount, currency_code)
            VALUES ({cash}, 20.00, 'AED'), ({customer}, -20.00, 'AED');

            DECLARE @journal_id bigint = 987654321;
            EXEC ledger.usp_post_journal
                @idempotency_key = N'{key}',
                @journal_type    = 'SALARY_CREDIT',
                @lines           = @lines,
                @journal_id      = @journal_id OUTPUT;
            SELECT @journal_id;
            """);

        Assert.Equal(1, await JournalsWithKeyAsync(key));
        Assert.Equal(await db.ScalarAsync<long>($"SELECT journal_id FROM ledger.journal WHERE idempotency_key = N'{key}';"), returned);
    }

    // Twenty retries arriving at the same moment: the application lock on
    // the key queues them, so the first writes the journal and the other
    // nineteen find it and return its id. None of them fails.
    [Fact]
    public async Task Twenty_simultaneous_calls_with_the_same_key_give_one_journal_and_the_same_id()
    {
        var cash = await db.GlLedgerAccountIdAsync("1010");
        var customer = await db.NewCustomerLedgerAccountAsync();
        var key = BankCoreDatabase.NewKey();
        JournalLine[] lines = [new JournalLine(cash, 50.00m), new JournalLine(customer, -50.00m)];

        var outcomes = await db.PostAllAtOnceAsync(
            Enumerable.Range(0, 20).Select(_ => (key, "SALARY_CREDIT", lines)).ToList());

        Assert.All(outcomes, outcome => Assert.Null(outcome.ErrorNumber));
        Assert.Single(outcomes.Select(outcome => outcome.JournalId).Distinct());
        Assert.Equal(1, await JournalsWithKeyAsync(key));
        Assert.Equal(50.00m, await db.CustomerBalanceAsync(customer));
    }

    // --- Keys are exact ------------------------------------------------------------
    // An idempotency key is an identifier: two keys are the same only if they
    // are the same characters (ADR-009).

    [Fact]
    public async Task Keys_that_differ_only_in_case_are_two_keys()
    {
        var lower = "case-" + Guid.NewGuid();
        var upper = lower.ToUpperInvariant();

        var first = await SalaryCreditAsync(lower);
        var second = await SalaryCreditAsync(upper);

        Assert.NotEqual(first, second);
        Assert.Equal(1, await JournalsWithKeyAsync(lower));
        Assert.Equal(1, await JournalsWithKeyAsync(upper));
    }

    [Theory]
    [InlineData("{0} ")]
    [InlineData(" {0}")]
    [InlineData("{0}\t")]
    public async Task A_key_padded_with_whitespace_is_refused(string format)
    {
        var key = string.Format(format, BankCoreDatabase.NewKey());

        await BankCoreDatabase.AssertRefusedAsync(() => SalaryCreditAsync(key), 50010);
    }

    [Fact]
    public async Task A_key_of_101_characters_is_refused()
    {
        var key = KeyOfLength(101);

        await BankCoreDatabase.AssertRefusedAsync(() => SalaryCreditAsync(key), 50010);
    }

    [Fact]
    public async Task A_key_of_100_characters_is_accepted()
    {
        var key = KeyOfLength(100);

        await SalaryCreditAsync(key);

        Assert.Equal(1, await JournalsWithKeyAsync(key));
    }

    // Without the length check, the 101-character key would be cut to its
    // first 100 characters on the way in, match the first journal, and get
    // that journal's id back: the second request would silently never post.
    [Fact]
    public async Task Two_keys_that_share_their_first_100_characters_do_not_collide()
    {
        var shortKey = KeyOfLength(100);
        var longKey = shortKey + "X";
        var customer = await db.NewCustomerLedgerAccountAsync();

        await SalaryCreditAsync(shortKey, customer);
        await BankCoreDatabase.AssertRefusedAsync(() => SalaryCreditAsync(longKey, customer), 50010);

        Assert.Equal(1, await db.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM ledger.journal WHERE idempotency_key LIKE N'{shortKey[..36]}%';"));
        Assert.Equal(25.00m, await db.CustomerBalanceAsync(customer));
    }

    // The table holds the rule too, for an insert that goes around the procedure.
    [Fact]
    public Task A_direct_insert_of_a_padded_key_is_refused_by_the_check_constraint() =>
        BankCoreDatabase.AssertRefusedAsync(
            () => db.ExecuteAsync(
                $"""
                INSERT INTO ledger.journal (journal_type, business_date, posted_at_utc, idempotency_key)
                VALUES ('FEE', '{BankCoreDatabase.OpenDate}', SYSUTCDATETIME(), N'{BankCoreDatabase.NewKey()} ');
                """),
            547, "ck_journal_idempotency_key");

    // The application lock is case-sensitive, and so is the column: ten
    // pairs of keys differing only in case, all at once, give twenty journals.
    [Fact]
    public async Task Simultaneous_calls_with_keys_that_differ_only_in_case_give_separate_journals()
    {
        var cash = await db.GlLedgerAccountIdAsync("1010");
        var customer = await db.NewCustomerLedgerAccountAsync();
        JournalLine[] lines = [new JournalLine(cash, 1.00m), new JournalLine(customer, -1.00m)];

        var keys = Enumerable.Range(0, 10)
            .Select(_ => "case-" + Guid.NewGuid())
            .SelectMany(key => new[] { key, key.ToUpperInvariant() })
            .ToList();

        var outcomes = await db.PostAllAtOnceAsync(keys.Select(key => (key, "SALARY_CREDIT", lines)).ToList());

        Assert.All(outcomes, outcome => Assert.Null(outcome.ErrorNumber));
        Assert.Equal(20, outcomes.Select(outcome => outcome.JournalId).Distinct().Count());
        Assert.Equal(20.00m, await db.CustomerBalanceAsync(customer));
    }

    // A fresh key of exactly the given length: a GUID padded with letters.
    private static string KeyOfLength(int length)
    {
        var start = BankCoreDatabase.NewKey();
        return start + new string('k', length - start.Length);
    }

    // A salary credit of 25.00 with the given key, to a new customer unless
    // one is named. Returns the journal_id.
    private async Task<long> SalaryCreditAsync(string key, long? customer = null)
    {
        var cash = await db.GlLedgerAccountIdAsync("1010");
        var credited = customer ?? await db.NewCustomerLedgerAccountAsync();
        return await db.PostJournalAsync(key, "SALARY_CREDIT",
            new JournalLine(cash, 25.00m), new JournalLine(credited, -25.00m));
    }

    private Task<int> JournalsWithKeyAsync(string key) =>
        db.ScalarAsync<int>($"SELECT COUNT(*) FROM ledger.journal WHERE idempotency_key = N'{key}';");
}
