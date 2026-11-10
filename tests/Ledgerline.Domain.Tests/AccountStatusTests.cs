namespace Ledgerline.Domain.Tests;

// The codes core.account.status stores. DomainParityTests checks that this
// list equals ck_account_status.
public class AccountStatusTests
{
    [Theory]
    [InlineData(AccountStatus.Open, "OPEN")]
    [InlineData(AccountStatus.Frozen, "FROZEN")]
    [InlineData(AccountStatus.Closed, "CLOSED")]
    public void ToCode_gives_the_database_code(AccountStatus status, string expected)
    {
        Assert.Equal(expected, status.ToCode());
    }

    [Fact]
    public void ToCode_refuses_an_undefined_value()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((AccountStatus)99).ToCode());
    }
}
