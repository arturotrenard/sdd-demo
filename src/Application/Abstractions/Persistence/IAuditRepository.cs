using Npgsql;
using SddDemo.Ledger.Domain.Common;

namespace SddDemo.Ledger.Application.Abstractions.Persistence;

/// <summary>
/// data-model.md §5 + research.md §6 — write-only contract for the audit log.
/// <see cref="WriteAsync"/> participates in the same transaction as the
/// state-changing SQL (FR-012, SC-005). <see cref="PurgeOlderThanAsync"/> is the
/// only deletion path, invoked by <c>AuditRetentionPurgeService</c> daily at 03:00 UTC.
/// </summary>
public interface IAuditRepository
{
    Task<Result> WriteAsync(
        AuditEntryToWrite entry,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken);

    Task<Result<int>> PurgeOlderThanAsync(
        TimeSpan retention,
        CancellationToken cancellationToken);
}

/// <summary>
/// Plain DTO carrier for the audit row that the repository will write.
/// The real Domain <c>AuditEntry</c> aggregate (with Builder + invariants) lands in
/// Phase 3 (T045). Until then this DTO exposes the columns the SQL expects.
/// </summary>
public sealed record AuditEntryToWrite(
    Guid ActorId,
    Guid LedgerId,
    short EventType,
    DateTimeOffset EventAt,
    System.Text.Json.JsonDocument Payload);
