using System.Globalization;
using System.Reflection;

namespace Ledgerline.Domain.Tests;

// Money: a decimal amount in one currency that always fits DECIMAL(19,4)
// (blueprint 6 and 7.5). Attributes cannot hold decimal values, so the
// theories pass amounts as text and parse them with the invariant culture.
public class MoneyTests
{
    [Fact]
    public void Four_decimal_places_are_accepted()
    {
        var money = new Money(0.0001m, Currency.Aed);

        Assert.Equal(0.0001m, money.Amount);
        Assert.Equal(Currency.Aed, money.Currency);
    }

    [Theory]
    [InlineData("0.00001")]
    [InlineData("1.23456")]
    [InlineData("-0.00001")]
    public void A_fifth_decimal_place_is_refused(string amount)
    {
        var error = Assert.Throws<DomainRuleException>(() => new Money(Parse(amount), Currency.Aed));

        Assert.Equal(DomainRule.MoneyTooManyDecimalPlaces, error.Rule);
    }

    // The rule is about the value, not about how it was written: 1.50000 is
    // exactly 1.5.
    [Fact]
    public void Trailing_zeros_beyond_four_places_are_fine()
    {
        var money = new Money(1.50000m, Currency.Aed);

        Assert.Equal(1.5m, money.Amount);
    }

    // The largest amount DECIMAL(19,4) holds, on both sides of zero.
    [Theory]
    [InlineData("999999999999999.9999")]
    [InlineData("-999999999999999.9999")]
    public void The_largest_storable_amount_is_accepted(string amount)
    {
        Assert.Equal(Parse(amount), new Money(Parse(amount), Currency.Aed).Amount);
    }

    [Theory]
    [InlineData("1000000000000000")]
    [InlineData("-1000000000000000")]
    public void One_more_than_the_largest_storable_amount_is_refused(string amount)
    {
        var error = Assert.Throws<DomainRuleException>(() => new Money(Parse(amount), Currency.Aed));

        Assert.Equal(DomainRule.MoneyOutOfRange, error.Rule);
    }

    [Fact]
    public void Add_subtract_and_negate()
    {
        var hundred = new Money(100.25m, Currency.Aed);
        var change = new Money(0.75m, Currency.Aed);

        Assert.Equal(new Money(101.00m, Currency.Aed), hundred + change);
        Assert.Equal(new Money(99.50m, Currency.Aed), hundred - change);
        Assert.Equal(new Money(-100.25m, Currency.Aed), -hundred);
    }

    // A sum that no longer fits DECIMAL(19,4) is refused like any other amount.
    [Fact]
    public void A_sum_beyond_the_largest_storable_amount_is_refused()
    {
        var largest = new Money(Money.MaxAmount, Currency.Aed);

        var error = Assert.Throws<DomainRuleException>(() => largest + new Money(0.0001m, Currency.Aed));

        Assert.Equal(DomainRule.MoneyOutOfRange, error.Rule);
    }

    [Fact]
    public void Comparisons_in_one_currency()
    {
        var small = new Money(1.00m, Currency.Aed);
        var large = new Money(2.00m, Currency.Aed);

        Assert.True(small < large);
        Assert.True(large > small);
        Assert.True(small <= large);
        Assert.True(small <= new Money(1.00m, Currency.Aed));
        Assert.True(large >= small);
        Assert.False(small > large);
    }

    // There is no meaningful answer to 1 AED + 1 USD, or to 1 AED < 1 USD.
    [Theory]
    [InlineData("+")]
    [InlineData("-")]
    [InlineData("<")]
    [InlineData(">")]
    [InlineData("<=")]
    [InlineData(">=")]
    public void Operators_refuse_two_currencies(string op)
    {
        var dirham = new Money(1.00m, Currency.Aed);
        var dollar = new Money(1.00m, Currency.Usd);

        Action apply = op switch
        {
            "+" => () => _ = dirham + dollar,
            "-" => () => _ = dirham - dollar,
            "<" => () => _ = dirham < dollar,
            ">" => () => _ = dirham > dollar,
            "<=" => () => _ = dirham <= dollar,
            ">=" => () => _ = dirham >= dollar,
            _ => throw new ArgumentOutOfRangeException(nameof(op)),
        };

        var error = Assert.Throws<DomainRuleException>(apply);
        Assert.Equal(DomainRule.CurrencyMismatch, error.Rule);
    }

    // decimal compares values, so 1.0 and 1.00 are the same amount. Equal
    // values must also give equal hash codes, or a dictionary keyed on Money
    // would treat them as two keys.
    [Fact]
    public void Equal_amounts_written_differently_are_equal_and_hash_alike()
    {
        var one = new Money(1.0m, Currency.Aed);
        var alsoOne = new Money(1.00m, Currency.Aed);

        Assert.Equal(one, alsoOne);
        Assert.True(one == alsoOne);
        Assert.Equal(one.GetHashCode(), alsoOne.GetHashCode());
    }

    // Equality answers false for two currencies; unlike the operators above it
    // does not throw.
    [Fact]
    public void The_same_amount_in_two_currencies_is_not_equal()
    {
        var dirham = new Money(1.00m, Currency.Aed);
        var dollar = new Money(1.00m, Currency.Usd);

        Assert.NotEqual(dirham, dollar);
        Assert.False(dirham == dollar);
        Assert.True(dirham != dollar);
    }

    [Fact]
    public void Zero_and_the_sign_properties()
    {
        Assert.True(Money.Zero(Currency.Aed).IsZero);
        Assert.Equal(Currency.Usd, Money.Zero(Currency.Usd).Currency);

        Assert.True(new Money(0.01m, Currency.Aed).IsPositive);
        Assert.False(new Money(0.01m, Currency.Aed).IsNegative);
        Assert.True(new Money(-0.01m, Currency.Aed).IsNegative);
        Assert.False(Money.Zero(Currency.Aed).IsPositive);
        Assert.False(Money.Zero(Currency.Aed).IsNegative);
    }

    [Theory]
    [InlineData("12.34", true)]
    [InlineData("12.3400", true)]
    [InlineData("12", true)]
    [InlineData("12.345", false)]
    [InlineData("0.0001", false)]
    public void IsWholeMinorUnits_means_whole_fils(string amount, bool expected)
    {
        Assert.Equal(expected, new Money(Parse(amount), Currency.Aed).IsWholeMinorUnits);
    }

    // Half away from zero, in both directions. Banker's rounding would give
    // 0.00 and -0.02 here.
    [Theory]
    [InlineData("0.005", "0.01")]
    [InlineData("-0.025", "-0.03")]
    [InlineData("41.1049", "41.10")]
    public void RoundToMinorUnit_rounds_half_away_from_zero(string amount, string expected)
    {
        var rounded = new Money(Parse(amount), Currency.Aed).RoundToMinorUnit();

        Assert.Equal(new Money(Parse(expected), Currency.Aed), rounded);
    }

    [Theory]
    [InlineData("1234.5", "AED 1234.5000")]
    [InlineData("-0.01", "AED -0.0100")]
    [InlineData("0", "AED 0.0000")]
    public void ToString_is_the_code_and_four_decimals(string amount, string expected)
    {
        Assert.Equal(expected, new Money(Parse(amount), Currency.Aed).ToString());
    }

    // de-DE writes 1.234,5 and ar-SA may use Arabic digits; the text must not
    // depend on the computer it runs on.
    [Theory]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    public void ToString_ignores_the_current_culture(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);

            Assert.Equal("AED 1234.5000", new Money(1234.5m, Currency.Aed).ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void A_null_currency_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => new Money(1m, null!));
    }

    // Floating point must never reach money. C# has no implicit conversion
    // from double to decimal, and this checks that no public member of Money
    // offers a float or double door either: no constructor, method, operator
    // or property that takes or returns one.
    [Fact]
    public void No_public_member_takes_or_returns_float_or_double()
    {
        Type[] floating = [typeof(float), typeof(double)];
        const BindingFlags everyPublic = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

        var offending = new List<string>();

        foreach (var method in typeof(Money).GetMethods(everyPublic))
        {
            if (floating.Contains(method.ReturnType)
                || method.GetParameters().Any(parameter => floating.Contains(parameter.ParameterType)))
            {
                offending.Add(method.ToString()!);
            }
        }

        offending.AddRange(typeof(Money).GetConstructors(everyPublic)
            .Where(constructor => constructor.GetParameters().Any(parameter => floating.Contains(parameter.ParameterType)))
            .Select(constructor => constructor.ToString()!));

        offending.AddRange(typeof(Money).GetProperties(everyPublic)
            .Where(property => floating.Contains(property.PropertyType))
            .Select(property => property.ToString()!));

        offending.AddRange(typeof(Money).GetFields(everyPublic)
            .Where(field => floating.Contains(field.FieldType))
            .Select(field => field.ToString()!));

        Assert.Empty(offending);

        // Proves the search sees operators: without this, a broken search
        // would pass forever.
        Assert.Contains(typeof(Money).GetMethods(everyPublic), method => method.Name == "op_Addition");
    }

    private static decimal Parse(string amount) => decimal.Parse(amount, CultureInfo.InvariantCulture);
}
