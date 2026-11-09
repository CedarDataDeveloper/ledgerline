# ADR-009: Idempotency keys on every money path

- Status: Accepted
- Date: 2026-11-09

## Decision

Every command that moves money carries an idempotency key, and every journal
stores it in `ledger.journal.idempotency_key`, backed by the unique
constraint `uq_journal_idempotency_key`. Posting with a key that already has a
journal returns that journal's id and does nothing else (INV-03). The key
formats for each operation are in blueprint section 7.6.

What exists after LL-004:

- The key column and its unique constraint.
- Keys are exact: two keys are the same only if they are the same characters.
  - The column has the binary collation `Latin1_General_BIN2`. Under the
    server's case-insensitive collation `abc` and `ABC` would be one key,
    while the application lock below is case-sensitive, so the two would
    disagree.
  - No padding: `ck_journal_idempotency_key` refuses an empty key and any key
    with leading or trailing whitespace. SQL Server's `=` ignores trailing
    spaces even under a binary collation, so the check compares byte lengths
    (`DATALENGTH`) of the key and its trimmed form.
  - At most 100 characters. SQL Server silently cuts an argument that is
    longer than its parameter. With a 100-character parameter, a
    101-character key would arrive as its first 100 characters, match another
    request's journal and return its id: that request would never post, and
    no error would say so. The procedure's parameter is therefore
    `NVARCHAR(400)`, so an over-long key arrives whole and can be refused.
- `ledger.usp_post_journal` refuses a key that is missing, blank, padded or
  longer than 100 characters (50010), and checks for the key before anything
  else, so a retry gets the original answer even if its lines would now be
  refused.
- Two calls with the same key at the same moment: an exclusive application
  lock on the key (`sp_getapplock`, owned by the transaction) lets one through
  at a time. The second waits, finds the first's journal and returns its id.
  The unique constraint stays as the backstop.

Still to come: `ops.transfer_request` with the client's request ID as its
primary key, and the stored response a replay returns (LL-007); the keys for
each money path as it arrives: salary credits and payments out (LL-007),
interest capitalisation and clearing settlement (LL-008), fees (LL-009);
idempotent loads in the pipeline (LL-011 onwards).

## Why

Networks fail and jobs crash halfway. With a key on every money path, a retry
or a re-run is always safe: it either does the work once or finds it already
done.

## Trade-off accepted

Key design up front: every money path needs a key that is stable across
retries and unique across genuine requests, decided before the path is built.
The key alone decides. A repeat is not compared with the original, so a
client that reuses a key for a different request gets the first result back
without an error.

Exact keys put the burden on clients: a client must send the same characters
on every retry, with no change of case and no added spaces. Because the
column's collation differs from the database's, a query that combines the
key with other text (for example in a `UNION`) needs an explicit `COLLATE`.
