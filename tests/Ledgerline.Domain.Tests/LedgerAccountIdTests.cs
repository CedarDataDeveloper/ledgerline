namespace Ledgerline.Domain.Tests;

// ledger.ledger_account.ledger_account_id as its own type, so it cannot be
// mistaken for an account_id.
public class LedgerAccountIdTests
{
    [Fact]
    public void A_positive_id_is_accepted()
    {
        Assert.Equal(1L, new LedgerAccountId(1L).Value);
        Assert.Equal(new LedgerAccountId(7L), new LedgerAccountId(7L));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Zero_and_negative_ids_are_refused(long value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LedgerAccountId(value));
    }
}
