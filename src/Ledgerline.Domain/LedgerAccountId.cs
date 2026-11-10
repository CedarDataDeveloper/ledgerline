namespace Ledgerline.Domain;

// ledger.ledger_account.ledger_account_id, which starts at 1. A separate type
// from AccountId so that the two BIGINT ids can never be swapped (see AccountId).
public readonly record struct LedgerAccountId
{
    public LedgerAccountId(long value)
    {
        if (value < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A ledger account id is 1 or more.");
        }

        Value = value;
    }

    public long Value { get; }
}
