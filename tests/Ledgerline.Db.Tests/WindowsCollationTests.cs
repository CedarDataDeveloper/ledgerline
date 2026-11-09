using Ledgerline.Domain;

namespace Ledgerline.Db.Tests;

// The IBAN check must not depend on the server's collation. This server uses
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
