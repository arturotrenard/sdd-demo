using System.Buffers.Binary;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using SddDemo.Ledger.V1;
using DomainLedger = SddDemo.Ledger.Domain.Ledgers.Ledger;
using DomainStatus = SddDemo.Ledger.Domain.Ledgers.LedgerStatus;
using ProtoStatus = SddDemo.Ledger.V1.LedgerStatus;

namespace SddDemo.Ledger.Api.Grpc;

/// <summary>
/// data-model.md §6 — Domain → wire mapping. The version_token is the 8-byte
/// big-endian encoding of the bigint version column; clients treat it as opaque
/// bytes per the proto contract.
/// </summary>
public static class LedgerViewMapper
{
    public static LedgerView ToLedgerView(this DomainLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        return new LedgerView
        {
            Id = ledger.Id.ToString(),
            Name = ledger.Name,
            Description = ledger.Description ?? string.Empty,
            CurrencyCode = ledger.CurrencyCode,
            Status = ToProtoStatus(ledger.Status),
            VersionToken = EncodeVersion(ledger.Version),
            CreatedAt = Timestamp.FromDateTimeOffset(ledger.CreatedAt),
            LastModifiedAt = Timestamp.FromDateTimeOffset(ledger.LastModifiedAt),
        };
    }

    public static ByteString EncodeVersion(long version)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(buffer, version);
        return ByteString.CopyFrom(buffer);
    }

    public static long DecodeVersion(ByteString token)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (token.Length != 8)
        {
            throw new ArgumentException(
                $"version_token must be 8 bytes, got {token.Length}.",
                nameof(token));
        }
        return BinaryPrimitives.ReadInt64BigEndian(token.Span);
    }

    private static ProtoStatus ToProtoStatus(DomainStatus status) => status switch
    {
        DomainStatus.Active => ProtoStatus.Active,
        DomainStatus.Archived => ProtoStatus.Archived,
        _ => ProtoStatus.Unspecified,
    };
}
