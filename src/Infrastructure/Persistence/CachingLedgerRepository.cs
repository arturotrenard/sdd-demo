using SddDemo.Ledger.Application.Abstractions.Persistence;
using SddDemo.Ledger.Domain.Auditing;
using SddDemo.Ledger.Domain.Common;
using SddDemo.Ledger.Domain.Ledgers;
using ZiggyCreatures.Caching.Fusion;
using DomainLedger = SddDemo.Ledger.Domain.Ledgers.Ledger;

namespace SddDemo.Ledger.Infrastructure.Persistence;

/// <summary>
/// research.md §4 — FusionCache decorator wrapping <see cref="ILedgerRepository"/>.
/// US1 only handles the Create write path: forwards to the inner repository and
/// invalidates the owner tag on success so any cached read entries (US2 onwards)
/// are dropped. Read methods (Get/List) are added in Phase 4 alongside their
/// matching tests.
/// </summary>
public sealed class CachingLedgerRepository(
    ILedgerRepository inner,
    IFusionCacheProvider cacheProvider) : ILedgerRepository
{
    private IFusionCache Cache => cacheProvider.GetDefaultCache();

    public async Task<Result<DomainLedger>> CreateAsync(
        DomainLedger ledger,
        AuditEntry audit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        var result = await inner.CreateAsync(ledger, audit, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            await Cache
                .RemoveByTagAsync(OwnerTag(ledger.OwnerId), token: cancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }

    internal static string OwnerTag(Guid ownerId) => $"owner:{ownerId}";
}
