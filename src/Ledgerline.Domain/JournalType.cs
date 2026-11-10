namespace Ledgerline.Domain;

// The kinds of journal the ledger accepts. The database spells each one in
// capitals in ledger.journal.journal_type, and ck_journal_journal_type allows
// exactly these; a parity test in Ledgerline.Db.Tests keeps the two lists equal.
public enum JournalType
{
    Transfer,
    PaymentOut,
    SalaryCredit,
    InterestCapitalisation,
    ClearingSettlement,
    Fee,
    Reversal,
    CapitalInjection,
}

public static class JournalTypeCodes
{
    // The code the database stores. Written out one by one rather than
    // derived from the enum name, so renaming an enum member can never
    // silently change what is written to the ledger.
    public static string ToCode(this JournalType type) => type switch
    {
        JournalType.Transfer => "TRANSFER",
        JournalType.PaymentOut => "PAYMENT_OUT",
        JournalType.SalaryCredit => "SALARY_CREDIT",
        JournalType.InterestCapitalisation => "INTEREST_CAPITALISATION",
        JournalType.ClearingSettlement => "CLEARING_SETTLEMENT",
        JournalType.Fee => "FEE",
        JournalType.Reversal => "REVERSAL",
        JournalType.CapitalInjection => "CAPITAL_INJECTION",
        // C# lets any number be cast to an enum, so (JournalType)99 is possible.
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a known journal type."),
    };
}
