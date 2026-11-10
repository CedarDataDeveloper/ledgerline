namespace Ledgerline.Domain;

// An ISO 4217 currency the bank knows. The constructor is private, so the only
// currencies that exist are the ones listed here: nobody can invent "XXX".
//
// MinorUnits is the number of decimal places of the currency's smallest coin.
// AED has 100 fils to the dirham, so 2.
public sealed record Currency
{
    // The bank's home currency. v1 posts in AED only (ck_entry_currency_code).
    public static Currency Aed { get; } = new("AED", 2);

    // Not posted in v1. It exists because v2 adds USD investing, and because
    // tests need a second currency to prove that currencies are kept apart.
    public static Currency Usd { get; } = new("USD", 2);

    private Currency(string code, int minorUnits)
    {
        Code = code;
        MinorUnits = minorUnits;
    }

    public string Code { get; }

    public int MinorUnits { get; }

    // The known currency for an exact upper-case code. Lower-case "aed" is
    // refused on purpose: the database's case-insensitive collation would
    // accept it, but the domain is stricter, so only one spelling ever
    // reaches the ledger.
    public static Currency FromCode(string? code) => code switch
    {
        "AED" => Aed,
        "USD" => Usd,
        _ => throw new DomainRuleException(DomainRule.CurrencyUnknown, $"'{code}' is not a currency the bank knows."),
    };

    public override string ToString() => Code;
}
