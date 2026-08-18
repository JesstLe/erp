using Erp.Application.Cashier;
using Erp.Application.Common;
using Erp.Application.Identity;
using Erp.Application.Security;
using Erp.Domain.Cashier;

namespace Erp.Api.Endpoints;

public static class CashierEndpoints
{
    public static IEndpointRouteBuilder MapCashierEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/cashier").WithTags("Cashier")
            .RequireAuthorization(SystemPermissions.CashierCheckout);

        group.MapGet("/pending-visits", async (Guid storeId, int? page, int? pageSize,
            IIdentityService identity, ICashierService cashier,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            if (!Pagination.TryNormalize(page, pageSize, out var normalizedPage, out var normalizedPageSize))
                return EndpointResults.InvalidPagination();
            return Results.Ok(await cashier.ListPendingVisitsAsync(current.TenantId, storeId, normalizedPage,
                normalizedPageSize, cancellationToken));
        });

        group.MapGet("/service-employees", async (Guid storeId, IIdentityService identity, ICashierService cashier,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            return Results.Ok(await cashier.ListServiceEmployeesAsync(current.TenantId, storeId,
                cancellationToken));
        });

        group.MapGet("/orders", async (Guid storeId, string? query, Guid? customerId, Guid? catalogItemId,
            Guid? employeeId, string? status, DateOnly? fromDate, DateOnly? toDate, int? page, int? pageSize,
            IIdentityService identity, ICashierService cashier,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            if (query?.Trim().Length > 100 || !ValidOrderStatus(status) ||
                fromDate.HasValue && toDate.HasValue && (toDate < fromDate ||
                    toDate.Value.DayNumber - fromDate.Value.DayNumber > 3660))
                return Results.UnprocessableEntity(new
                {
                    error = new { code = "VALIDATION_FAILED", message = "消费单查询条件无效" },
                });
            if (!Pagination.TryNormalize(page, pageSize, out var normalizedPage, out var normalizedPageSize))
                return EndpointResults.InvalidPagination();
            return Results.Ok(await cashier.ListOrdersAsync(current.TenantId, storeId,
                new ServiceOrderSearchCriteria(query, customerId, catalogItemId, employeeId, status, fromDate,
                    toDate), normalizedPage, normalizedPageSize, cancellationToken));
        });

        group.MapGet("/orders/{orderId:guid}", async (Guid orderId, Guid storeId, IIdentityService identity,
            ICashierService cashier, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            return EndpointResults.From(await cashier.GetOrderAsync(current.TenantId, storeId, orderId, cancellationToken));
        });

        group.MapPost("/orders", async (CreateOrderRequest request, IIdentityService identity, ICashierService cashier,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await cashier.CreateOrderAsync(current.TenantId,
                new CreateServiceOrderCommand(request.StoreId, request.VisitId, request.CustomerId, request.Note,
                    (request.Lines ?? []).Select(x => new CreateServiceOrderLineCommand(x.LineType, x.ServiceItemId,
                        x.ProductItemId, x.ServiceEmployeeId, x.Quantity, x.ActualSeconds, x.EnteredPriceMinor,
                        x.PriceOverrideReason)).ToList(), request.CommandId,
                    current.Id, current.Roles), cancellationToken));
        });

        group.MapPost("/orders/{orderId:guid}/void", async (Guid orderId, VoidOrderRequest request,
            IIdentityService identity, ICashierService cashier, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await cashier.VoidOrderAsync(current.TenantId,
                new VoidServiceOrderCommand(request.StoreId, orderId, request.ExpectedVersion, request.Reason,
                    request.CommandId, current.Id), cancellationToken));
        });

        group.MapPost("/orders/{orderId:guid}/confirm", async (Guid orderId, ConfirmOrderRequest request,
            IIdentityService identity, ICashierService cashier, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await cashier.ConfirmOrderAsync(current.TenantId,
                new ConfirmServiceOrderCommand(request.StoreId, orderId, request.ExpectedVersion, request.CommandId,
                    current.Id), cancellationToken));
        });

        group.MapGet("/price-policy", async (IIdentityService identity, ICashierService cashier,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            return Results.Ok(await cashier.GetPriceOverridePolicyAsync(current.TenantId, current.Id,
                cancellationToken));
        });

        group.MapPut("/price-policy", async (UpdatePricePolicyRequest request, IIdentityService identity,
            ICashierService cashier, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await cashier.UpdatePriceOverridePolicyAsync(current.TenantId,
                new UpdatePriceOverridePolicyCommand(request.StoreId,
                    request.ManagerLineDiscountBasisPoints, request.ManagerOrderDiscountMinor,
                    request.AllowManagerPriceIncrease, request.ExpectedVersion, request.CommandId, current.Id),
                cancellationToken));
        }).RequireAuthorization(SystemPermissions.CashierApprovePrice);

        group.MapGet("/price-approvals", async (Guid storeId, string? status, int? page, int? pageSize,
            IIdentityService identity,
            ICashierService cashier, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            if (!Pagination.TryNormalize(page, pageSize, out var normalizedPage, out var normalizedPageSize))
                return EndpointResults.InvalidPagination();
            return Results.Ok(await cashier.ListPriceOverrideApprovalsAsync(current.TenantId, storeId, status,
                normalizedPage, normalizedPageSize, cancellationToken));
        }).RequireAuthorization(SystemPermissions.CashierApprovePrice);

        group.MapPost("/price-approvals/{approvalId:guid}/approve", async (Guid approvalId,
            DecidePriceApprovalRequest request, IIdentityService identity, ICashierService cashier,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await cashier.ApprovePriceOverrideAsync(current.TenantId,
                new DecidePriceOverrideApprovalCommand(request.StoreId, approvalId, request.ExpectedVersion,
                    request.Note, request.CommandId, current.Id), cancellationToken));
        }).RequireAuthorization(SystemPermissions.CashierApprovePrice);

        group.MapPost("/price-approvals/{approvalId:guid}/reject", async (Guid approvalId,
            DecidePriceApprovalRequest request, IIdentityService identity, ICashierService cashier,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await cashier.RejectPriceOverrideAsync(current.TenantId,
                new DecidePriceOverrideApprovalCommand(request.StoreId, approvalId, request.ExpectedVersion,
                    request.Note, request.CommandId, current.Id), cancellationToken));
        }).RequireAuthorization(SystemPermissions.CashierApprovePrice);

        return endpoints;
    }

    private static bool HasStore(CurrentUserDto user, Guid storeId) => user.Stores.Any(x => x.Id == storeId);
    private static bool ValidOrderStatus(string? value) => string.IsNullOrWhiteSpace(value) ||
        Enum.TryParse<ServiceOrderStatus>(value, true, out _);
    private sealed record CreateOrderLineRequest(string? LineType, Guid? ServiceItemId, Guid? ProductItemId,
        Guid? ServiceEmployeeId, int Quantity, int? ActualSeconds, long EnteredPriceMinor,
        string? PriceOverrideReason);
    private sealed record CreateOrderRequest(Guid StoreId, Guid? VisitId, Guid? CustomerId, string? Note,
        IReadOnlyList<CreateOrderLineRequest>? Lines, Guid CommandId);
    private sealed record ConfirmOrderRequest(Guid StoreId, uint ExpectedVersion, Guid CommandId);
    private sealed record VoidOrderRequest(Guid StoreId, uint ExpectedVersion, string Reason, Guid CommandId);
    private sealed record UpdatePricePolicyRequest(Guid StoreId, int ManagerLineDiscountBasisPoints,
        long ManagerOrderDiscountMinor, bool AllowManagerPriceIncrease, uint ExpectedVersion, Guid CommandId);
    private sealed record DecidePriceApprovalRequest(Guid StoreId, uint ExpectedVersion, string? Note,
        Guid CommandId);
}
