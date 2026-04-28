namespace SddDemo.Ledger.Application.Abstractions.Observability;

/// <summary>
/// Application-level seam for ledger metrics. Concrete implementation lives in
/// the Api composition root (`Api/Observability/LedgerMetrics.cs`) and uses
/// `IMeterFactory` per Constitution Principle IV — Application code stays free
/// of `System.Diagnostics.Metrics` types so observability remains a service
/// concern, not a business concern.
/// </summary>
public interface ILedgerMetrics
{
    /// <summary>Increment the <c>ledger.created</c> counter.</summary>
    void RecordCreated();

    /// <summary>Increment the <c>ledger.updated</c> counter.</summary>
    void RecordUpdated();

    /// <summary>Increment the <c>ledger.deleted</c> counter.</summary>
    void RecordDeleted();

    /// <summary>Increment the <c>ledger.archived</c> counter.</summary>
    void RecordArchived();

    /// <summary>Increment the <c>ledger.audit.purged</c> counter by <paramref name="count"/> rows.</summary>
    void RecordAuditPurged(long count);

    /// <summary>
    /// Record latency on the <c>ledger.operation.duration</c> histogram with
    /// low-cardinality tags (per Constitution: bounded operation + outcome).
    /// </summary>
    void RecordOperationDuration(string operation, string outcome, double seconds);
}
