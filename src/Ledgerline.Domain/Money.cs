using System.Globalization;

namespace Ledgerline.Domain;

// An amount of money in one currency (blueprint 6 and 7.5).
//
// The amount is a decimal, never a float or double: binary floating point
// cannot hold 0.10 exactly, so sums drift by fractions of a fils. C# has no
// implicit conversion from double to decimal, so a double cannot reach the
// constructor by accident; it would need an explicit cast, which a reviewer
// would see. A test checks that no public member of Money takes or returns a
// float or double.
//
// Every amount fits the database's DECIMAL(19,4): at most four decimal places
// and at most 15 digits before the decimal point. A Money that could not be
// stored is refused here, at the moment it is made.
//
// A record gives value equality: 1.0 AED equals 1.00 AED (decimal compares
// values, not trailing zeros), and AED never equals USD. The properties are
// get-only, so nothing can change a Money after the constructor has checked it.
public sealed record Money
{
    // The largest amount DECIMAL(19,4) can hold: 15 digits, then 4 decimals.
    public const decimal MaxAmount = 999_999_999_999_999.9999m;

    private const int StoredDecimalPlaces = 4;

    public Money(decimal amount, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);

        // Checked by value, not by how the number was written: 1.50000m has
        // five digits after the point but is exactly 1.5, so it is fine;
        // 0.00001m really has a fifth decimal place, so it is refused.
        if (amount != Rounding.HalfAwayFromZero(amount, StoredDecimalPlaces))
        {
            throw new DomainRuleException(
                DomainRule.MoneyTooManyDecimalPlaces,
                $"{amount.ToString(CultureInfo.InvariantCulture)} has more than four decimal places.");
        }

        if (Math.Abs(amount) > MaxAmount)
        {
            throw new DomainRuleException(
                DomainRule.MoneyOutOfRange,
                $"{amount.ToString(CultureInfo.InvariantCulture)} does not fit DECIMAL(19,4).");
        }

        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public Currency Currency { get; }

    public bool IsZero => Amount == 0m;

    public bool IsPositive => Amount > 0m;

    public bool IsNegative => Amount < 0m;

    // True when the amount is a whole number of the currency's smallest coin:
    // 12.34 AED is 1234 fils, but 12.345 AED is not a whole number of fils.
    public bool IsWholeMinorUnits => Amount == Rounding.HalfAwayFromZero(Amount, Currency.MinorUnits);

    public static Money Zero(Currency currency) => new(0m, currency);

    // Rounds to the currency's smallest coin, half away from zero: 0.005 AED
    // becomes 0.01 AED.
    public Money RoundToMinorUnit() =>
        new(Rounding.HalfAwayFromZero(Amount, Currency.MinorUnits), Currency);

    // The arithmetic and comparison operators refuse two currencies: there is
    // no meaningful answer to 100 AED + 100 USD, or to 100 AED < 100 USD.
    // Equality is different: 100 AED == 100 USD simply answers false.
    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount - right.Amount, left.Currency);
    }

    public static Money operator -(Money money)
    {
        ArgumentNullException.ThrowIfNull(money);
        return new Money(-money.Amount, money.Currency);
    }

    public static bool operator <(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount < right.Amount;
    }

    public static bool operator >(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount > right.Amount;
    }

    public static bool operator <=(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount <= right.Amount;
    }

    public static bool operator >=(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount >= right.Amount;
    }

    // Always the same text whatever the computer's language settings, for
    // example "AED 1234.5000": the code, a space, and the amount with four
    // decimals and a full stop. Under de-DE the default would use a comma.
    public override string ToString() =>
        $"{Currency.Code} {Amount.ToString("F4", CultureInfo.InvariantCulture)}";

    private static void EnsureSameCurrency(Money left, Money right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (left.Currency != right.Currency)
        {
            throw new DomainRuleException(
                DomainRule.CurrencyMismatch,
                $"Cannot combine {left.Currency.Code} with {right.Currency.Code}.");
        }
    }
}
