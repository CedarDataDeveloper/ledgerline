namespace Ledgerline.Domain;

// The answer to "may this account be debited?" (Account.CheckDebit). The
// refusals match the posting procedure's: AccountClosed is 50008,
// AccountFrozen is 50007 and InsufficientFunds is 50006.
public enum DebitDecision
{
    Allowed,
    AccountClosed,
    AccountFrozen,
    InsufficientFunds,
}
