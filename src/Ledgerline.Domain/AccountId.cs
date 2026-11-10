namespace Ledgerline.Domain;

// core.account.account_id. Both this and ledger.ledger_account.ledger_account_id
// are BIGINT, so as plain longs one could be passed where the other belongs
// and the compiler would not notice. As two types, it does.
//
// Account numbers start at 1000000001 (core.account_id_seq), but the type only
// refuses what can never be an id, so a test can use a small number.
public readonly record struct AccountId
{
    public AccountId(long value)
    {
        if (value < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "An account id is 1 or more.");
        }

        Value = value;
    }

    public long Value { get; }
}
