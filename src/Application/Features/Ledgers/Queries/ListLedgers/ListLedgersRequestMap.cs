using System.ComponentModel.DataAnnotations;
using SddDemo.Ledger.Domain.Common;

namespace SddDemo.Ledger.Application.Features.Ledgers.Queries.ListLedgers;

/// <summary>
/// data-model.md §4.1 — translation record between the protobuf
/// <c>ListLedgersRequest</c> and the Application <see cref="ListLedgersQuery"/>.
/// Defaults <c>page_size = 0</c> to 50; rejects values outside [1, 200] with a
/// <c>Validation</c> failure (Range attribute).
/// </summary>
public sealed record ListLedgersRequestMap(
    bool IncludeArchived,
    string? PageCursor,
    [property: Range(1, 200)] int PageSize)
{
    private const int DefaultPageSize = 50;

    public static Result<ListLedgersRequestMap> From(
        bool includeArchived,
        string? pageCursor,
        int pageSize,
        IServiceProvider services)
    {
        var normalisedSize = pageSize <= 0 ? DefaultPageSize : pageSize;
        var normalisedCursor = string.IsNullOrWhiteSpace(pageCursor) ? null : pageCursor;

        var dto = new ListLedgersRequestMap(includeArchived, normalisedCursor, normalisedSize);
        return DomainValidator.Validate(dto, services);
    }

    public ListLedgersQuery ToQuery() => new(IncludeArchived, PageCursor, PageSize);
}
