// T039 (TDD red) — written before T047 (CreateLedgerHandler).
// Verifies handler resolves ICurrentUser, builds Ledger with Status=Active + Version=1,
// builds a Create AuditEntry with the post-state payload, calls CreateAsync once,
// increments ledger.created on success, and propagates failures unchanged.

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using SddDemo.Ledger.Application.Abstractions.Identity;
using SddDemo.Ledger.Application.Abstractions.Observability;
using SddDemo.Ledger.Application.Abstractions.Persistence;
using SddDemo.Ledger.Application.Features.Ledgers.Commands.CreateLedger;
using SddDemo.Ledger.Domain.Auditing;
using SddDemo.Ledger.Domain.Common;
using SddDemo.Ledger.Domain.Currency;
using SddDemo.Ledger.Domain.Ledgers;
using SddDemo.Ledger.Infrastructure.Currency;
using Xunit;
using DomainLedger = SddDemo.Ledger.Domain.Ledgers.Ledger;

namespace SddDemo.Ledger.Application.Tests.Ledgers;

public class CreateLedgerHandlerTests
{
    private static readonly Guid OwnerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly ILedgerRepository _repo = Substitute.For<ILedgerRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ILedgerMetrics _metrics = Substitute.For<ILedgerMetrics>();
    private readonly TimeProvider _clock = TimeProvider.System;
    private readonly IServiceProvider _services;

    public CreateLedgerHandlerTests()
    {
        var sc = new ServiceCollection();
        sc.AddSingleton<ICurrencyCatalog, CurrencyCatalog>();
        _services = sc.BuildServiceProvider();
    }

    private CreateLedgerHandler NewHandler() =>
        new(_repo, _currentUser, _metrics, _clock, _services);

    [Fact]
    public async Task Handle_returns_failure_when_owner_cannot_be_resolved()
    {
        _currentUser.ResolveOwnerId().Returns(Result<Guid>.Failure(
            new Error("identity.missing_owner", "no header", ErrorType.Validation)));

        var handler = NewHandler();

        var result = await handler.Handle(
            new CreateLedgerCommand("Op", null, "USD"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        await _repo.DidNotReceive().CreateAsync(
            Arg.Any<DomainLedger>(),
            Arg.Any<AuditEntry>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_builds_active_ledger_with_version_one_and_audit_create_event()
    {
        _currentUser.ResolveOwnerId().Returns(Result<Guid>.Success(OwnerId));

        DomainLedger? capturedLedger = null;
        AuditEntry? capturedAudit = null;

        _repo.CreateAsync(Arg.Any<DomainLedger>(), Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedLedger = callInfo.Arg<DomainLedger>();
                capturedAudit = callInfo.Arg<AuditEntry>();
                return Task.FromResult(Result<DomainLedger>.Success(capturedLedger));
            });

        var handler = NewHandler();

        var result = await handler.Handle(
            new CreateLedgerCommand("Op", "Primary operating ledger", "USD"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        capturedLedger.Should().NotBeNull();
        capturedLedger!.OwnerId.Should().Be(OwnerId);
        capturedLedger.Status.Should().Be(LedgerStatus.Active);
        capturedLedger.Version.Should().Be(1);
        capturedLedger.Name.Should().Be("Op");
        capturedLedger.CurrencyCode.Should().Be("USD");

        capturedAudit.Should().NotBeNull();
        capturedAudit!.ActorId.Should().Be(OwnerId);
        capturedAudit.LedgerId.Should().Be(capturedLedger.Id);
        capturedAudit.EventType.Should().Be(AuditEventType.Create);

        await _repo.Received(1).CreateAsync(
            Arg.Any<DomainLedger>(),
            Arg.Any<AuditEntry>(),
            Arg.Any<CancellationToken>());

        _metrics.Received(1).RecordCreated();
    }

    [Fact]
    public async Task Handle_propagates_repository_failure_unchanged()
    {
        _currentUser.ResolveOwnerId().Returns(Result<Guid>.Success(OwnerId));

        var failure = new Error("ledger.name_already_exists", "dup", ErrorType.Conflict);
        _repo.CreateAsync(Arg.Any<DomainLedger>(), Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<DomainLedger>.Failure(failure)));

        var handler = NewHandler();

        var result = await handler.Handle(
            new CreateLedgerCommand("Op", null, "USD"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(failure);
        _metrics.DidNotReceive().RecordCreated();
    }

}

