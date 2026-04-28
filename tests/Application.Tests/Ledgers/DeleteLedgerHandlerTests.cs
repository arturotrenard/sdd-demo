// T070 (US4) — covers DeleteLedgerHandler with NSubstitute fakes.
// active-delete success, archived rejection, owner-mismatch → NotFound,
// version-mismatch → Conflict.

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SddDemo.Ledger.Application.Abstractions.Identity;
using SddDemo.Ledger.Application.Abstractions.Observability;
using SddDemo.Ledger.Application.Abstractions.Persistence;
using SddDemo.Ledger.Application.Features.Ledgers.Commands.DeleteLedger;
using SddDemo.Ledger.Domain.Auditing;
using SddDemo.Ledger.Domain.Common;
using SddDemo.Ledger.Domain.Currency;
using SddDemo.Ledger.Domain.Ledgers;
using SddDemo.Ledger.Infrastructure.Currency;
using Xunit;
using DomainLedger = SddDemo.Ledger.Domain.Ledgers.Ledger;

namespace SddDemo.Ledger.Application.Tests.Ledgers;

public class DeleteLedgerHandlerTests
{
    private static readonly Guid OwnerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly ILedgerRepository _repo = Substitute.For<ILedgerRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ILedgerMetrics _metrics = Substitute.For<ILedgerMetrics>();
    private readonly TimeProvider _clock = TimeProvider.System;
    private readonly IServiceProvider _services;

    public DeleteLedgerHandlerTests()
    {
        var sc = new ServiceCollection();
        sc.AddSingleton<ICurrencyCatalog, CurrencyCatalog>();
        _services = sc.BuildServiceProvider();
    }

    private DeleteLedgerHandler NewHandler() =>
        new(_repo, _currentUser, _metrics, _clock, _services);

    [Fact]
    public async Task Handle_active_delete_succeeds_and_increments_deleted()
    {
        var current = SeedActive(LedgerStatus.Active, version: 3);
        StubOwnerAndCurrent(current);
        _repo.DeleteAsync(current.Id, OwnerId, 3, Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success()));

        var result = await NewHandler().Handle(new DeleteLedgerCommand(current.Id, 3), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _metrics.Received(1).RecordDeleted();
    }

    [Fact]
    public async Task Handle_archived_returns_ArchivedCannotDelete()
    {
        var current = SeedActive(LedgerStatus.Archived, version: 1);
        StubOwnerAndCurrent(current);

        var result = await NewHandler().Handle(new DeleteLedgerCommand(current.Id, 1), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("ledger.archived.cannot_delete");
        await _repo.DidNotReceive().DeleteAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_owner_mismatch_returns_NotFound_no_information_leak()
    {
        _currentUser.ResolveOwnerId().Returns(Result<Guid>.Success(OwnerId));
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<DomainLedger>.Failure(LedgerErrors.NotFound)));

        var result = await NewHandler().Handle(new DeleteLedgerCommand(Guid.NewGuid(), 1), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("ledger.not_found");
    }

    [Fact]
    public async Task Handle_propagates_Conflict_from_repository()
    {
        var current = SeedActive(LedgerStatus.Active, version: 5);
        StubOwnerAndCurrent(current);
        _repo.DeleteAsync(current.Id, OwnerId, 5, Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure(LedgerErrors.Conflict)));

        var result = await NewHandler().Handle(new DeleteLedgerCommand(current.Id, 5), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("ledger.conflict");
        _metrics.DidNotReceive().RecordDeleted();
    }

    private DomainLedger SeedActive(LedgerStatus status, long version)
    {
        var now = DateTimeOffset.UtcNow;
        return DomainLedger.Builder()
            .WithId(Guid.NewGuid())
            .WithOwnerId(OwnerId)
            .WithName("X")
            .WithDescription(null)
            .WithCurrencyCode("USD")
            .WithStatus(status)
            .WithVersion(version)
            .WithTimestamps(now, now)
            .Build(_services).Value!;
    }

    private void StubOwnerAndCurrent(DomainLedger current)
    {
        _currentUser.ResolveOwnerId().Returns(Result<Guid>.Success(OwnerId));
        _repo.GetByIdAsync(current.Id, OwnerId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<DomainLedger>.Success(current)));
    }
}
