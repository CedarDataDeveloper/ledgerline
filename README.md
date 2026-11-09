# Ledgerline

Ledgerline is a fictional UAE digital bank, built as a financial data engineering
portfolio project. Every customer, account and transaction is synthetic. There is
no real personal or financial data anywhere in this repository.

## What v1 will show

- A double-entry banking core in .NET 10 and SQL Server 2025, where one stored
  procedure is the only way to post to an append-only ledger and balances are
  always derived from it.
- A change data capture pipeline in Python that swaps internal IDs for tokens
  before anything reaches the warehouse.
- A dbt warehouse (bronze, silver, gold) with daily reconciliations and a
  register of breaks.
- A three-page Power BI report.

## Status

| Ticket | Scope | State |
| --- | --- | --- |
| LL-001 | Repository scaffold, solution structure, setup script | Implemented |
| LL-002 | DbUp migration runner, database schemas, SET options for indexed views | Implemented |
| LL-003 | Core and pii tables, token vault, constraints, synthetic IBAN generator with mod-97 | Implemented |
| LL-004 to LL-015 | The bank, the pipeline, the warehouse, the report | Planned |

The full ticket list is in section 17 of the technical blueprint.

## Requirements

- Windows
- SQL Server 2025 Developer edition with SQL Server Agent running
- .NET 10 SDK
- Python 3.12
- ODBC Driver 18 for SQL Server

## Getting started

```powershell
./setup.ps1
```

The script checks the tools above, creates a Python environment in `.venv`, then
builds and tests everything. Before the tests it creates the `BankCore` and
`Warehouse` databases if they are missing and applies any pending scripts from
`db/*/migrations`; scripts already applied are skipped. Two options cover
machines that differ from the defaults:

- `-Python <path>`: the Python 3.12 interpreter to use when `python` on PATH is
  not version 3.12.
- `-SqlServer <name>`: the instance to migrate and test against when it is not
  the default instance on `localhost`.

## Layout

| Folder | Contents |
| --- | --- |
| `docs/` | Blueprint, decision records, planted traps, parking lot |
| `db/` | Numbered SQL migration scripts for BankCore and the warehouse |
| `src/` | The .NET application: Domain, Application, Infrastructure, Api, Simulator |
| `tests/` | xUnit tests for the domain and for the database |
| `generator/` | Python: synthetic customers and transaction intents |
| `pipeline/` | Python: change data capture extract and token swap |
| `dbt/` | Warehouse models and tests |
| `powerbi/` | Report file and measure definitions |
| `ops/` | SQL Server Agent job scripts, replay script, runbook |

## Licence

MIT. See [LICENSE](LICENSE).
