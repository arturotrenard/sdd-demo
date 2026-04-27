using Grpc.Core;
using Grpc.Core.Interceptors;

namespace SddDemo.Ledger.Api.Grpc.Interceptors;

/// <summary>
/// research.md §8 — transport-shape validation. Per-request *RequestMap types
/// (introduced in user-story phases) carry Data Annotations; this interceptor
/// is the second in the chain (after the safety net) and short-circuits with
/// <see cref="StatusCode.InvalidArgument"/> when a downstream mapping fails.
/// In Phase 2 it is a pass-through — the user-story phases plug the mapper
/// in via DI.
/// </summary>
public sealed class ValidationInterceptor : Interceptor
{
    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        // Phase 2 placeholder — user-story phases (Phase 3+) replace the body to
        // resolve and invoke the matching *RequestMap.From(request) and translate
        // a Result.Failure(Validation) into RpcException(InvalidArgument) via
        // ResultToRpcExceptionMapper before continuation.
        return continuation(request, context);
    }
}
