using System.ComponentModel.DataAnnotations;

namespace SddDemo.Ledger.Application.Features.Ledgers.Commands.DeleteLedger;

/// <summary>
/// data-model.md §4 — Application command for FR-008 hard-delete.
/// <see cref="ExpectedVersion"/> is the long value decoded from the wire's
/// <c>version_token</c>.
/// </summary>
public sealed record DeleteLedgerCommand(
    [property: Required] Guid Id,
    [property: Range(1, long.MaxValue)] long ExpectedVersion);
