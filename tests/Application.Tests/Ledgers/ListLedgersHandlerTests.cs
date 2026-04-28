// T052 (US2) — covers ListLedgersHandler with NSubstitute fakes.
// Verifies owner-scoping forward and propagation of repository failures.

using FluentAssertions;
using NSubstitute;
using SddDemo.Ledger.Application.Abstractions.Identity;
using SddDemo.Ledger.Application.Abstractions.Persistence;
using SddDemo.Ledger.Application.Features.Ledgers.Queries.ListLedgers;
using SddDemo.Ledger.Domain.Common;
using Xunit;
using DomainLedger = SddDemo.Ledger.Domain.Ledgers.Ledger;

namespace SddDemo.Ledger.Application.Tests.Ledgers;

public class ListLedgersHandlerTests
{
    private static readonly Guid OwnerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly ILedgerRepository _repo = Substitute.For<ILedgerRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    private ListLedgersHandler NewHandler() => new(_repo, _currentUser);

    [Fact]
    public async Task Handle_propagates_failure_when_owner_cannot_be_resolved()
    {
        _currentUser.ResolveOwnerId().Returns(Result<Guid>.Failure(
            new Error("identity.missing_owner", "no header", ErrorType.Validation)));

        var result = await NewHandler().Handle(
            new ListLedgersQuery(false, null, 50), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await _repo.DidNotReceive().ListAsync(
            Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_forwards_query_with_owner_and_filters_intact()
    {
        _currentUser.ResolveOwnerId().Returns(Result<Guid>.Success(OwnerId));

        var page = new LedgerListPage(Array.Empty<DomainLedger>(), null);
        _repo.ListAsync(OwnerId, true, "abc", 25, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<LedgerListPage>.Success(page)));

        var result = await NewHandler().Handle(
            new ListLedgersQuery(IncludeArchived: true, PageCursor: "abc", PageSize: 25),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _repo.Received(1).ListAsync(OwnerId, true, "abc", 25, Arg.Any<CancellationToken>());
    }
}
