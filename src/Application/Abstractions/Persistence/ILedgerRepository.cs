using SddDemo.Ledger.Application.Features.Ledgers.Queries.ListLedgers;
using SddDemo.Ledger.Domain.Auditing;
using SddDemo.Ledger.Domain.Common;
using SddDemo.Ledger.Domain.Ledgers;
using DomainLedger = SddDemo.Ledger.Domain.Ledgers.Ledger;

namespace SddDemo.Ledger.Application.Abstractions.Persistence;

/// <summary>
/// data-model.md §5 — public ledger repository contract. Phases 3–6 fill in the
/// full surface: Create (US1), GetById/List (US2), Update (US3), Delete (US4).
/// </summary>
public interface ILedgerRepository
{
    /// <summary>
    /// Inserts the ledger and writes the create audit entry in the same transaction.
    /// Returns <see cref="LedgerErrors.NameAlreadyExists"/> on a case-insensitive
    /// owner-scoped duplicate name (FR-003).
    /// </summary>
    Task<Result<DomainLedger>> CreateAsync(
        DomainLedger ledger,
        AuditEntry audit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Owner-scoped fetch by id (FR-005, FR-009). Returns
    /// <see cref="LedgerErrors.NotFound"/> when the row is absent or owned by
    /// another user — the two cases are indistinguishable to the caller.
    /// </summary>
    Task<Result<DomainLedger>> GetByIdAsync(
        Guid ledgerId,
        Guid ownerId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Owner-scoped, deterministic, keyset-paginated list (FR-006). The page
    /// cursor is opaque to callers and round-trips back through
    /// <see cref="LedgerListPage.NextPageCursor"/>.
    /// </summary>
    Task<Result<LedgerListPage>> ListAsync(
        Guid ownerId,
        bool includeArchived,
        string? pageCursor,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// Optimistic-concurrency update (FR-007 / FR-007a). Audit row written in
    /// the same transaction. Returns <see cref="LedgerErrors.Conflict"/> on a
    /// version mismatch, <see cref="LedgerErrors.NotFound"/> when the row is
    /// gone, or <see cref="LedgerErrors.NameAlreadyExists"/> on a duplicate
    /// rename collision.
    /// </summary>
    Task<Result<DomainLedger>> UpdateAsync(
        DomainLedger updated,
        long expectedVersion,
        AuditEntry audit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Hard-delete with optimistic concurrency (FR-008). Audit row remains
    /// after delete (retention purge owns that lifecycle). Returns
    /// <see cref="LedgerErrors.Conflict"/> on a version mismatch and
    /// <see cref="LedgerErrors.NotFound"/> when the row is absent.
    /// </summary>
    Task<Result> DeleteAsync(
        Guid ledgerId,
        Guid ownerId,
        long expectedVersion,
        AuditEntry audit,
        CancellationToken cancellationToken);
}
