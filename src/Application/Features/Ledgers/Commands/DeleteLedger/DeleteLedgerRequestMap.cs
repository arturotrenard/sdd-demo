using System.Buffers.Binary;
using System.ComponentModel.DataAnnotations;
using SddDemo.Ledger.Domain.Common;

namespace SddDemo.Ledger.Application.Features.Ledgers.Commands.DeleteLedger;

/// <summary>
/// data-model.md §4.1 — translation record between the protobuf
/// <c>DeleteLedgerRequest</c> and the Application <see cref="DeleteLedgerCommand"/>.
/// Decodes <c>version_token</c> as 8-byte big-endian.
/// </summary>
public sealed record DeleteLedgerRequestMap(
    [property: Required] Guid Id,
    [property: Range(1, long.MaxValue)] long ExpectedVersion)
{
    public static Result<DeleteLedgerRequestMap> From(
        string? rawId,
        ReadOnlySpan<byte> versionToken,
        IServiceProvider services)
    {
        if (!Guid.TryParse(rawId ?? string.Empty, out var id))
        {
            return Result<DeleteLedgerRequestMap>.Failure(new Error(
                "ledger.invalid_id",
                "id must be a canonical UUID.",
                ErrorType.Validation));
        }

        if (versionToken.Length != 8)
        {
            return Result<DeleteLedgerRequestMap>.Failure(new Error(
                "ledger.invalid_version_token",
                "version_token must be 8 bytes.",
                ErrorType.Validation));
        }

        var expectedVersion = BinaryPrimitives.ReadInt64BigEndian(versionToken);

        return DomainValidator.Validate(
            new DeleteLedgerRequestMap(id, expectedVersion),
            services);
    }

    public DeleteLedgerCommand ToCommand() => new(Id, ExpectedVersion);
}
