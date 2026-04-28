using System.ComponentModel.DataAnnotations;
using SddDemo.Ledger.Domain.Common;

namespace SddDemo.Ledger.Application.Features.Ledgers.Queries.GetLedger;

/// <summary>
/// data-model.md §4.1 — translation record between the protobuf
/// <c>GetLedgerRequest</c> and the Application <see cref="GetLedgerQuery"/>.
/// Parses the canonical UUID and emits a <c>Validation</c> failure on a malformed
/// input rather than letting <see cref="Guid.Parse(string)"/> throw.
/// </summary>
public sealed record GetLedgerRequestMap(
    [property: Required] Guid Id)
{
    public static Result<GetLedgerRequestMap> From(string? rawId, IServiceProvider services)
    {
        if (!Guid.TryParse(rawId ?? string.Empty, out var id))
        {
            return Result<GetLedgerRequestMap>.Failure(new Error(
                "ledger.invalid_id",
                "id must be a canonical UUID.",
                ErrorType.Validation));
        }

        return DomainValidator.Validate(new GetLedgerRequestMap(id), services);
    }

    public GetLedgerQuery ToQuery() => new(Id);
}
