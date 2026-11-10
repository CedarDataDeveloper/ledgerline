namespace Ledgerline.Domain.Tests;

// The codes ledger.journal.journal_type stores. DomainParityTests checks
// that this list equals ck_journal_journal_type.
public class JournalTypeTests
{
    [Theory]
    [InlineData(JournalType.Transfer, "TRANSFER")]
    [InlineData(JournalType.PaymentOut, "PAYMENT_OUT")]
    [InlineData(JournalType.SalaryCredit, "SALARY_CREDIT")]
    [InlineData(JournalType.InterestCapitalisation, "INTEREST_CAPITALISATION")]
    [InlineData(JournalType.ClearingSettlement, "CLEARING_SETTLEMENT")]
    [InlineData(JournalType.Fee, "FEE")]
    [InlineData(JournalType.Reversal, "REVERSAL")]
    [InlineData(JournalType.CapitalInjection, "CAPITAL_INJECTION")]
    public void ToCode_gives_the_database_code(JournalType type, string expected)
    {
        Assert.Equal(expected, type.ToCode());
    }

    // C# lets any number be cast to an enum.
    [Fact]
    public void ToCode_refuses_an_undefined_value()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((JournalType)99).ToCode());
    }
}
