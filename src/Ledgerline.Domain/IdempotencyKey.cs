namespace Ledgerline.Domain;

// The key that makes a money-moving command safe to retry (INV-03, ADR-009).
// Posting twice with the same key gives one journal.
//
// The rules mirror ledger.journal.idempotency_key and the posting procedure's
// 50010, so a key built here is never refused there:
// - 1 to 100 characters. The column is NVARCHAR(100), which counts UTF-16
//   code units, the same as string.Length.
// - No whitespace at the start or the end. SQL Server's = ignores trailing
//   spaces, so "abc" and "abc " would be one key there. Spaces inside the key
//   are allowed, as the database allows them.
// - Exact: "abc" and "ABC" are two keys. Record equality compares strings
//   ordinally and case-sensitively, like the column's Latin1_General_BIN2
//   collation.
//
// The key formats for each money path (SAL-{wps_file_id}-{line_no},
// PAY-{request_id} and the others in blueprint 7.6) will be added here as
// factory methods as each path arrives, so every key is built in one place.
public sealed record IdempotencyKey
{
    public const int MaxLength = 100;

    public IdempotencyKey(string? value)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > MaxLength
            || char.IsWhiteSpace(value[0])
            || char.IsWhiteSpace(value[^1]))
        {
            throw new DomainRuleException(
                DomainRule.IdempotencyKeyInvalid,
                "INV-03: an idempotency key must be 1 to 100 characters with no leading or trailing whitespace.");
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
