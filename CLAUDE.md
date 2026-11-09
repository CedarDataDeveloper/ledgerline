# Ledgerline: standing instructions for Claude Code

Ledgerline is a fictional bank with synthetic data only, built as a financial data
engineering portfolio project. Read docs/blueprint before any task. Lake is the
architect: propose a plan, explain choices, and wait for review on design decisions.

## Non-negotiables
- Money is DECIMAL(19,4) in SQL and decimal in C#. Never float, real or double.
- Round half away from zero. In C# always pass MidpointRounding.AwayFromZero.
- Timestamps are UTC datetime2(3) in *_utc columns. Business dates are DATE (Asia/Dubai).
- Never call DateTime.Now. Use the injected TimeProvider and ops.business_calendar.
- The ledger is append-only. Corrections are reversal journals. No UPDATE or DELETE.
- Only ledger.usp_post_journal writes ledger rows. Every journal nets to zero.
- Every money-moving command carries an idempotency key backed by a UNIQUE constraint.
- Personal data lives only in the pii schema. Never log it, never replicate it.
- Synthetic data only: no real names, ID numbers, IBANs or card numbers.

## How we work
- One ticket = one branch = one small pull request. Stay inside the ticket.
- Write the failing test first for any rule in the blueprint (INV-xx, BR-xx).
- Explain non-obvious choices in the PR description. Record design decisions as
  docs/decisions/ADR-xxx.md.
- New ideas go in docs/parking-lot.md, never into the code.
- Schema changes are new numbered scripts in db/*/migrations. Never edit an applied one.

## Commands
- dotnet build; dotnet test
- python -m pytest generator pipeline
- cd dbt; dbt build
- ./ops/replay.ps1 -Customers 2000 -Months 6 -Seed 42
