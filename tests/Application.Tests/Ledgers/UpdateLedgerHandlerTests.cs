// T062 (US3) — covers UpdateLedgerHandler with NSubstitute fakes against the
// state machine in data-model.md §1.1.

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SddDemo.Ledger.Application.Abstractions.Identity;
using SddDemo.Ledger.Application.Abstractions.Observability;
using SddDemo.Ledger.Application.Abstractions.Persistence;
using SddDemo.Ledger.Application.Features.Ledgers.Commands.UpdateLedger;
using SddDemo.Ledger.Domain.Auditing;
using SddDemo.Ledger.Domain.Common;
using SddDemo.Ledger.Domain.Currency;
using SddDemo.Ledger.Domain.Ledgers;
using SddDemo.Ledger.Infrastructure.Currency;
using Xunit;
using DomainLedger = SddDemo.Ledger.Domain.Ledgers.Ledger;

namespace SddDemo.Ledger.Application.Tests.Ledgers;

public class UpdateLedgerHandlerTests
{
    private static readonly Guid OwnerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly ILedgerRepository _repo = Substitute.For<ILedgerRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ILedgerMetrics _metrics = Substitute.For<ILedgerMetrics>();
    private readonly TimeProvider _clock = TimeProvider.System;
    private readonly IServiceProvider _services;

    public UpdateLedgerHandlerTests()
    {
        var sc = new ServiceCollection();
        sc.AddSingleton<ICurrencyCatalog, CurrencyCatalog>();
        _services = sc.BuildServiceProvider();
    }

    private UpdateLedgerHandler NewHandler() =>
        new(_repo, _currentUser, _metrics, _clock, _services);

    [Fact]
    public async Task Handle_active_rename_succeeds_and_increments_updated()
    {
        var current = SeedActive("Old", LedgerStatus.Active, version: 3);
        StubOwnerAndCurrent(current);
        StubUpdateSuccess();

        var result = await NewHandler().Handle(
            new UpdateLedgerCommand(current.Id, ExpectedVersion: 3, Name: "New", Description: null, Status: null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _metrics.Received(1).RecordUpdated();
        _metrics.DidNotReceive().RecordArchived();
    }

    [Fact]
    public async Task Handle_active_to_archived_increments_archived()
    {
        var current = SeedActive("X", LedgerStatus.Active, version: 1);
        StubOwnerAndCurrent(current);
        StubUpdateSuccess();

        var result = await NewHandler().Handle(
            new UpdateLedgerCommand(current.Id, 1, null, null, LedgerStatus.Archived),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _metrics.Received(1).RecordArchived();
    }

    [Fact]
    public async Task Handle_archived_rename_returns_ArchivedReadOnly()
    {
        var current = SeedActive("X", LedgerStatus.Archived, version: 1);
        StubOwnerAndCurrent(current);

        var result = await NewHandler().Handle(
            new UpdateLedgerCommand(current.Id, 1, Name: "Renamed", Description: null, Status: null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("ledger.archived.read_only");
        await _repo.DidNotReceive().UpdateAsync(
            Arg.Any<DomainLedger>(), Arg.Any<long>(), Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_archived_to_active_un_archive_succeeds()
    {
        var current = SeedActive("X", LedgerStatus.Archived, version: 2);
        StubOwnerAndCurrent(current);
        StubUpdateSuccess();

        var result = await NewHandler().Handle(
            new UpdateLedgerCommand(current.Id, 2, null, null, LedgerStatus.Active),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_returns_NotFound_when_repository_returns_NotFound_no_information_leak()
    {
        _currentUser.ResolveOwnerId().Returns(Result<Guid>.Success(OwnerId));
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<DomainLedger>.Failure(LedgerErrors.NotFound)));

        var result = await NewHandler().Handle(
            new UpdateLedgerCommand(Guid.NewGuid(), 1, null, null, null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("ledger.not_found");
    }

    [Fact]
    public async Task Handle_propagates_Conflict_from_repository_unchanged()
    {
        var current = SeedActive("X", LedgerStatus.Active, version: 5);
        StubOwnerAndCurrent(current);
        _repo.UpdateAsync(Arg.Any<DomainLedger>(), Arg.Any<long>(), Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<DomainLedger>.Failure(LedgerErrors.Conflict)));

        var result = await NewHandler().Handle(
            new UpdateLedgerCommand(current.Id, 5, Name: "X2", Description: null, Status: null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("ledger.conflict");
        _metrics.DidNotReceive().RecordUpdated();
    }

    private DomainLedger SeedActive(string name, LedgerStatus status, long version)
    {
        var now = DateTimeOffset.UtcNow;
        return DomainLedger.Builder()
            .WithId(Guid.NewGuid())
            .WithOwnerId(OwnerId)
            .WithName(name)
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

    private void StubUpdateSuccess()
    {
        _repo.UpdateAsync(Arg.Any<DomainLedger>(), Arg.Any<long>(), Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(
                Result<DomainLedger>.Success(callInfo.Arg<DomainLedger>())));
    }
}
