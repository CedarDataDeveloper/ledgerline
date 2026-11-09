namespace Ledgerline.Domain.Tests;

// Ledgerline's synthetic IBANs: AE, two check digits, the fictional bank code
// 999, then the account number padded to 16 digits (blueprint 7.1).
public class SyntheticIbanTests
{
    // Computed independently in T-SQL, so this is a real cross-check of the
    // check-digit arithmetic, not the code agreeing with itself.
    [Fact]
    public void Generate_matches_the_known_vector()
    {
        Assert.Equal("AE429990000001000000001", SyntheticIban.Generate(1000000001));
    }

    // From the smallest account number to the 16-digit maximum.
    [Theory]
    [InlineData(1L)]
    [InlineData(9L)]
    [InlineData(96L)]
    [InlineData(97L)]
    [InlineData(1000000001L)]
    [InlineData(1000000002L)]
    [InlineData(4242424242424242L)]
    [InlineData(1234567890123456L)]
    [InlineData(9999999999999999L)]
    public void Every_generated_iban_is_valid_and_has_the_expected_layout(long accountNumber)
    {
        var iban = SyntheticIban.Generate(accountNumber);

        Assert.True(SyntheticIban.IsValid(iban), iban);
        Assert.Equal(23, iban.Length);
        Assert.StartsWith("AE", iban);
        Assert.Equal(SyntheticIban.BankCode, iban.Substring(4, 3));
        Assert.Equal(accountNumber.ToString("D16"), iban.Substring(7));
    }

    // Mod-97 catches every single-digit typing error. Positions 2 to 22 are
    // the digits; position 0 and 1 are the letters AE.
    [Theory]
    [InlineData(1L)]
    [InlineData(1000000001L)]
    [InlineData(9999999999999999L)]
    public void Changing_any_single_digit_makes_it_invalid(long accountNumber)
    {
        var iban = SyntheticIban.Generate(accountNumber);

        for (var position = 2; position < iban.Length; position++)
        {
            for (var digit = '0'; digit <= '9'; digit++)
            {
                if (digit == iban[position])
                {
                    continue;
                }

                var changed = iban[..position] + digit + iban[(position + 1)..];
                Assert.False(SyntheticIban.IsValid(changed), $"{changed} (position {position}) was accepted");
            }
        }
    }

    // Mod-97 also catches two neighbouring digits typed the wrong way round.
    [Theory]
    [InlineData(1L)]
    [InlineData(1000000001L)]
    [InlineData(1234567890123456L)]
    public void Swapping_two_different_adjacent_digits_makes_it_invalid(long accountNumber)
    {
        var iban = SyntheticIban.Generate(accountNumber);
        var swapsTried = 0;

        for (var position = 2; position < iban.Length - 1; position++)
        {
            if (iban[position] == iban[position + 1])
            {
                continue;
            }

            var chars = iban.ToCharArray();
            (chars[position], chars[position + 1]) = (chars[position + 1], chars[position]);
            var swapped = new string(chars);

            Assert.False(SyntheticIban.IsValid(swapped), $"{swapped} (positions {position} and {position + 1}) was accepted");
            swapsTried++;
        }

        // Guards against a loop that silently tried nothing.
        Assert.True(swapsTried > 0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AE42999000000100000000")]   // 22 characters
    [InlineData("AE4299900000010000000011")] // 24 characters
    [InlineData("ae429990000001000000001")]  // lower-case country code
    [InlineData("AE42999000000100000A001")]  // a letter among the digits
    [InlineData("AE42 999000000100000001")]  // a space among the digits
    [InlineData("GB429990000001000000001")]  // another country
    // A published example with a real bank's code (033): its check digits are
    // correct, but it is not a Ledgerline IBAN, so it must be refused.
    [InlineData("AE070331234567890123456")]
    public void IsValid_rejects(string? iban)
    {
        Assert.False(SyntheticIban.IsValid(iban));
    }

    // Arabic-Indic digits pass char.IsDigit, but they are not 0 to 9.
    [Fact]
    public void IsValid_rejects_digits_from_other_scripts()
    {
        var iban = "AE42999000000100000000" + '١';

        Assert.False(SyntheticIban.IsValid(iban));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    [InlineData(10000000000000000L)] // 17 digits
    [InlineData(long.MaxValue)]
    public void Generate_rejects_numbers_outside_1_to_16_digits(long accountNumber)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SyntheticIban.Generate(accountNumber));
    }
}
