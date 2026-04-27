using System.Diagnostics;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;

namespace SddDemo.Ledger.Api.Grpc.Interceptors;

/// <summary>
/// Constitution Principle VI > Outermost safety net — registered FIRST in the
/// interceptor chain. Passes <see cref="RpcException"/> through untouched (those
/// came from <see cref="ResultToRpcExceptionMapper"/>); translates
/// <see cref="OperationCanceledException"/> to <see cref="StatusCode.Cancelled"/>;
/// converts everything else to a sanitised <see cref="StatusCode.Internal"/>
/// carrying only the trace_id. Internal details stay in logs.
/// </summary>
public sealed class ExceptionSafetyNetInterceptor(ILogger<ExceptionSafetyNetInterceptor> logger) : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context).ConfigureAwait(false);
        }
        catch (RpcException)
        {
            // Designed-failure path from ResultToRpcExceptionMapper — let it through.
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new RpcException(new Status(StatusCode.Cancelled, "Cancelled."));
        }
        catch (Exception ex)
        {
            var traceId = Activity.Current?.TraceId.ToString() ?? "unknown";

            logger.LogError(
                ex,
                "Unhandled exception in {Method}. TraceId={TraceId}",
                context.Method,
                traceId);

            throw new RpcException(new Status(
                StatusCode.Internal,
                $"An unexpected error occurred. Correlation: {traceId}"));
        }
    }
}
