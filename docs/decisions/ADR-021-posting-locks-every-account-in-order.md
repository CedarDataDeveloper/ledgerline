# ADR-021: Posting locks every account in the journal, in ascending order

- Status: Accepted
- Date: 2026-11-09

## Decision

Before it checks balances and statuses, `ledger.usp_post_journal` locks every
ledger account in the journal with `UPDLOCK`, one at a time in ascending
`ledger_account_id` order, in an explicit loop so the order is certain. The
locks are held until the transaction ends. The lock is on the
`ledger.ledger_account` row, not on the balance view, because an account with
no entries has no row in the view to lock.

This strengthens blueprint section 7.3, which locks only the debited customer
accounts.

What exists after LL-004: the locking loop, and two tests that run postings at
the same moment. Twenty payments of 60.00 from an account holding 100.00 let
exactly one through. Sixty transfers in both directions between the same two
accounts all succeed with no deadlock.

Still to come: nothing further for this rule. Later money paths (LL-007,
LL-008) post through the same procedure and inherit it.

## Why

With one consistent order, two journals that share accounts queue one behind
the other instead of deadlocking. A transfer from A to B and one from B to A
both lock the lower id first. Locking only the debited account does not give
that guarantee: each transfer locks a different account first, and the other
account's locks are then taken in whatever order SQL Server happens to use.
When the blueprint's version was tried against the opposite-direction test
(during review of LL-004, on 2026-11-09), it produced deadlocks (error 1205)
in three runs out of three; this version has produced none in any run.

Holding the lock also means the overdraft check reads a balance that nobody
else can change before this posting commits.

## Trade-off accepted

A little more locking than the blueprint's version: credited accounts and the
bank's GL accounts are locked too, so postings that share a GL account such as
cash at central bank (1010) run one at a time. They already did in practice,
because each posting updates that account's row in the balance view, which
SQL Server holds until commit (see ADR-007). The extra cost is small at
synthetic volumes.
