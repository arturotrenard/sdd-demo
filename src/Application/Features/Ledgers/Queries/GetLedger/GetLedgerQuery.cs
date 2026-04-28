using System.ComponentModel.DataAnnotations;

namespace SddDemo.Ledger.Application.Features.Ledgers.Queries.GetLedger;

/// <summary>
/// data-model.md §4 — Application query for FR-005 single-fetch by id.
/// </summary>
public sealed record GetLedgerQuery([property: Required] Guid Id);
