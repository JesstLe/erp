using Erp.Application.Cashier;
using Erp.Application.Common;
using Erp.Application.Identity;
using Erp.Application.Security;

namespace Erp.Api.Endpoints;

public static class RefundEndpoints
{
    public static IEndpointRouteBuilder MapRefundEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/refunds").WithTags("Refunds")
            .RequireAuthorization(SystemPermissions.RefundRequest);

        group.MapGet("", async (Guid storeId, Guid? paymentId, int? page, int? pageSize,
            IIdentityService identity,
            IRefundService refunds, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            if (!Pagination.TryNormalize(page, pageSize, out var normalizedPage, out var normalizedPageSize))
                return EndpointResults.InvalidPagination();
            return Results.Ok(await refunds.ListAsync(current.TenantId, storeId, paymentId,
                normalizedPage, normalizedPageSize, cancellationToken));
        });

        group.MapPost("", async (Request request, IIdentityService identity, IRefundService refunds,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await refunds.RequestAsync(current.TenantId,
                new RequestRefundCommand(request.StoreId, request.PaymentId,
                    request.ExpectedPaymentVersion, request.Reason ?? string.Empty,
                    (request.Lines ?? []).Select(x => new RequestRefundLineCommand(
                        x.OriginalAllocationId, x.AmountMinor)).ToList(), request.CommandId, current.Id),
                cancellationToken));
        });

        group.MapPost("/{refundId:guid}/approve", async (Guid refundId, ApproveRequest request,
            IIdentityService identity, IRefundService refunds, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await refunds.ApproveAsync(current.TenantId,
                new ApproveRefundCommand(request.StoreId, refundId, request.ExpectedVersion,
                    request.CommandId, current.Id), cancellationToken));
        }).RequireAuthorization(SystemPermissions.RefundApprove);

        group.MapPost("/{refundId:guid}/reject", async (Guid refundId, RejectRequest request,
            IIdentityService identity, IRefundService refunds, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await refunds.RejectAsync(current.TenantId,
                new RejectRefundCommand(request.StoreId, refundId, request.ExpectedVersion,
                    request.Reason ?? string.Empty, request.CommandId, current.Id), cancellationToken));
        }).RequireAuthorization(SystemPermissions.RefundApprove);

        group.MapPost("/{refundId:guid}/channel/query", async (Guid refundId,
            OperateChannelRequest request, IIdentityService identity, IRefundService refunds,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await refunds.QueryChannelAsync(current.TenantId,
                new OperateChannelRefundCommand(request.StoreId, refundId, current.Id), cancellationToken));
        });

        group.MapPost("/{refundId:guid}/channel/retry", async (Guid refundId,
            OperateChannelRequest request, IIdentityService identity, IRefundService refunds,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await refunds.RetryChannelAsync(current.TenantId,
                new OperateChannelRefundCommand(request.StoreId, refundId, current.Id), cancellationToken));
        }).RequireAuthorization(SystemPermissions.RefundApprove);

        return endpoints;
    }

    private static bool HasStore(CurrentUserDto user, Guid storeId) => user.Stores.Any(x => x.Id == storeId);
    private sealed record LineRequest(Guid OriginalAllocationId, long AmountMinor);
    private sealed record Request(Guid StoreId, Guid PaymentId, uint ExpectedPaymentVersion,
        string? Reason, IReadOnlyList<LineRequest>? Lines, Guid CommandId);
    private sealed record ApproveRequest(Guid StoreId, uint ExpectedVersion, Guid CommandId);
    private sealed record RejectRequest(Guid StoreId, uint ExpectedVersion, string? Reason, Guid CommandId);
    private sealed record OperateChannelRequest(Guid StoreId);
}
