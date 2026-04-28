using DomainLedger = SddDemo.Ledger.Domain.Ledgers.Ledger;

namespace SddDemo.Ledger.Application.Features.Ledgers.Queries.ListLedgers;

/// <summary>
/// data-model.md §4 — immutable page DTO returned by
/// <c>ILedgerRepository.ListAsync</c>. <see cref="NextPageCursor"/> is null when
/// there are no further pages.
/// </summary>
public sealed record LedgerListPage(
    IReadOnlyList<DomainLedger> Items,
    string? NextPageCursor);
