using Ledgerline.Domain;

namespace Ledgerline.Db.Tests;

// The IBAN check and the ledger must not depend on the server's collation. This server uses
// SQL_Latin1_General_CP1_CI_AS; many others use a Windows collation, under
// which LIKE '[0-9]' also matches characters such as the superscript two (²).
// Each test here gets its own database created with a Windows collation, then
// migrated with the real BankCore scripts, and dropped afterwards.
public class WindowsCollationTests : IAsyncLifetime
{
    private const string WindowsCollation = "Latin1_General_100_CI_AS";

    private readonly BankCoreDatabase _db = new() { Collation = WindowsCollation };

    public Task InitializeAsync() => _db.InitializeAsync();

    public Task DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task A_generated_iban_is_accepted()
    {
        Assert.Equal(WindowsCollation, await _db.DatabaseCollationAsync());

        var customerId = await _db.InsertCustomerAsync();
        await _db.OpenAccountAsync(customerId, "SAVINGS_AED");
    }

    // The ledger under a Windows collation: a balanced journal posts, and a
    // repeat of its key returns the same journal.
    [Fact]
    public async Task A_journal_posts_and_a_repeat_of_its_key_returns_the_same_id()
    {
        Assert.Equal(WindowsCollation, await _db.DatabaseCollationAsync());
        await _db.EnsureOpenBusinessDateAsync();

        var cash = await _db.GlLedgerAccountIdAsync("1010");
        var customer = await _db.NewCustomerLedgerAccountAsync();
        var key = BankCoreDatabase.NewKey();
        JournalLine[] lines = [new JournalLine(cash, 80.00m), new JournalLine(customer, -80.00m)];

        var first = await _db.PostJournalAsync(key, "SALARY_CREDIT", lines);
        var second = await _db.PostJournalAsync(key, "SALARY_CREDIT", lines);

        Assert.Equal(first, second);
        Assert.Equal(80.00m, await _db.CustomerBalanceAsync(customer));
    }

    [Fact]
    public async Task An_iban_with_a_superscript_digit_is_refused_by_the_iban_check()
    {
        Assert.Equal(WindowsCollation, await _db.DatabaseCollationAsync());

        var customerId = await _db.InsertCustomerAsync();
        var accountId = await _db.NextAccountIdAsync();
        var iban = SyntheticIban.Generate(accountId);
        var withSuperscript = iban[..20] + "²" + iban[21..];

        await BankCoreDatabase.AssertRefusedAsync(
            () => _db.InsertAccountAsync(accountId, customerId, "SAVINGS_AED", withSuperscript), 547, "ck_account_iban");
    }
}
