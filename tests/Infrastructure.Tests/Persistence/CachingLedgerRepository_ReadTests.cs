// T054 (US2) — covers CachingLedgerRepository read-through behaviour with a
// real FusionCache (L1 only, no Redis required). The inner repo is an
// NSubstitute mock so call-counts prove cache hits.
//
// Asserts:
//   - First GetByIdAsync calls inner; second hits L1 (inner not called).
//   - Only Result.Success is cached — a NotFound followed by an inserted row
//     resolves on the next call.
//   - List-page caching keys differ by (includeArchived, pageCursor, pageSize).

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SddDemo.Ledger.Application.Abstractions.Persistence;
using SddDemo.Ledger.Application.Features.Ledgers.Queries.ListLedgers;
using SddDemo.Ledger.Domain.Auditing;
using SddDemo.Ledger.Domain.Common;
using SddDemo.Ledger.Domain.Currency;
using SddDemo.Ledger.Domain.Ledgers;
using SddDemo.Ledger.Infrastructure.Currency;
using SddDemo.Ledger.Infrastructure.Persistence;
using Xunit;
using ZiggyCreatures.Caching.Fusion;
using DomainLedger = SddDemo.Ledger.Domain.Ledgers.Ledger;

namespace SddDemo.Ledger.Infrastructure.Tests.Persistence;

public class CachingLedgerRepository_ReadTests
{
    private static readonly Guid OwnerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static (CachingLedgerRepository sut, ILedgerRepository inner, IServiceProvider services)
        BuildSut()
    {
        var inner = Substitute.For<ILedgerRepository>();

        var sc = new ServiceCollection();
        sc.AddFusionCache().WithDefaultEntryOptions(o =>
        {
            o.Duration = TimeSpan.FromMinutes(5);
        });
        sc.AddSingleton<ICurrencyCatalog, CurrencyCatalog>();
        var sp = sc.BuildServiceProvider();

        var provider = sp.GetRequiredService<IFusionCacheProvider>();
        return (new CachingLedgerRepository(inner, provider), inner, sp);
    }

    private static DomainLedger NewLedger(Guid id, IServiceProvider services)
    {
        var now = DateTimeOffset.UtcNow;
        return DomainLedger.Builder()
            .WithId(id)
            .WithOwnerId(OwnerId)
            .WithName("X")
            .WithDescription(null)
            .WithCurrencyCode("USD")
            .WithStatus(LedgerStatus.Active)
            .WithVersion(1)
            .WithTimestamps(now, now)
            .Build(services).Value!;
    }

    [Fact]
    public async Task GetByIdAsync_second_call_hits_L1_and_does_not_invoke_inner_repo()
    {
        var (sut, inner, services) = BuildSut();
        var ledgerId = Guid.NewGuid();
        var ledger = NewLedger(ledgerId, services);

        inner.GetByIdAsync(ledgerId, OwnerId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<DomainLedger>.Success(ledger)));

        var first = await sut.GetByIdAsync(ledgerId, OwnerId, CancellationToken.None);
        var second = await sut.GetByIdAsync(ledgerId, OwnerId, CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        await inner.Received(1).GetByIdAsync(ledgerId, OwnerId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByIdAsync_failure_is_not_cached_subsequent_success_resolves()
    {
        var (sut, inner, services) = BuildSut();
        var ledgerId = Guid.NewGuid();
        var ledger = NewLedger(ledgerId, services);

        inner.GetByIdAsync(ledgerId, OwnerId, Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(Result<DomainLedger>.Failure(LedgerErrors.NotFound)),
                Task.FromResult(Result<DomainLedger>.Success(ledger)));

        var first = await sut.GetByIdAsync(ledgerId, OwnerId, CancellationToken.None);
        var second = await sut.GetByIdAsync(ledgerId, OwnerId, CancellationToken.None);

        first.IsFailure.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        await inner.Received(2).GetByIdAsync(ledgerId, OwnerId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAsync_uses_distinct_keys_for_different_filters()
    {
        var (sut, inner, services) = BuildSut();
        var page = new LedgerListPage(new[] { NewLedger(Guid.NewGuid(), services) }, null);
        inner.ListAsync(OwnerId, Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<LedgerListPage>.Success(page)));

        await sut.ListAsync(OwnerId, false, null, 50, CancellationToken.None);
        await sut.ListAsync(OwnerId, true, null, 50, CancellationToken.None);
        await sut.ListAsync(OwnerId, false, "cursor1", 50, CancellationToken.None);
        await sut.ListAsync(OwnerId, false, null, 25, CancellationToken.None);

        // Each tuple is a distinct cache key — inner called 4 times.
        await inner.Received(4).ListAsync(
            OwnerId, Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>());

        // A second identical call hits L1.
        await sut.ListAsync(OwnerId, false, null, 50, CancellationToken.None);
        await inner.Received(4).ListAsync(
            OwnerId, Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
