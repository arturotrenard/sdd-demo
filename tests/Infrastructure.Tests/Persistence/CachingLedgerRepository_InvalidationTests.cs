// T064 (US3) — covers CachingLedgerRepository invalidation on Update/Delete.
// A successful update invalidates both owner:{ownerId} and ledger:{ledgerId}
// tags so subsequent GetByIdAsync / ListAsync miss L1 and re-load from inner.

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

public class CachingLedgerRepository_InvalidationTests
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

    private static DomainLedger Build(Guid id, IServiceProvider services, long version = 1)
    {
        var now = DateTimeOffset.UtcNow;
        return DomainLedger.Builder()
            .WithId(id)
            .WithOwnerId(OwnerId)
            .WithName("X")
            .WithDescription(null)
            .WithCurrencyCode("USD")
            .WithStatus(LedgerStatus.Active)
            .WithVersion(version)
            .WithTimestamps(now, now)
            .Build(services).Value!;
    }

    [Fact]
    public async Task UpdateAsync_invalidates_owner_and_ledger_tags()
    {
        var (sut, inner, services) = BuildSut();
        var ledgerId = Guid.NewGuid();
        var initial = Build(ledgerId, services, version: 1);
        var updated = Build(ledgerId, services, version: 2);

        inner.GetByIdAsync(ledgerId, OwnerId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<DomainLedger>.Success(initial)));
        inner.ListAsync(OwnerId, Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<LedgerListPage>.Success(
                new LedgerListPage(new[] { initial }, null))));
        inner.UpdateAsync(updated, 1, Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<DomainLedger>.Success(updated)));

        // Warm both caches.
        await sut.GetByIdAsync(ledgerId, OwnerId, CancellationToken.None);
        await sut.ListAsync(OwnerId, false, null, 50, CancellationToken.None);

        await inner.Received(1).GetByIdAsync(ledgerId, OwnerId, Arg.Any<CancellationToken>());
        await inner.Received(1).ListAsync(OwnerId, false, null, 50, Arg.Any<CancellationToken>());

        // Second cached read — inner still 1.
        await sut.GetByIdAsync(ledgerId, OwnerId, CancellationToken.None);
        await sut.ListAsync(OwnerId, false, null, 50, CancellationToken.None);
        await inner.Received(1).GetByIdAsync(ledgerId, OwnerId, Arg.Any<CancellationToken>());
        await inner.Received(1).ListAsync(OwnerId, false, null, 50, Arg.Any<CancellationToken>());

        // Update invalidates both tags.
        var auditDummy = AuditEntry.Builder()
            .WithActorId(OwnerId)
            .WithLedgerId(ledgerId)
            .WithEventType(AuditEventType.Update)
            .WithEventAt(DateTimeOffset.UtcNow)
            .WithPayload("{}")
            .Build().Value!;

        var updateResult = await sut.UpdateAsync(updated, 1, auditDummy, CancellationToken.None);
        updateResult.IsSuccess.Should().BeTrue();

        // Subsequent reads miss L1 — inner gets a second call for each.
        await sut.GetByIdAsync(ledgerId, OwnerId, CancellationToken.None);
        await sut.ListAsync(OwnerId, false, null, 50, CancellationToken.None);

        await inner.Received(2).GetByIdAsync(ledgerId, OwnerId, Arg.Any<CancellationToken>());
        await inner.Received(2).ListAsync(OwnerId, false, null, 50, Arg.Any<CancellationToken>());
    }
}
