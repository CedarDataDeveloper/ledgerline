namespace Ledgerline.Domain.Tests;

// core.account.account_id as its own type, so it cannot be mistaken for a
// ledger_account_id.
public class AccountIdTests
{
    [Fact]
    public void A_positive_id_is_accepted()
    {
        Assert.Equal(1000000001L, new AccountId(1000000001L).Value);
        Assert.Equal(new AccountId(1L), new AccountId(1L));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void Zero_and_negative_ids_are_refused(long value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AccountId(value));
    }
}
