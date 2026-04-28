using System.ComponentModel.DataAnnotations;
using SddDemo.Ledger.Domain.Currency;
using SddDemo.Ledger.Domain.Ledgers;

namespace SddDemo.Ledger.Application.Features.Ledgers.Commands.UpdateLedger;

/// <summary>
/// data-model.md §4 — Application command for FR-007 / FR-007a ledger update.
/// <see cref="ExpectedVersion"/> is the long value decoded from the wire's
/// <c>version_token</c>. The optional fields are <c>null</c> when the caller's
/// <c>update_mask</c> did not list them; the handler treats <c>null</c> as
/// "leave as-is".
/// </summary>
public sealed record UpdateLedgerCommand(
    [property: Required] Guid Id,
    [property: Range(1, long.MaxValue)] long ExpectedVersion,
    [property: StringLength(100, MinimumLength = 1)] string? Name,
    [property: StringLength(500)] string? Description,
    LedgerStatus? Status);
