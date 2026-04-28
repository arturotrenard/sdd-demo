namespace SddDemo.Ledger.Domain.Ledgers;

/// <summary>
/// data-model.md §1.1 — ledger lifecycle states. Active ledgers can be mutated;
/// Archived ledgers are read-only except for the un-archive transition (FR-007a).
/// Numeric values mirror the proto enum and the SQL <c>status</c> column.
/// </summary>
public enum LedgerStatus
{
    Active = 1,
    Archived = 2,
}
