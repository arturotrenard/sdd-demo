using SddDemo.Ledger.Domain.Common;

namespace SddDemo.Ledger.Application.Abstractions.Persistence;

/// <summary>
/// data-model.md §5 — public ledger repository contract. Implementations land in Phase 3+:
/// LedgerRepository (Dapper) and CachingLedgerRepository (FusionCache decorator wired via
/// Scrutor). Domain types and audit entries are written in the same transaction
/// (research.md §6).
/// </summary>
/// <remarks>
/// Concrete signatures use Object placeholders for Ledger / AuditEntry / LedgerListPage —
/// the real Domain/DTO types land in user-story phases (T044, T045, T057).
/// This interface is intentionally minimal until those types exist; refer to
/// data-model.md §5 for the canonical signatures the user-story phases will introduce.
/// </remarks>
public interface ILedgerRepository
{
    // CreateAsync / UpdateAsync / DeleteAsync / GetByIdAsync / ListAsync land in
    // Phases 3-6 (US1..US4) when Ledger, AuditEntry, and LedgerListPage exist.
    // Marker method ensures the interface compiles until then.
    Task<Result> PingAsync(CancellationToken cancellationToken);
}
