using System.Text.Json;
using SddDemo.Ledger.Application.Abstractions.Identity;
using SddDemo.Ledger.Application.Abstractions.Observability;
using SddDemo.Ledger.Application.Abstractions.Persistence;
using SddDemo.Ledger.Domain.Auditing;
using SddDemo.Ledger.Domain.Common;
using SddDemo.Ledger.Domain.Ledgers;
using DomainLedger = SddDemo.Ledger.Domain.Ledgers.Ledger;

namespace SddDemo.Ledger.Application.Features.Ledgers.Commands.DeleteLedger;

/// <summary>
/// FR-008 — owner-scoped hard-delete. Pre-fetches the row owner-scoped so that:
/// (a) cross-owner deletes return <see cref="LedgerErrors.NotFound"/>
/// indistinguishably from missing; (b) archived ledgers reject with
/// <see cref="LedgerErrors.ArchivedCannotDelete"/>; (c) the audit row carries
/// the pre-delete snapshot. The repository performs the optimistic delete and
/// is the sole arbiter of version mismatches.
/// </summary>
public sealed class DeleteLedgerHandler(
    ILedgerRepository repository,
    ICurrentUser currentUser,
    ILedgerMetrics metrics,
    TimeProvider timeProvider,
    IServiceProvider services)
{
    public async Task<Result> Handle(
        DeleteLedgerCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var ownerResult = currentUser.ResolveOwnerId();
        if (ownerResult.IsFailure)
        {
            return Result.Failure(ownerResult.Error!);
        }

        var ownerId = ownerResult.Value;

        var currentResult = await repository
            .GetByIdAsync(command.Id, ownerId, cancellationToken)
            .ConfigureAwait(false);

        if (currentResult.IsFailure)
        {
            return Result.Failure(currentResult.Error!);
        }

        var current = currentResult.Value!;

        if (current.Status == LedgerStatus.Archived)
        {
            return Result.Failure(LedgerErrors.ArchivedCannotDelete);
        }

        var now = timeProvider.GetUtcNow();

        var auditResult = AuditEntry.Builder()
            .WithActorId(ownerId)
            .WithLedgerId(current.Id)
            .WithEventType(AuditEventType.Delete)
            .WithEventAt(now)
            .WithPayload(SerializePayload(current))
            .Build(services);

        if (auditResult.IsFailure)
        {
            return Result.Failure(auditResult.Error!);
        }

        var persisted = await repository
            .DeleteAsync(current.Id, ownerId, command.ExpectedVersion, auditResult.Value!, cancellationToken)
            .ConfigureAwait(false);

        if (persisted.IsSuccess)
        {
            metrics.RecordDeleted();
        }

        return persisted;
    }

    private static string SerializePayload(DomainLedger ledger) =>
        JsonSerializer.Serialize(new
        {
            id = ledger.Id,
            owner_id = ledger.OwnerId,
            name = ledger.Name,
            description = ledger.Description,
            currency_code = ledger.CurrencyCode,
            status = (int)ledger.Status,
            version = ledger.Version,
            created_at = ledger.CreatedAt,
            last_modified_at = ledger.LastModifiedAt,
        });
}
