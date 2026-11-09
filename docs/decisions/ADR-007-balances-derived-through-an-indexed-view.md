# ADR-007: Balances derived through an indexed view

- Status: Accepted
- Date: 2026-11-09

## Decision

No table stores a balance. `ledger.v_account_balance` sums `ledger.entry` per
ledger account, and its unique clustered index `ux_v_account_balance` makes it
an indexed view: SQL Server stores the totals and updates them in the same
transaction as every insert into `ledger.entry`.

What exists after LL-004:

- The view, read `WITH (NOEXPAND)`. A customer's balance is `-net_amount`,
  because customer accounts are liabilities of the bank. An account with no
  entries has no row, so its balance is zero.
- The posting procedure reads it for the overdraft check (INV-04).
- `ix_entry_account_date (ledger_account_id, business_date) INCLUDE (amount)`
  for point-in-time balances.
- A standing test that fails if a column named `balance` or ending in
  `_balance` appears, and a test that the view always agrees with the entries.

Still to come: point-in-time balances for interest (LL-008) and
reconciliation (later tickets) read through the index above.

## Why

A stored balance can drift away from the ledger: one missed update and the two
disagree. A view that SQL Server maintains inside the posting's own
transaction cannot drift.

## Trade-off accepted

Every posting updates one view row per account it touches, and the bank's own
accounts, such as cash at central bank (1010), are touched by many postings.
At real-bank volumes those rows become hot spots. Synthetic volumes are far
below that; the upgrade path is periodic balance snapshots.
