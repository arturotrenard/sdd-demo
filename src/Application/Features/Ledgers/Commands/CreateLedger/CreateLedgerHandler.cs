using System.Text.Json;
using SddDemo.Ledger.Application.Abstractions.Identity;
using SddDemo.Ledger.Application.Abstractions.Observability;
using SddDemo.Ledger.Application.Abstractions.Persistence;
using SddDemo.Ledger.Domain.Auditing;
using SddDemo.Ledger.Domain.Common;
using SddDemo.Ledger.Domain.Ledgers;
using DomainLedger = SddDemo.Ledger.Domain.Ledgers.Ledger;

namespace SddDemo.Ledger.Application.Features.Ledgers.Commands.CreateLedger;

/// <summary>
/// FR-001 / FR-012 — creates a new ledger for the resolved owner, builds the
/// matching <see cref="AuditEventType.Create"/> entry with the post-state JSON
/// snapshot, dispatches to the repository (which writes both rows in one
/// transaction), and ticks the <c>ledger.created</c> counter on success.
/// Registered <c>AddScoped&lt;CreateLedgerHandler&gt;()</c>.
/// </summary>
public sealed class CreateLedgerHandler(
    ILedgerRepository repository,
    ICurrentUser currentUser,
    ILedgerMetrics metrics,
    TimeProvider timeProvider,
    IServiceProvider services)
{
    public async Task<Result<DomainLedger>> Handle(
        CreateLedgerCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var ownerResult = currentUser.ResolveOwnerId();
        if (ownerResult.IsFailure)
        {
            return Result<DomainLedger>.Failure(ownerResult.Error!);
        }

        var ownerId = ownerResult.Value;
        var now = timeProvider.GetUtcNow();
        var id = Guid.CreateVersion7();

        var ledgerResult = DomainLedger.Builder()
            .WithId(id)
            .WithOwnerId(ownerId)
            .WithName(command.Name)
            .WithDescription(command.Description)
            .WithCurrencyCode(command.CurrencyCode)
            .WithStatus(LedgerStatus.Active)
            .WithVersion(1)
            .WithTimestamps(now, now)
            .Build(services);

        if (ledgerResult.IsFailure)
        {
            return ledgerResult;
        }

        var ledger = ledgerResult.Value!;

        var auditResult = AuditEntry.Builder()
            .WithActorId(ownerId)
            .WithLedgerId(ledger.Id)
            .WithEventType(AuditEventType.Create)
            .WithEventAt(now)
            .WithPayload(SerializePayload(ledger))
            .Build(services);

        if (auditResult.IsFailure)
        {
            return Result<DomainLedger>.Failure(auditResult.Error!);
        }

        var persisted = await repository
            .CreateAsync(ledger, auditResult.Value!, cancellationToken)
            .ConfigureAwait(false);

        if (persisted.IsSuccess)
        {
            metrics.RecordCreated();
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
