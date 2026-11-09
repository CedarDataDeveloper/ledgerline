# Decision records

One short file per design decision: what was decided, why, and the trade-off
accepted. Copy `ADR-000-template.md` to start a new one.

ADR-001 to ADR-019 are listed in section 18 of the technical blueprint. Each
one is written up here as the ticket that implements it is built. Decisions
made after the blueprint start at ADR-020.

| ADR | Decision | Status |
| --- | --- | --- |
| [006](ADR-006-database-enforces-invariants.md) | The database enforces invariants too | Accepted |
| [007](ADR-007-balances-derived-through-an-indexed-view.md) | Balances derived through an indexed view | Accepted |
| [008](ADR-008-personal-data-split.md) | Personal-data split, CDC column lists, token swap at extract | Accepted |
| [009](ADR-009-idempotency-keys-on-every-money-path.md) | Idempotency keys on every money path | Accepted |
| [010](ADR-010-schema-as-numbered-sql-scripts.md) | Schema as numbered SQL scripts applied by DbUp; EF Core maps only | Accepted |
| [020](ADR-020-fabric-finish-line-deferred.md) | The Fabric finish line is deferred out of v1 | Accepted |
| [021](ADR-021-posting-locks-every-account-in-order.md) | Posting locks every account in the journal, in ascending order | Accepted |
