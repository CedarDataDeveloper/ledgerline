namespace Ledgerline.Db.Tests;

// INV-08: postings use the open business date only. This class's database
// never opens a date, so the procedure has no date to stamp and must refuse.
public class NoOpenBusinessDateTests(BankCoreDatabase db) : IClassFixture<BankCoreDatabase>
{
    [Fact]
    public async Task Posting_with_no_open_business_date_is_refused_and_writes_nothing()
    {
        // Day-one capital, between two GL accounts, so no customer is needed.
        var cash = await db.GlLedgerAccountIdAsync("1010");
        var capital = await db.GlLedgerAccountIdAsync("3000");

        await BankCoreDatabase.AssertRefusedAsync(
            () => db.PostJournalAsync(BankCoreDatabase.NewKey(), "CAPITAL_INJECTION",
                new JournalLine(cash, 1_000_000.00m), new JournalLine(capital, -1_000_000.00m)),
            50009);

        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM ledger.journal;"));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM ledger.entry;"));
    }
}
