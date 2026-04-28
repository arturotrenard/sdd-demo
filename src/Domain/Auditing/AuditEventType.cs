namespace SddDemo.Ledger.Domain.Auditing;

/// <summary>
/// data-model.md §1.2 — kinds of state-change events captured in <c>ledger_audit</c>.
/// Numeric values mirror the SQL <c>event_type</c> column.
/// </summary>
public enum AuditEventType
{
    Create = 1,
    Update = 2,
    Delete = 3,
}
