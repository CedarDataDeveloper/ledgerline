namespace Ledgerline.Db.Tests;

// Postings that run at the same moment. The procedure locks every ledger
// account in the journal with UPDLOCK, in ascending ledger_account_id order
// (ADR-021): two postings that share an account queue one behind the other,
// so the overdraft check always sees the latest balance, and because every
// posting takes its locks in the same order, they never deadlock.
public class PostingConcurrencyTests(BankCoreDatabase db) : IClassFixture<BankCoreDatabase>, IAsyncLifetime
{
    // SQL Server's error number for a deadlock victim.
    private const int Deadlock = 1205;

    public Task InitializeAsync() => db.EnsureOpenBusinessDateAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public Task Twenty_simultaneous_payments_of_60_from_100_let_exactly_one_through() =>
        RacingPaymentsAsync(db);

    [Fact]
    public Task Simultaneous_transfers_in_opposite_directions_never_deadlock() =>
        OppositeDirectionsAsync(db);

    // The two scenarios are shared with ReadCommittedSnapshotTests, which
    // runs them in a database with read committed snapshot on.

    // Twenty payments race for the same money. Without the lock, several
    // could read 100.00 before any of them wrote, and all would pass the
    // overdraft check.
    internal static async Task RacingPaymentsAsync(BankCoreDatabase db)
    {
        var payer = await db.NewCustomerLedgerAccountAsync();
        var payee = await db.NewCustomerLedgerAccountAsync();
        await db.FundAsync(payer, 100.00m);

        var outcomes = await db.PostAllAtOnceAsync(
            Enumerable.Range(0, 20)
                .Select(_ => (BankCoreDatabase.NewKey(), "TRANSFER",
                    new[] { new JournalLine(payer, 60.00m), new JournalLine(payee, -60.00m) }))
                .ToList());

        Assert.Single(outcomes, outcome => outcome.JournalId is not null);
        Assert.Equal(19, outcomes.Count(outcome => outcome.ErrorNumber == 50006));
        Assert.Equal(40.00m, await db.CustomerBalanceAsync(payer));
        Assert.Equal(60.00m, await db.CustomerBalanceAsync(payee));
    }

    // Transfers both ways between the same two accounts at once: A to B
    // locks A then B, and so does B to A, because the order is by
    // ledger_account_id, not by which side is debited.
    internal static async Task OppositeDirectionsAsync(BankCoreDatabase db)
    {
        var a = await db.NewCustomerLedgerAccountAsync();
        var b = await db.NewCustomerLedgerAccountAsync();
        await db.FundAsync(a, 1_000.00m);
        await db.FundAsync(b, 1_000.00m);

        var postings = Enumerable.Range(0, 60)
            .Select(i => i % 2 == 0
                ? (BankCoreDatabase.NewKey(), "TRANSFER", new[] { new JournalLine(a, 1.00m), new JournalLine(b, -1.00m) })
                : (BankCoreDatabase.NewKey(), "TRANSFER", new[] { new JournalLine(b, 2.00m), new JournalLine(a, -2.00m) }))
            .ToList();

        var outcomes = await db.PostAllAtOnceAsync(postings);

        Assert.DoesNotContain(outcomes, outcome => outcome.ErrorNumber == Deadlock);
        Assert.All(outcomes, outcome => Assert.Null(outcome.ErrorNumber));

        // 30 transfers of 1.00 from A to B and 30 of 2.00 back.
        Assert.Equal(1_030.00m, await db.CustomerBalanceAsync(a));
        Assert.Equal(970.00m, await db.CustomerBalanceAsync(b));
        Assert.Equal(2_000.00m, await db.CustomerBalanceAsync(a) + await db.CustomerBalanceAsync(b));
    }
}
