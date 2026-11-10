namespace Ledgerline.Domain.Tests;

// INV-03: the key that makes a command safe to retry. The rules are the same
// as ck_journal_idempotency_key and the procedure's 50010 (ADR-009);
// DomainParityTests checks the two agree.
public class IdempotencyKeyTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void Keys_of_1_to_100_characters_are_accepted(int length)
    {
        var value = new string('k', length);

        Assert.Equal(value, new IdempotencyKey(value).Value);
    }

    [Fact]
    public void A_key_of_101_characters_is_refused()
    {
        var error = Assert.Throws<DomainRuleException>(() => new IdempotencyKey(new string('k', 101)));

        Assert.Equal(DomainRule.IdempotencyKeyInvalid, error.Rule);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" abc")]
    [InlineData("abc ")]
    [InlineData("\tabc")]
    [InlineData("abc\t")]
    [InlineData("abc\n")]
    [InlineData("\rabc")]
    [InlineData(" ")]
    public void Missing_empty_or_padded_keys_are_refused(string? value)
    {
        var error = Assert.Throws<DomainRuleException>(() => new IdempotencyKey(value));

        Assert.Equal(DomainRule.IdempotencyKeyInvalid, error.Rule);
    }

    // The database allows spaces inside a key, so the domain does too.
    [Fact]
    public void A_space_inside_the_key_is_accepted()
    {
        Assert.Equal("has inner space", new IdempotencyKey("has inner space").Value);
    }

    // Keys are exact, like the column's binary collation.
    [Fact]
    public void Keys_that_differ_only_in_case_are_two_keys()
    {
        Assert.NotEqual(new IdempotencyKey("abc"), new IdempotencyKey("ABC"));
        Assert.Equal(new IdempotencyKey("abc"), new IdempotencyKey("abc"));
    }

    [Fact]
    public void ToString_is_the_key_exactly()
    {
        Assert.Equal("SAL-42-7", new IdempotencyKey("SAL-42-7").ToString());
    }
}
