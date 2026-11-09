namespace Ledgerline.Db.Tests;

// Azure SQL and Fabric turn read committed snapshot on by default. Under it,
// a plain read sees the last committed version instead of waiting for a
// lock, so the posting procedure's safety must come from the locks it asks
// for (UPDLOCK, REPEATABLEREAD, the application lock), not from plain reads
// happening to wait. This class runs the two concurrency scenarios in a
// database with the setting on.
public class ReadCommittedSnapshotTests : IAsyncLifetime
{
    private readonly BankCoreDatabase _db = new() { ReadCommittedSnapshot = true };

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await _db.EnsureOpenBusinessDateAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task Racing_payments_let_exactly_one_through_under_read_committed_snapshot()
    {
        Assert.True(await IsReadCommittedSnapshotOnAsync());

        await PostingConcurrencyTests.RacingPaymentsAsync(_db);
    }

    [Fact]
    public async Task Opposite_transfers_never_deadlock_under_read_committed_snapshot()
    {
        Assert.True(await IsReadCommittedSnapshotOnAsync());

        await PostingConcurrencyTests.OppositeDirectionsAsync(_db);
    }

    private Task<bool> IsReadCommittedSnapshotOnAsync() =>
        _db.ScalarAsync<bool>("SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME();");
}
