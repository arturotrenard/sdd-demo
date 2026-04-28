using Grpc.Core;
using SddDemo.Ledger.Application.Features.Ledgers.Commands.CreateLedger;
using SddDemo.Ledger.Application.Features.Ledgers.Commands.DeleteLedger;
using SddDemo.Ledger.Application.Features.Ledgers.Commands.UpdateLedger;
using SddDemo.Ledger.Application.Features.Ledgers.Queries.GetLedger;
using SddDemo.Ledger.Application.Features.Ledgers.Queries.ListLedgers;
using SddDemo.Ledger.V1;
using DomainStatus = SddDemo.Ledger.Domain.Ledgers.LedgerStatus;
using LedgersBase = SddDemo.Ledger.V1.Ledgers.LedgersBase;
using ProtoStatus = SddDemo.Ledger.V1.LedgerStatus;

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
    GetLedgerHandler getHandler,
    ListLedgersHandler listHandler,
    UpdateLedgerHandler updateHandler,
    DeleteLedgerHandler deleteHandler,
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

    public override async Task<LedgerView> GetLedger(
        GetLedgerRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var mapResult = GetLedgerRequestMap.From(request.Id, services);
        if (mapResult.IsFailure)
        {
            throw ResultToRpcExceptionMapper.ToRpcException(mapResult.Error!);
        }

        var result = await getHandler.Handle(mapResult.Value!.ToQuery(), context.CancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            throw ResultToRpcExceptionMapper.ToRpcException(result.Error!);
        }

        return result.Value!.ToLedgerView();
    }

    public override async Task<ListLedgersResponse> ListLedgers(
        ListLedgersRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var mapResult = ListLedgersRequestMap.From(
            request.IncludeArchived,
            request.PageCursor,
            request.PageSize,
            services);

        if (mapResult.IsFailure)
        {
            throw ResultToRpcExceptionMapper.ToRpcException(mapResult.Error!);
        }

        var result = await listHandler.Handle(mapResult.Value!.ToQuery(), context.CancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            throw ResultToRpcExceptionMapper.ToRpcException(result.Error!);
        }

        var page = result.Value!;
        var response = new ListLedgersResponse
        {
            NextPageCursor = page.NextPageCursor ?? string.Empty,
        };
        foreach (var ledger in page.Items)
        {
            response.Ledgers.Add(ledger.ToLedgerView());
        }
        return response;
    }

    public override async Task<LedgerView> UpdateLedger(
        UpdateLedgerRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var maskPaths = request.UpdateMask?.Paths.ToArray() ?? Array.Empty<string>();
        var mapResult = UpdateLedgerRequestMap.From(
            request.Id,
            request.VersionToken.Span,
            maskPaths,
            request.Name,
            request.Description,
            FromProtoStatus(request.Status),
            services);

        if (mapResult.IsFailure)
        {
            throw ResultToRpcExceptionMapper.ToRpcException(mapResult.Error!);
        }

        var result = await updateHandler.Handle(mapResult.Value!.ToCommand(), context.CancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            throw ResultToRpcExceptionMapper.ToRpcException(result.Error!);
        }

        return result.Value!.ToLedgerView();
    }

    public override async Task<DeleteLedgerResponse> DeleteLedger(
        DeleteLedgerRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var mapResult = DeleteLedgerRequestMap.From(
            request.Id,
            request.VersionToken.Span,
            services);

        if (mapResult.IsFailure)
        {
            throw ResultToRpcExceptionMapper.ToRpcException(mapResult.Error!);
        }

        var result = await deleteHandler.Handle(mapResult.Value!.ToCommand(), context.CancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            throw ResultToRpcExceptionMapper.ToRpcException(result.Error!);
        }

        return new DeleteLedgerResponse();
    }

    private static DomainStatus? FromProtoStatus(ProtoStatus status) => status switch
    {
        ProtoStatus.Active => DomainStatus.Active,
        ProtoStatus.Archived => DomainStatus.Archived,
        _ => null,
    };
}
