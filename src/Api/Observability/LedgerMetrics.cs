using System.Diagnostics.Metrics;
using SddDemo.Ledger.Application.Abstractions.Observability;

namespace SddDemo.Ledger.Api.Observability;

/// <summary>
/// research.md §10 — IMeterFactory.Create("SddDemo.Ledger") with dot-namespaced
/// metric names. Counters increment from handlers (Phase 3+); the histogram is
/// recorded around each gRPC call. Explicit bucket boundaries live with the OTLP
/// AddView in <see cref="OpenTelemetryRegistration"/>.
///
/// Lives in the Api composition root (NOT Application) so observability stays a
/// service-level concern. Application code depends on
/// <see cref="ILedgerMetrics"/> only — never on `System.Diagnostics.Metrics`.
/// </summary>
public sealed class LedgerMetrics : ILedgerMetrics
{
    public const string MeterName = "SddDemo.Ledger";

    private readonly Counter<long> _created;
    private readonly Counter<long> _updated;
    private readonly Counter<long> _deleted;
    private readonly Counter<long> _archived;
    private readonly Counter<long> _auditPurged;
    private readonly Histogram<double> _operationDuration;

    public LedgerMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        var meter = meterFactory.Create(MeterName);

        _created = meter.CreateCounter<long>("ledger.created", "ledger", "Number of ledgers created.");
        _updated = meter.CreateCounter<long>("ledger.updated", "ledger", "Number of ledgers updated.");
        _deleted = meter.CreateCounter<long>("ledger.deleted", "ledger", "Number of ledgers deleted.");
        _archived = meter.CreateCounter<long>("ledger.archived", "ledger", "Ledgers transitioned to Archived.");
        _auditPurged = meter.CreateCounter<long>("ledger.audit.purged", "row", "Audit rows purged by retention.");
        _operationDuration = meter.CreateHistogram<double>(
            "ledger.operation.duration",
            unit: "s",
            description: "Latency of ledger operations.");
    }

    public void RecordCreated() => _created.Add(1);

    public void RecordUpdated() => _updated.Add(1);

    public void RecordDeleted() => _deleted.Add(1);

    public void RecordArchived() => _archived.Add(1);

    public void RecordAuditPurged(long count) => _auditPurged.Add(count);

    public void RecordOperationDuration(string operation, string outcome, double seconds)
    {
        _operationDuration.Record(
            seconds,
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("outcome", outcome));
    }
}
