// T052 (US2) — covers GetLedgerHandler with NSubstitute fakes.
// Verifies owner-scoping, propagation of NotFound, and propagation of an
// owner-resolution failure.

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SddDemo.Ledger.Application.Abstractions.Identity;
using SddDemo.Ledger.Application.Abstractions.Persistence;
using SddDemo.Ledger.Application.Features.Ledgers.Queries.GetLedger;
using SddDemo.Ledger.Domain.Common;
using SddDemo.Ledger.Domain.Currency;
using SddDemo.Ledger.Domain.Ledgers;
using SddDemo.Ledger.Infrastructure.Currency;
using Xunit;
using DomainLedger = SddDemo.Ledger.Domain.Ledgers.Ledger;

namespace SddDemo.Ledger.Application.Tests.Ledgers;

public class GetLedgerHandlerTests
{
    private static readonly Guid OwnerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly ILedgerRepository _repo = Substitute.For<ILedgerRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IServiceProvider _services;

    public GetLedgerHandlerTests()
    {
        var sc = new ServiceCollection();
        sc.AddSingleton<ICurrencyCatalog, CurrencyCatalog>();
        _services = sc.BuildServiceProvider();
    }

    private GetLedgerHandler NewHandler() => new(_repo, _currentUser);

    [Fact]
    public async Task Handle_propagates_failure_when_owner_cannot_be_resolved()
    {
        _currentUser.ResolveOwnerId().Returns(Result<Guid>.Failure(
            new Error("identity.missing_owner", "no header", ErrorType.Validation)));

        var result = await NewHandler().Handle(
            new GetLedgerQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        await _repo.DidNotReceive().GetByIdAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_forwards_query_with_resolved_owner_id()
    {
        _currentUser.ResolveOwnerId().Returns(Result<Guid>.Success(OwnerId));
        var ledgerId = Guid.NewGuid();

        var ledger = BuildLedger(OwnerId, ledgerId, _services).Value!;
        _repo.GetByIdAsync(ledgerId, OwnerId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<DomainLedger>.Success(ledger)));

        var result = await NewHandler().Handle(
            new GetLedgerQuery(ledgerId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(ledgerId);
    }

    [Fact]
    public async Task Handle_propagates_NotFound_unchanged()
    {
        _currentUser.ResolveOwnerId().Returns(Result<Guid>.Success(OwnerId));
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<DomainLedger>.Failure(LedgerErrors.NotFound)));

        var result = await NewHandler().Handle(
            new GetLedgerQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("ledger.not_found");
    }

    private static Result<DomainLedger> BuildLedger(Guid ownerId, Guid id, IServiceProvider services) =>
        DomainLedger.Builder()
            .WithId(id)
            .WithOwnerId(ownerId)
            .WithName("X")
            .WithDescription(null)
            .WithCurrencyCode("USD")
            .WithStatus(LedgerStatus.Active)
            .WithVersion(1)
            .WithTimestamps(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
            .Build(services);
}
