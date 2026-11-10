namespace Ledgerline.Domain.Tests;

// One journal line. Debit and Credit take a positive amount and set the sign:
// positive = debit, negative = credit, as in ledger.journal_line.
public class JournalLineTests
{
    private static readonly LedgerAccountId Cash = new(1);

    [Fact]
    public void Debit_keeps_the_amount_positive()
    {
        var line = JournalLine.Debit(Cash, new Money(100.00m, Currency.Aed));

        Assert.Equal(Cash, line.LedgerAccountId);
        Assert.Equal(new Money(100.00m, Currency.Aed), line.SignedAmount);
        Assert.Equal(Currency.Aed, line.Currency);
        Assert.True(line.IsDebit);
        Assert.False(line.IsCredit);
    }

    [Fact]
    public void Credit_makes_the_amount_negative()
    {
        var line = JournalLine.Credit(Cash, new Money(100.00m, Currency.Aed));

        Assert.Equal(new Money(-100.00m, Currency.Aed), line.SignedAmount);
        Assert.True(line.IsCredit);
        Assert.False(line.IsDebit);
    }

    // The procedure's 50004: no zero amounts. A negative amount is refused
    // too: the sign comes from choosing Debit or Credit.
    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 0)]
    [InlineData(true, -1)]
    [InlineData(false, -1)]
    public void A_zero_or_negative_amount_is_refused(bool debit, int amount)
    {
        var money = new Money(amount, Currency.Aed);

        var error = Assert.Throws<DomainRuleException>(() => Line(debit, money));

        Assert.Equal(DomainRule.LineAmountNotPositive, error.Rule);
    }

    // The procedure's 50004 again: postings are in whole fils.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_amount_finer_than_one_fils_is_refused(bool debit)
    {
        var error = Assert.Throws<DomainRuleException>(() => Line(debit, new Money(0.001m, Currency.Aed)));

        Assert.Equal(DomainRule.LineAmountNotWholeMinorUnits, error.Rule);
    }

    private static JournalLine Line(bool debit, Money amount) =>
        debit ? JournalLine.Debit(Cash, amount) : JournalLine.Credit(Cash, amount);
}
