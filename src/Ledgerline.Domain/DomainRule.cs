namespace Ledgerline.Domain;

// The rules the domain refuses to break. Every rule that protects money is
// enforced twice, here and in the database (blueprint 6, ADR-006), so each
// rule names its twin in BankCore.
//
// Business refusals are not here. "This account is frozen" or "not enough
// money" are normal answers to a question, not broken rules, so Account
// returns them as a DebitDecision or CreditDecision instead of throwing.
// The database raises those as 50006, 50007 and 50008.
public enum DomainRule
{
    // Twin: the currency_code check constraints, such as
    // ck_entry_currency_code (AED only in v1). A code the bank does not know.
    CurrencyUnknown,

    // Twin: the posting procedure keeps currencies apart when it checks a
    // journal balances (50001). Adding dirhams to dollars has no meaning.
    CurrencyMismatch,

    // Twin: the DECIMAL(19,4) type of every money column. A fifth decimal
    // place cannot be stored.
    MoneyTooManyDecimalPlaces,

    // Twin: DECIMAL(19,4) again. At most 15 digits before the decimal point.
    MoneyOutOfRange,

    // INV-03. Twin: ck_journal_idempotency_key and the procedure's 50010.
    IdempotencyKeyInvalid,

    // Twin: the procedure's 50003.
    JournalTooFewLines,

    // Twin: the procedure's 50004 (a zero amount).
    LineAmountNotPositive,

    // Twin: the procedure's 50004 (an amount finer than one fils).
    LineAmountNotWholeMinorUnits,

    // INV-01. Twin: the procedure's 50001 and the trigger ledger.tr_entry_balanced.
    JournalNotBalanced,
}
