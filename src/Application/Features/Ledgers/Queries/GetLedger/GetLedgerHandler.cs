using SddDemo.Ledger.Application.Abstractions.Identity;
using SddDemo.Ledger.Application.Abstractions.Persistence;
using SddDemo.Ledger.Domain.Common;
using DomainLedger = SddDemo.Ledger.Domain.Ledgers.Ledger;

namespace SddDemo.Ledger.Application.Features.Ledgers.Queries.GetLedger;

/// <summary>
/// FR-005 / FR-009 — owner-scoped fetch of a single ledger. The repository
/// returns <c>Result.Failure(LedgerErrors.NotFound)</c> indistinguishably for
/// "missing" and "not owned by caller" so the handler does not need to add any
/// extra guards. Registered <c>AddScoped&lt;GetLedgerHandler&gt;()</c>.
/// </summary>
public sealed class GetLedgerHandler(
    ILedgerRepository repository,
    ICurrentUser currentUser)
{
    public async Task<Result<DomainLedger>> Handle(
        GetLedgerQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var ownerResult = currentUser.ResolveOwnerId();
        if (ownerResult.IsFailure)
        {
            return Result<DomainLedger>.Failure(ownerResult.Error!);
        }

        return await repository
            .GetByIdAsync(query.Id, ownerResult.Value, cancellationToken)
            .ConfigureAwait(false);
    }
}
