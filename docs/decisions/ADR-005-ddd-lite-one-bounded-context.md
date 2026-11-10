# ADR-005: DDD-lite, one bounded context

- Status: Accepted
- Date: 2026-11-10

## Decision

The domain model is one bounded context, Ledger, built from a few plain C#
types in `src/Ledgerline.Domain`: value objects that check themselves when
they are made, and aggregates that can only be built in a valid state. No
framework, no repositories or domain events, and no package or project
references (a test fails if one is added). Each rule that protects money is
also enforced by the database (ADR-006); the domain is the early, clear
answer, and the posting procedure is the judge.

What exists after LL-005:

- `Money`: a decimal amount and a `Currency`. Refuses more than four decimal
  places and anything that does not fit `DECIMAL(19,4)`; refuses arithmetic
  and comparison across two currencies. No public member takes or returns a
  float or double.
- `Currency`: AED and USD only, each with its number of minor-unit places.
- `Rounding.HalfAwayFromZero`: the one place in C# that rounds money. A
  parity test checks it against SQL Server's `ROUND`.
- `BusinessDate`: the bank's working day, with no member that reads the
  clock.
- `IdempotencyKey`: the same rules as the key column and the procedure's
  50010 (see ADR-009).
- `AccountId` and `LedgerAccountId`: the two BIGINT ids as two types, so they
  cannot be swapped.
- `JournalType` and `AccountStatus`, with the codes the database stores;
  parity tests check them against `ck_journal_journal_type` and
  `ck_account_status`.
- `JournalLine` and `Journal`: lines are made with `Debit` or `Credit` from a
  positive amount in whole minor units; `Journal.Create` is the only way to
  build a journal and refuses fewer than two lines or lines that do not net
  to zero in each currency (INV-01). A journal cannot be changed once built.
- `Account`: decides a debit (closed, then frozen, then not enough money) and
  a credit (only closed refuses) and returns the answer as a decision, not an
  exception. The balance is passed in, because balances are derived (ADR-007).
- `DomainRuleException` with a `DomainRule` that names each rule's database
  twin.

Still to come: the clock and the conversion of a clock reading to a business
date (LL-006); `TransferRequest` and the key formats for each money path
(LL-007); interest accrual as a pure function (LL-008); `PendingChange` and
maker-checker approval (LL-009).

## Why

Rules matter more than patterns. A small model that makes invalid money,
unbalanced journals and bad keys impossible to build protects the ledger
better than layers of ceremony, and every line of it can be explained. One
bounded context fits a bank this size: everything it does is posting to one
ledger.

## Trade-off accepted

Less enterprise ceremony, so the model does not show the full DDD toolkit
(no domain events, no repositories, no separate contexts for payments or
products). If the bank grew, some of that would have to be added later. The
rules also exist twice, in C# and in SQL; parity tests keep the two in step,
but only for the rules they cover.
