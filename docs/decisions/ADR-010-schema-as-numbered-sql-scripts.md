# ADR-010: Schema as numbered SQL scripts applied by DbUp

- Status: Accepted
- Date: 2026-11-09

## Decision

The schema of BankCore and the warehouse is defined by numbered SQL scripts in
`db/bankcore/migrations` and `db/warehouse/migrations`, applied by DbUp through
`src/Ledgerline.DbMigrator`. EF Core maps to that schema and never migrates it.

Rules for every script:

- Named `NNNN_lower_snake_case.sql`, numbers unique within a folder. DbUp runs
  scripts in name order.
- Starts with the standard header of SET options (`ANSI_NULLS`, `ANSI_PADDING`,
  `ANSI_WARNINGS`, `ARITHABORT`, `CONCAT_NULL_YIELDS_NULL`, `QUOTED_IDENTIFIER`
  on; `NUMERIC_ROUNDABORT` off) followed by `GO`. Indexed views need these.
- Runs in its own transaction. A script that fails is rolled back completely
  and is not recorded, so it can be fixed and run again.
- Is never edited once applied. A change is a new script with the next number.

DbUp records applied scripts in its default journal table, `dbo.SchemaVersions`,
in each database.

## Why

Stored procedures, triggers, views and change data capture live in the
database. They are written in SQL anyway, so the schema is too, in one place
and in one language, and reviewed as plain SQL in pull requests.

## Trade-off accepted

- No EF Core migrations convenience: every schema change is written by hand
  as SQL, and the EF Core mappings are kept in step manually.
- Database defaults are not changed with `ALTER DATABASE ... SET`. Client
  connections override those defaults, so each script sets its own options in
  the header instead.
- Applied scripts are not checksummed. Editing one after it has run goes
  unnoticed by DbUp, so the "never edit" rule relies on review.
