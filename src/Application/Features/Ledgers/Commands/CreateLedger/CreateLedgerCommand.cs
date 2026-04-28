using System.ComponentModel.DataAnnotations;
using SddDemo.Ledger.Domain.Currency;

namespace SddDemo.Ledger.Application.Features.Ledgers.Commands.CreateLedger;

/// <summary>
/// data-model.md §4 — Application command for FR-001 ledger creation.
/// Decorated with Data Annotations so DomainValidator.Validate aggregates
/// every input violation into a single <c>Result.Failure(Validation)</c>.
/// </summary>
public sealed record CreateLedgerCommand(
    [property: Required, StringLength(100, MinimumLength = 1)] string Name,
    [property: StringLength(500)] string? Description,
    [property: Required, ValidIsoCurrency] string CurrencyCode);
