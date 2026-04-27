using System.Diagnostics.Metrics;

namespace SddDemo.Ledger.Api.Observability;

/// <summary>
/// research.md §10 — IMeterFactory.Create("SddDemo.Ledger") with dot-namespaced
/// metric names. Counters increment from handlers (Phase 3+); the histogram is
/// recorded around each gRPC call. Explicit bucket boundaries live with the OTLP
/// AddView in <see cref="OpenTelemetryRegistration"/>.
/// </summary>
public sealed class LedgerMetrics
{
    public const string MeterName = "SddDemo.Ledger";

    public LedgerMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        var meter = meterFactory.Create(MeterName);

        Created = meter.CreateCounter<long>("ledger.created", "ledger", "Number of ledgers created.");
        Updated = meter.CreateCounter<long>("ledger.updated", "ledger", "Number of ledgers updated.");
        Deleted = meter.CreateCounter<long>("ledger.deleted", "ledger", "Number of ledgers deleted.");
        Archived = meter.CreateCounter<long>("ledger.archived", "ledger", "Ledgers transitioned to Archived.");
        AuditPurged = meter.CreateCounter<long>("ledger.audit.purged", "row", "Audit rows purged by retention.");
        OperationDuration = meter.CreateHistogram<double>(
            "ledger.operation.duration",
            unit: "s",
            description: "Latency of ledger operations.");
    }

    public Counter<long> Created { get; }
    public Counter<long> Updated { get; }
    public Counter<long> Deleted { get; }
    public Counter<long> Archived { get; }
    public Counter<long> AuditPurged { get; }
    public Histogram<double> OperationDuration { get; }
}
