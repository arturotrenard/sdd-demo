using System.ComponentModel.DataAnnotations;
using SddDemo.Ledger.Domain.Common;
using SddDemo.Ledger.Domain.Currency;

namespace SddDemo.Ledger.Application.Features.Ledgers.Commands.CreateLedger;

/// <summary>
/// data-model.md §4.1 — translation record between the protobuf <c>CreateLedgerRequest</c>
/// and the Application <see cref="CreateLedgerCommand"/>. Carries Data Annotations so
/// the validation tier 1 (transport-shape) runs through <see cref="DomainValidator"/>;
/// trims whitespace from the name and normalises the currency to upper-invariant.
/// </summary>
public sealed record CreateLedgerRequestMap(
    [property: Required, StringLength(100, MinimumLength = 1)] string Name,
    [property: StringLength(500)] string? Description,
    [property: Required, ValidIsoCurrency] string CurrencyCode)
{
    /// <summary>
    /// Builds the validated mapping record from the loose protobuf message shape.
    /// </summary>
    public static Result<CreateLedgerRequestMap> From(
        string? rawName,
        string? rawDescription,
        string? rawCurrencyCode,
        IServiceProvider services)
    {
        var dto = new CreateLedgerRequestMap(
            Name: (rawName ?? string.Empty).Trim(),
            Description: string.IsNullOrWhiteSpace(rawDescription) ? null : rawDescription.Trim(),
            CurrencyCode: (rawCurrencyCode ?? string.Empty).Trim().ToUpperInvariant());

        return DomainValidator.Validate(dto, services);
    }

    public CreateLedgerCommand ToCommand() => new(Name, Description, CurrencyCode);
}
