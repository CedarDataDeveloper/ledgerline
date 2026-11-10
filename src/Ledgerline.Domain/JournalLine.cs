namespace Ledgerline.Domain;

// One line of a journal: an amount debited or credited to one ledger account.
//
// The sign follows the database exactly (ledger.journal_line): a positive
// amount is a debit, a negative amount is a credit. Callers never write the
// sign themselves. They call Debit or Credit with a positive amount, and the
// line sets the sign, so code reads like the blueprint's process tables:
// "Dr 1010 / Cr the salary account".
public sealed record JournalLine
{
    private JournalLine(LedgerAccountId ledgerAccountId, Money signedAmount)
    {
        LedgerAccountId = ledgerAccountId;
        SignedAmount = signedAmount;
    }

    public LedgerAccountId LedgerAccountId { get; }

    // Positive = debit, negative = credit.
    public Money SignedAmount { get; }

    public Currency Currency => SignedAmount.Currency;

    public bool IsDebit => SignedAmount.IsPositive;

    public bool IsCredit => SignedAmount.IsNegative;

    public static JournalLine Debit(LedgerAccountId ledgerAccountId, Money amount) =>
        new(ledgerAccountId, CheckedAmount(amount));

    public static JournalLine Credit(LedgerAccountId ledgerAccountId, Money amount) =>
        new(ledgerAccountId, -CheckedAmount(amount));

    // The same rules as the posting procedure's 50004: not zero, and in whole
    // fils. A negative amount is refused too, because the sign is set by
    // choosing Debit or Credit; a negative debit would really be a credit.
    private static Money CheckedAmount(Money amount)
    {
        ArgumentNullException.ThrowIfNull(amount);

        if (!amount.IsPositive)
        {
            throw new DomainRuleException(
                DomainRule.LineAmountNotPositive,
                $"A journal line needs a positive amount; the sign comes from Debit or Credit. Got {amount}.");
        }

        if (!amount.IsWholeMinorUnits)
        {
            throw new DomainRuleException(
                DomainRule.LineAmountNotWholeMinorUnits,
                $"A journal line must be in whole minor units (whole fils for AED). Got {amount}.");
        }

        return amount;
    }
}
