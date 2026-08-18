using Erp.Application.Identity;
using Erp.Application.Common;
using Erp.Application.Inventory;
using Erp.Application.Security;

namespace Erp.Api.Endpoints;

public static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/inventory").WithTags("Inventory")
            .RequireAuthorization(SystemPermissions.InventoryRead);

        group.MapGet("/balances", async (Guid storeId, IIdentityService identity, IInventoryService inventory,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            return Results.Ok(await inventory.ListBalancesAsync(current.TenantId, storeId, cancellationToken));
        });

        group.MapGet("/movements", async (Guid storeId, Guid? productItemId, int? page, int? pageSize,
            IIdentityService identity,
            IInventoryService inventory, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            if (!Pagination.TryNormalize(page, pageSize, out var normalizedPage, out var normalizedPageSize))
                return EndpointResults.InvalidPagination();
            return Results.Ok(await inventory.ListMovementsAsync(current.TenantId, storeId, productItemId,
                normalizedPage, normalizedPageSize, cancellationToken));
        });

        group.MapGet("/documents", async (Guid storeId, int? page, int? pageSize,
            IIdentityService identity, IInventoryService inventory,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            if (!Pagination.TryNormalize(page, pageSize, out var normalizedPage, out var normalizedPageSize))
                return EndpointResults.InvalidPagination();
            return Results.Ok(await inventory.ListDocumentsAsync(current.TenantId, storeId, normalizedPage,
                normalizedPageSize, cancellationToken));
        });

        group.MapPost("/documents", async (PostDocumentRequest request, IIdentityService identity,
            IInventoryService inventory, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!current.Roles.Contains(SystemRoles.Owner)) return Results.Forbid();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await inventory.PostDocumentAsync(current.TenantId,
                new PostInventoryDocumentCommand(request.StoreId, request.DocumentType, request.Reason,
                    (request.Lines ?? []).Select(x => new PostInventoryDocumentLineCommand(x.ProductItemId,
                        x.Quantity)).ToList(), request.CommandId, current.Id), cancellationToken));
        }).RequireAuthorization(SystemPermissions.InventoryWrite);

        group.MapPost("/product-returns", async (ProductReturnRequest request, IIdentityService identity,
            IInventoryService inventory, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!current.Roles.Contains(SystemRoles.Owner)) return Results.Forbid();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await inventory.ReturnProductAsync(current.TenantId,
                new ReturnProductCommand(request.StoreId, request.OrderId, request.OrderLineId,
                    request.Quantity, request.Reason, request.ExpectedOrderVersion, request.CommandId,
                    current.Id), cancellationToken));
        }).RequireAuthorization(SystemPermissions.InventoryWrite);

        return endpoints;
    }

    private static bool HasStore(CurrentUserDto user, Guid storeId) => user.Stores.Any(x => x.Id == storeId);
    private sealed record PostDocumentLineRequest(Guid ProductItemId, int Quantity);
    private sealed record PostDocumentRequest(Guid StoreId, string DocumentType, string Reason,
        IReadOnlyList<PostDocumentLineRequest>? Lines, Guid CommandId);
    private sealed record ProductReturnRequest(Guid StoreId, Guid OrderId, Guid OrderLineId, int Quantity,
        string Reason, uint ExpectedOrderVersion, Guid CommandId);
}
