using System.Globalization;

namespace Ledgerline.Domain.Tests;

// Half away from zero, the one rounding rule (blueprint 7.5). The same
// vectors are checked against SQL Server's ROUND in DomainParityTests.
public class RoundingTests
{
    // Banker's rounding (half to even, C#'s default) gives a different answer
    // on the first seven: 0.00, 0.02, 0.04, 0.00, -0.02, 41.10 and -41.10.
    // 2.675 is here because as a double it is really 2.67499999..., so
    // floating point rounds it to 2.67; as a decimal it is exact and rounds
    // to 2.68 under either rule.
    [Theory]
    [InlineData("0.005", "0.01")]
    [InlineData("0.025", "0.03")]
    [InlineData("0.045", "0.05")]
    [InlineData("-0.005", "-0.01")]
    [InlineData("-0.025", "-0.03")]
    [InlineData("41.105", "41.11")]
    [InlineData("-41.105", "-41.11")]
    [InlineData("2.675", "2.68")]
    [InlineData("41.104999", "41.10")]
    public void HalfAwayFromZero_to_two_places(string value, string expected)
    {
        Assert.Equal(Parse(expected), Rounding.HalfAwayFromZero(Parse(value), 2));
    }

    // A daily accrual kept at six decimal places (blueprint 7.5), rounded to
    // the fils when it is capitalised. Not a midpoint: this is the ordinary
    // case the rule must not disturb.
    [Fact]
    public void A_six_decimal_accrual_rounds_to_the_fils()
    {
        Assert.Equal(41.10m, Rounding.HalfAwayFromZero(41.095890m, 2));
    }

    [Fact]
    public void HalfAwayFromZero_to_six_places()
    {
        Assert.Equal(0.000001m, Rounding.HalfAwayFromZero(0.0000005m, 6));
        Assert.Equal(-0.000001m, Rounding.HalfAwayFromZero(-0.0000005m, 6));
    }

    private static decimal Parse(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
