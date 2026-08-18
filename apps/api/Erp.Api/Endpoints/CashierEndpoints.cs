using Erp.Application.Cashier;
using Erp.Application.Identity;
using Erp.Application.Security;

namespace Erp.Api.Endpoints;

public static class CashierEndpoints
{
    private static readonly string[] OrderOperators = [SystemRoles.Owner, SystemRoles.StoreManager, SystemRoles.Cashier];

    public static IEndpointRouteBuilder MapCashierEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/cashier").WithTags("Cashier").RequireAuthorization(policy => policy.RequireRole(OrderOperators));

        group.MapGet("/pending-visits", async (Guid storeId, IIdentityService identity, ICashierService cashier,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            return Results.Ok(await cashier.ListPendingVisitsAsync(current.TenantId, storeId, cancellationToken));
        });

        group.MapGet("/orders", async (Guid storeId, IIdentityService identity, ICashierService cashier,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            return Results.Ok(await cashier.ListOrdersAsync(current.TenantId, storeId, cancellationToken));
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
                        x.ProductItemId, x.Quantity, x.ActualSeconds, x.EnteredPriceMinor,
                        x.PriceOverrideReason)).ToList(), request.CommandId,
                    current.Id), cancellationToken));
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

        return endpoints;
    }

    private static bool HasStore(CurrentUserDto user, Guid storeId) => user.Stores.Any(x => x.Id == storeId);
    private sealed record CreateOrderLineRequest(string? LineType, Guid? ServiceItemId, Guid? ProductItemId,
        int Quantity, int? ActualSeconds, long EnteredPriceMinor, string? PriceOverrideReason);
    private sealed record CreateOrderRequest(Guid StoreId, Guid? VisitId, Guid? CustomerId, string? Note,
        IReadOnlyList<CreateOrderLineRequest>? Lines, Guid CommandId);
    private sealed record ConfirmOrderRequest(Guid StoreId, uint ExpectedVersion, Guid CommandId);
    private sealed record VoidOrderRequest(Guid StoreId, uint ExpectedVersion, string Reason, Guid CommandId);
}
