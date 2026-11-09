# ADR-020: The Fabric finish line is deferred out of v1

- Status: Accepted
- Date: 2026-11-09

## Decision

The Microsoft Fabric finish line (ticket LL-016) is not part of v1. Version 1
ends with the local system: BankCore, the change data capture pipeline, the dbt
warehouse on SQL Server and the Power BI report. ADR-015 (Fabric Warehouse
through the on-premises data gateway) is on hold, not rejected.

## Why

To be written by Lake. The decision on 9 November 2026 was to skip Fabric
"for now".

## Trade-off accepted

- The hybrid boundary is not demonstrated in v1: no cross-boundary
  reconciliation with matching control totals.
- The definition-of-done line "Warehouse rebuilt in Fabric with matching
  control totals across the boundary" does not apply to v1.
- The project cannot be described as migrated to Microsoft Fabric until this
  work is picked up again.
