using Grpc.Core;
using SddDemo.Ledger.Application.Features.Ledgers.Commands.CreateLedger;
using SddDemo.Ledger.V1;
using LedgersBase = SddDemo.Ledger.V1.Ledgers.LedgersBase;

namespace SddDemo.Ledger.Api.Grpc;

/// <summary>
/// gRPC surface — implements the <c>Ledgers</c> service generated from
/// <c>ledger.v1.proto</c>. Each RPC dispatches through its <c>*RequestMap</c> for
/// transport-shape validation (Constitution Principle VI > Validation tier 1) then
/// hands off to the matching Application handler. Failures are translated to
/// <see cref="RpcException"/> exclusively via <see cref="ResultToRpcExceptionMapper"/>.
/// </summary>
public sealed class LedgersService(
    CreateLedgerHandler createHandler,
    IServiceProvider services) : LedgersBase
{
    public override async Task<LedgerView> CreateLedger(
        CreateLedgerRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var mapResult = CreateLedgerRequestMap.From(
            request.Name,
            request.Description,
            request.CurrencyCode,
            services);

        if (mapResult.IsFailure)
        {
            throw ResultToRpcExceptionMapper.ToRpcException(mapResult.Error!);
        }

        var command = mapResult.Value!.ToCommand();
        var result = await createHandler.Handle(command, context.CancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            throw ResultToRpcExceptionMapper.ToRpcException(result.Error!);
        }

        return result.Value!.ToLedgerView();
    }
}
