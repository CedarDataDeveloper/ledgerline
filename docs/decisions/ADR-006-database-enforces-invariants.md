# ADR-006: The database enforces invariants too

- Status: Accepted
- Date: 2026-11-09

## Decision

Every rule that protects money is enforced in the database as well as in the
application. The posting procedure `ledger.usp_post_journal` checks each rule
before it writes, and table constraints and triggers hold the ledger's shape
even against SQL that goes around the procedure.

What exists after LL-004:

- INV-01 (every journal balances): the procedure refuses an unbalanced journal
  (50001), and the AFTER INSERT trigger `ledger.tr_entry_balanced` refuses any
  insert into `ledger.entry` that leaves a journal unbalanced.
- INV-02 (ledger rows are never changed): the INSTEAD OF UPDATE, DELETE
  triggers `ledger.tr_entry_immutable` and `ledger.tr_journal_immutable`
  (50002). `TRUNCATE` is refused by SQL Server because of the indexed view and
  the foreign key. Ledger accounts are insert-only too
  (`ledger.tr_ledger_account_immutable`), because repointing one would move an
  account's whole history.
- INV-03 (a repeated key has no new effect): see ADR-009.
- INV-04 (no overdraft) and INV-05 (frozen and closed accounts): checked in
  the procedure while every account in the journal is locked (see ADR-021).
- INV-08 (open business date only): the procedure reads the single `OPEN` row
  of `ops.business_calendar`, a filtered unique index allows only one, and a
  composite foreign key ties each entry's date to its journal's.
- Whole fils, non-zero amounts, AED only, and the shape of ledger accounts are
  check constraints.

What exists after LL-005: the domain half of these rules (ADR-005).
`Journal.Create` refuses fewer than two lines and lines that do not net to
zero in each currency (INV-01); `JournalLine` refuses zero and sub-fils
amounts; a `Journal` has no way to change once built (INV-02);
`Account.CheckDebit` refuses closed, then frozen, then not enough money, in
the procedure's order (INV-04, INV-05); `IdempotencyKey` has the key rules
(INV-03). `DomainParityTests` checks rounding, the journal type and status
codes, and the key rules against the database.

Still to come: the audit event, permissions and `DENY` that make the procedure the only way
in (LL-009); INV-06 and INV-07 with their tables (LL-008, LL-009).

## Known limits

Costs accepted for now, recorded so nobody mistakes them for oversights:

- A refusal rolls back the caller's whole transaction. The procedure runs with
  `XACT_ABORT ON`, so any refusal also undoes work the caller did earlier in
  the same transaction.
- Locks wait without a timeout. A transaction that stays open while holding a
  key or an account blocks retries of that key, and postings to that account,
  until it ends.
- The journal type is not checked against the lines. A `TRANSFER` between two
  GL accounts, for example, is accepted.
- Until the LL-009 permissions exist, SQL that inserts balanced entries
  directly into `ledger.entry` goes around the procedure's overdraft, status
  and date checks. The balancing trigger still holds.

## Why

Defence in depth. The application can have a bug, and a person with database
access can run any SQL. If the database refuses bad data on its own, neither
can make the ledger wrong.

## Trade-off accepted

Some rules exist twice, in C# and in SQL, and the two must be kept in step.
The database checks also cost a little time on every posting.
