using SddDemo.Ledger.Application.Abstractions.Persistence;
using SddDemo.Ledger.Application.Features.Ledgers.Queries.ListLedgers;
using SddDemo.Ledger.Domain.Auditing;
using SddDemo.Ledger.Domain.Common;
using SddDemo.Ledger.Domain.Ledgers;
using ZiggyCreatures.Caching.Fusion;
using DomainLedger = SddDemo.Ledger.Domain.Ledgers.Ledger;

namespace SddDemo.Ledger.Infrastructure.Persistence;

/// <summary>
/// research.md §4 — FusionCache decorator wrapping <see cref="ILedgerRepository"/>.
/// Reads (Get/List) are L1+L2 cached, tagged by <c>owner:{ownerId}</c> and (for
/// single-fetch) also <c>ledger:{ledgerId}</c>. Only <see cref="Result.Success"/>
/// is cached — <see cref="Result.Failure"/> is never persisted, so a NotFound
/// followed by an inserted row resolves on the second call. Writes
/// (Create/Update/Delete) forward to the inner repo and, on success, invalidate
/// the relevant tags.
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

    public async Task<Result<DomainLedger>> GetByIdAsync(
        Guid ledgerId,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        var key = LedgerKey(ownerId, ledgerId);

        var cached = await Cache
            .TryGetAsync<DomainLedger>(key, token: cancellationToken)
            .ConfigureAwait(false);

        if (cached.HasValue)
        {
            return Result<DomainLedger>.Success(cached.Value);
        }

        var result = await inner.GetByIdAsync(ledgerId, ownerId, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            await Cache
                .SetAsync(
                    key,
                    result.Value!,
                    tags: new[] { OwnerTag(ownerId), LedgerTag(ledgerId) },
                    token: cancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }

    public async Task<Result<LedgerListPage>> ListAsync(
        Guid ownerId,
        bool includeArchived,
        string? pageCursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var key = ListKey(ownerId, includeArchived, pageCursor, pageSize);

        var cached = await Cache
            .TryGetAsync<LedgerListPage>(key, token: cancellationToken)
            .ConfigureAwait(false);

        if (cached.HasValue)
        {
            return Result<LedgerListPage>.Success(cached.Value);
        }

        var result = await inner
            .ListAsync(ownerId, includeArchived, pageCursor, pageSize, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            await Cache
                .SetAsync(
                    key,
                    result.Value!,
                    tags: new[] { OwnerTag(ownerId) },
                    token: cancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }

    public async Task<Result<DomainLedger>> UpdateAsync(
        DomainLedger updated,
        long expectedVersion,
        AuditEntry audit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(updated);

        var result = await inner
            .UpdateAsync(updated, expectedVersion, audit, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            await Cache
                .RemoveByTagAsync(OwnerTag(updated.OwnerId), token: cancellationToken)
                .ConfigureAwait(false);
            await Cache
                .RemoveByTagAsync(LedgerTag(updated.Id), token: cancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }

    public async Task<Result> DeleteAsync(
        Guid ledgerId,
        Guid ownerId,
        long expectedVersion,
        AuditEntry audit,
        CancellationToken cancellationToken)
    {
        var result = await inner
            .DeleteAsync(ledgerId, ownerId, expectedVersion, audit, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            await Cache
                .RemoveByTagAsync(OwnerTag(ownerId), token: cancellationToken)
                .ConfigureAwait(false);
            await Cache
                .RemoveByTagAsync(LedgerTag(ledgerId), token: cancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }

    internal static string OwnerTag(Guid ownerId) => $"owner:{ownerId}";
    internal static string LedgerTag(Guid ledgerId) => $"ledger:{ledgerId}";
    internal static string LedgerKey(Guid ownerId, Guid ledgerId) =>
        $"ledger:{ownerId}:{ledgerId}";
    internal static string ListKey(Guid ownerId, bool includeArchived, string? pageCursor, int pageSize) =>
        $"ledger:list:{ownerId}:{includeArchived}:{pageCursor ?? string.Empty}:{pageSize}";
}
