using System.Text.Json;
using SddDemo.Ledger.Application.Abstractions.Identity;
using SddDemo.Ledger.Application.Abstractions.Observability;
using SddDemo.Ledger.Application.Abstractions.Persistence;
using SddDemo.Ledger.Domain.Auditing;
using SddDemo.Ledger.Domain.Common;
using SddDemo.Ledger.Domain.Ledgers;
using DomainLedger = SddDemo.Ledger.Domain.Ledgers.Ledger;

namespace SddDemo.Ledger.Application.Features.Ledgers.Commands.UpdateLedger;

/// <summary>
/// FR-007 / FR-007a — owner-scoped update of an existing ledger. Loads the
/// current row, enforces the Active/Archived state machine (data-model.md §1.1),
/// builds the new <see cref="DomainLedger"/> with <c>Version = current + 1</c>
/// and an updated timestamp, builds an Update <see cref="AuditEntry"/> with the
/// post-state JSON snapshot, and dispatches through
/// <see cref="ILedgerRepository.UpdateAsync"/>. The repository is the sole
/// arbiter of optimistic concurrency.
/// </summary>
public sealed class UpdateLedgerHandler(
    ILedgerRepository repository,
    ICurrentUser currentUser,
    ILedgerMetrics metrics,
    TimeProvider timeProvider,
    IServiceProvider services)
{
    public async Task<Result<DomainLedger>> Handle(
        UpdateLedgerCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var ownerResult = currentUser.ResolveOwnerId();
        if (ownerResult.IsFailure)
        {
            return Result<DomainLedger>.Failure(ownerResult.Error!);
        }

        var ownerId = ownerResult.Value;

        var currentResult = await repository
            .GetByIdAsync(command.Id, ownerId, cancellationToken)
            .ConfigureAwait(false);

        if (currentResult.IsFailure)
        {
            return currentResult;
        }

        var current = currentResult.Value!;

        var transitionResult = ApplyStateMachine(current, command);
        if (transitionResult.IsFailure)
        {
            return Result<DomainLedger>.Failure(transitionResult.Error!);
        }

        var (newName, newDescription, newStatus) = transitionResult.Value;

        var now = timeProvider.GetUtcNow();

        // Optimistic concurrency: the client's expected_version is what the database
        // arbitrates against. Using current.Version here (the row we just read) would
        // ignore the client's token entirely. The repository's UPDATE...WHERE version =
        // @ExpectedVersion is the single source of truth for conflict detection.
        var nextResult = DomainLedger.Builder()
            .WithId(current.Id)
            .WithOwnerId(current.OwnerId)
            .WithName(newName)
            .WithDescription(newDescription)
            .WithCurrencyCode(current.CurrencyCode)
            .WithStatus(newStatus)
            .WithVersion(command.ExpectedVersion + 1)
            .WithTimestamps(current.CreatedAt, now)
            .Build(services);

        if (nextResult.IsFailure)
        {
            return nextResult;
        }

        var next = nextResult.Value!;

        var auditResult = AuditEntry.Builder()
            .WithActorId(ownerId)
            .WithLedgerId(next.Id)
            .WithEventType(AuditEventType.Update)
            .WithEventAt(now)
            .WithPayload(SerializePayload(next))
            .Build(services);

        if (auditResult.IsFailure)
        {
            return Result<DomainLedger>.Failure(auditResult.Error!);
        }

        var persisted = await repository
            .UpdateAsync(next, command.ExpectedVersion, auditResult.Value!, cancellationToken)
            .ConfigureAwait(false);

        if (persisted.IsSuccess)
        {
            metrics.RecordUpdated();
            if (current.Status == LedgerStatus.Active && newStatus == LedgerStatus.Archived)
            {
                metrics.RecordArchived();
            }
        }

        return persisted;
    }

    private static Result<(string Name, string? Description, LedgerStatus Status)>
        ApplyStateMachine(DomainLedger current, UpdateLedgerCommand command)
    {
        if (current.Status == LedgerStatus.Archived)
        {
            // Archived → only un-archive (status=Active) is allowed; every other
            // attribute change is rejected with ArchivedReadOnly.
            var unarchiveOnly =
                command.Status is LedgerStatus.Active
                && command.Name is null
                && command.Description is null;

            if (!unarchiveOnly)
            {
                return Result<(string, string?, LedgerStatus)>.Failure(
                    LedgerErrors.ArchivedReadOnly);
            }

            return Result<(string, string?, LedgerStatus)>.Success(
                (current.Name, current.Description, LedgerStatus.Active));
        }

        // Active → Active (attribute change) or Active → Archived (status change).
        var name = command.Name ?? current.Name;
        var description = command.Description ?? current.Description;
        var status = command.Status ?? LedgerStatus.Active;

        return Result<(string, string?, LedgerStatus)>.Success((name, description, status));
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
