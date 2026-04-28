using System.Buffers.Binary;
using System.ComponentModel.DataAnnotations;
using SddDemo.Ledger.Domain.Common;
using SddDemo.Ledger.Domain.Ledgers;

namespace SddDemo.Ledger.Application.Features.Ledgers.Commands.UpdateLedger;

/// <summary>
/// data-model.md §4.1 — translation record between the protobuf
/// <c>UpdateLedgerRequest</c> and the Application <see cref="UpdateLedgerCommand"/>.
/// Honours <c>update_mask</c>: fields listed in the mask are forwarded; those not
/// listed are dropped to <c>null</c> so the handler can apply the "leave as-is"
/// rule. Decodes <c>version_token</c> as 8-byte big-endian per <c>LedgerViewMapper</c>.
/// </summary>
public sealed record UpdateLedgerRequestMap(
    [property: Required] Guid Id,
    [property: Range(1, long.MaxValue)] long ExpectedVersion,
    [property: StringLength(100, MinimumLength = 1)] string? Name,
    [property: StringLength(500)] string? Description,
    LedgerStatus? Status)
{
    public static Result<UpdateLedgerRequestMap> From(
        string? rawId,
        ReadOnlySpan<byte> versionToken,
        IReadOnlyCollection<string> updateMaskPaths,
        string? rawName,
        string? rawDescription,
        LedgerStatus? rawStatus,
        IServiceProvider services)
    {
        if (!Guid.TryParse(rawId ?? string.Empty, out var id))
        {
            return Result<UpdateLedgerRequestMap>.Failure(new Error(
                "ledger.invalid_id",
                "id must be a canonical UUID.",
                ErrorType.Validation));
        }

        if (versionToken.Length != 8)
        {
            return Result<UpdateLedgerRequestMap>.Failure(new Error(
                "ledger.invalid_version_token",
                "version_token must be 8 bytes.",
                ErrorType.Validation));
        }

        var expectedVersion = BinaryPrimitives.ReadInt64BigEndian(versionToken);

        var nameSelected = MaskContains(updateMaskPaths, "name");
        var descriptionSelected = MaskContains(updateMaskPaths, "description");
        var statusSelected = MaskContains(updateMaskPaths, "status");

        var dto = new UpdateLedgerRequestMap(
            Id: id,
            ExpectedVersion: expectedVersion,
            Name: nameSelected ? (rawName ?? string.Empty).Trim() : null,
            Description: descriptionSelected
                ? (string.IsNullOrWhiteSpace(rawDescription) ? null : rawDescription.Trim())
                : null,
            Status: statusSelected ? rawStatus : null);

        return DomainValidator.Validate(dto, services);
    }

    public UpdateLedgerCommand ToCommand() =>
        new(Id, ExpectedVersion, Name, Description, Status);

    private static bool MaskContains(IReadOnlyCollection<string> paths, string field) =>
        paths.Any(p => string.Equals(p, field, StringComparison.OrdinalIgnoreCase));
}
