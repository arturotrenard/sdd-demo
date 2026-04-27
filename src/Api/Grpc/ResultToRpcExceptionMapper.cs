using Grpc.Core;
using SddDemo.Ledger.Domain.Common;

namespace SddDemo.Ledger.Api.Grpc;

/// <summary>
/// Constitution Principle VI > API boundary translation — the SOLE place a
/// <see cref="Result.Failure"/> becomes an <see cref="RpcException"/>. Service classes
/// MUST call into this helper instead of constructing RpcException themselves.
/// Mapping table is fixed:
/// <list type="bullet">
///   <item><term>Validation</term><description>InvalidArgument</description></item>
///   <item><term>NotFound</term><description>NotFound</description></item>
///   <item><term>Conflict</term><description>AlreadyExists</description></item>
///   <item><term>Unauthorized</term><description>Unauthenticated</description></item>
///   <item><term>Forbidden</term><description>PermissionDenied</description></item>
///   <item><term>Failure</term><description>Internal</description></item>
/// </list>
/// </summary>
public static class ResultToRpcExceptionMapper
{
    public static TReply ToReply<TReply>(this Result<TReply> result)
        where TReply : class
    {
        if (result.IsSuccess)
        {
            return result.Value!;
        }

        throw ToRpcException(result.Error!);
    }

    public static void ThrowIfFailure(this Result result)
    {
        if (result.IsFailure)
        {
            throw ToRpcException(result.Error!);
        }
    }

    public static RpcException ToRpcException(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var status = error.Type switch
        {
            ErrorType.Validation => StatusCode.InvalidArgument,
            ErrorType.NotFound => StatusCode.NotFound,
            ErrorType.Conflict => StatusCode.AlreadyExists,
            ErrorType.Unauthorized => StatusCode.Unauthenticated,
            ErrorType.Forbidden => StatusCode.PermissionDenied,
            ErrorType.Failure => StatusCode.Internal,
            _ => StatusCode.Internal,
        };

        var trailers = new Metadata
        {
            { "ledger-error-code", error.Code },
        };

        return new RpcException(new Status(status, error.Message), trailers);
    }
}
