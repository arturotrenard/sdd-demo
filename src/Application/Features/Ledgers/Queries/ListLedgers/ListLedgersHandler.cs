using SddDemo.Ledger.Application.Abstractions.Identity;
using SddDemo.Ledger.Application.Abstractions.Persistence;
using SddDemo.Ledger.Domain.Common;

namespace SddDemo.Ledger.Application.Features.Ledgers.Queries.ListLedgers;

/// <summary>
/// FR-006 — owner-scoped, keyset-paginated list. The repository performs the
/// cursor decode and the LIMIT@PageSize+1 trick; the handler only resolves the
/// owner and forwards. Registered <c>AddScoped&lt;ListLedgersHandler&gt;()</c>.
/// </summary>
public sealed class ListLedgersHandler(
    ILedgerRepository repository,
    ICurrentUser currentUser)
{
    public async Task<Result<LedgerListPage>> Handle(
        ListLedgersQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var ownerResult = currentUser.ResolveOwnerId();
        if (ownerResult.IsFailure)
        {
            return Result<LedgerListPage>.Failure(ownerResult.Error!);
        }

        return await repository
            .ListAsync(
                ownerResult.Value,
                query.IncludeArchived,
                query.PageCursor,
                query.PageSize,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
