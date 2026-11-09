# ADR-008: Personal-data split, CDC column lists, token swap at extract

- Status: Accepted
- Date: 2026-11-09

## Decision

Personal data lives only in the `pii` schema of BankCore, which change data
capture never reads. Captured tables leave out identifying columns such as the
IBAN through their column lists. The extract replaces customer, account and
card IDs with random tokens from `pii.token_vault` before anything reaches the
warehouse, and the vault itself never leaves BankCore.

What exists after LL-003:

- The split: names, dates of birth, synthetic national IDs and contact
  details are in `pii.customer_identity`; `core.customer` holds none of them.
- The token vault, `pii.token_vault`, and the triggers on `core.customer` and
  `core.account` that give every new row its token, so nothing can exist
  without one.
- A standing database test that fails if a personal-data column appears
  outside the `pii` schema, in any table added later.

What arrives in LL-011: the change data capture column lists and the token
swap in the extract.

## Why

Privacy by design: personal data cannot leak from a place it never reaches,
so there is nothing to mask downstream. Someone with full access to the
warehouse still cannot name a customer.

## Trade-off accepted

Warehouse analysis works by token only. A question that needs a name or a
contact detail has to be answered inside BankCore, not in the warehouse.
