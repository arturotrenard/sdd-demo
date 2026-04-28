using SddDemo.Ledger.Domain.Auditing;
using SddDemo.Ledger.Domain.Common;
using SddDemo.Ledger.Domain.Ledgers;
using DomainLedger = SddDemo.Ledger.Domain.Ledgers.Ledger;

namespace SddDemo.Ledger.Application.Abstractions.Persistence;

/// <summary>
/// data-model.md §5 — public ledger repository contract. The Phase 3 surface only
/// covers <see cref="CreateAsync"/>; later story phases add Get/List (US2),
/// Update (US3), and Delete (US4).
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
}
