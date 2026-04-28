using System.ComponentModel.DataAnnotations;

namespace SddDemo.Ledger.Application.Features.Ledgers.Queries.ListLedgers;

/// <summary>
/// data-model.md §4 — Application query for FR-006 owner-scoped paginated list.
/// PageSize is constrained to [1, 200]; the request map defaults
/// <c>page_size = 0</c> to 50 before constructing the query.
/// </summary>
public sealed record ListLedgersQuery(
    bool IncludeArchived,
    string? PageCursor,
    [property: Range(1, 200)] int PageSize);
