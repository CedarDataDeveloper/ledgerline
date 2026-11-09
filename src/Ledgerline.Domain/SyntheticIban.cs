using System.Globalization;

namespace Ledgerline.Domain;

// Builds and checks Ledgerline's synthetic UAE IBANs (blueprint 7.1).
//
// A UAE IBAN has 23 characters: AE, two check digits, a 3-digit bank code and
// a 16-digit account number. Ledgerline uses the fictional bank code 999, so
// none of its IBANs can belong to a real bank.
//
// The check digits follow the international mod-97 rule (ISO 13616). To check
// an IBAN, move the first four characters to the end, turn each letter into a
// number (A = 10, ..., E = 14, so AE becomes 1014), read the result as one big
// number, and divide by 97: a valid IBAN leaves remainder 1. The table
// core.account checks the same rule (ck_account_iban).
public static class SyntheticIban
{
    public const string BankCode = "999";

    // The 16-digit account number field holds at most this many.
    private const long MaxAccountNumber = 9_999_999_999_999_999;

    // AE as numbers: A = 10, E = 14.
    private const string CountryCodeDigits = "1014";

    public static string Generate(long accountNumber)
    {
        if (accountNumber < 1 || accountNumber > MaxAccountNumber)
        {
            throw new ArgumentOutOfRangeException(
                nameof(accountNumber), accountNumber, "An account number must be between 1 and 9,999,999,999,999,999.");
        }

        // Left-padded with zeros to 16 digits, e.g. 1000000001 -> 0000001000000001.
        var accountDigits = accountNumber.ToString("D16", CultureInfo.InvariantCulture);

        // Choose the check digits so that the finished IBAN leaves remainder 1:
        // put 00 where the check digits will go, take the remainder, and
        // subtract it from 98. The result is always between 2 and 98.
        var remainder = Mod97(BankCode + accountDigits + CountryCodeDigits + "00");
        var checkDigits = (98 - remainder).ToString("D2", CultureInfo.InvariantCulture);

        return "AE" + checkDigits + BankCode + accountDigits;
    }

    // True only for a well-formed Ledgerline IBAN. Never throws, so it is safe
    // on any input.
    public static bool IsValid(string? iban)
    {
        if (iban is null || iban.Length != 23 || !iban.StartsWith("AE", StringComparison.Ordinal))
        {
            return false;
        }

        // Every character after AE must be 0 to 9. char.IsDigit is not used
        // because it also accepts digits from other scripts, such as Arabic-Indic.
        for (var i = 2; i < iban.Length; i++)
        {
            if (iban[i] < '0' || iban[i] > '9')
            {
                return false;
            }
        }

        if (iban.Substring(4, 3) != BankCode)
        {
            return false;
        }

        // Move AE and the check digits to the end: bank code and account
        // number first, then 1014 for AE, then the two check digits.
        var rearranged = iban[4..] + CountryCodeDigits + iban[2..4];
        return Mod97(rearranged) == 1;
    }

    // The remainder after dividing a long string of digits by 97.
    //
    // The number has 25 digits, too big for a long (at most 19 digits). So
    // work through it one digit at a time, the way long division is done on
    // paper: keep only the remainder so far, and for each next digit, multiply
    // that remainder by 10, add the digit, and take the remainder by 97 again.
    // The remainder never exceeds 96, so nothing can overflow.
    //
    // Example with 1234 % 97: 1 -> 12 -> 123 % 97 = 26 -> 264 % 97 = 70.
    private static int Mod97(string digits)
    {
        var remainder = 0;
        foreach (var character in digits)
        {
            var digit = character - '0';
            remainder = (remainder * 10 + digit) % 97;
        }
        return remainder;
    }
}
