namespace Ledgerline.Domain.Tests;

// The currencies the bank knows, and nothing else.
public class CurrencyTests
{
    [Fact]
    public void FromCode_returns_the_known_currencies()
    {
        Assert.Same(Currency.Aed, Currency.FromCode("AED"));
        Assert.Same(Currency.Usd, Currency.FromCode("USD"));
    }

    // Lower case is refused on purpose: only one spelling reaches the ledger.
    [Theory]
    [InlineData("aed")]
    [InlineData("XXX")]
    [InlineData("")]
    [InlineData(" AED")]
    [InlineData(null)]
    public void FromCode_refuses_anything_else(string? code)
    {
        var error = Assert.Throws<DomainRuleException>(() => Currency.FromCode(code));

        Assert.Equal(DomainRule.CurrencyUnknown, error.Rule);
    }

    // 100 fils to the dirham, 100 cents to the dollar.
    [Fact]
    public void Both_currencies_have_two_minor_unit_places()
    {
        Assert.Equal(2, Currency.Aed.MinorUnits);
        Assert.Equal(2, Currency.Usd.MinorUnits);
    }

    [Fact]
    public void Code_and_ToString_give_the_iso_code()
    {
        Assert.Equal("AED", Currency.Aed.Code);
        Assert.Equal("USD", Currency.Usd.ToString());
        Assert.NotEqual(Currency.Aed, Currency.Usd);
    }
}
