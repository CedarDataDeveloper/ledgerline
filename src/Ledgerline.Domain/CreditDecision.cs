namespace Ledgerline.Domain;

// The answer to "may this account be credited?" (Account.CheckCredit). Only a
// closed account refuses a credit, matching the posting procedure's 50008.
public enum CreditDecision
{
    Allowed,
    AccountClosed,
}
