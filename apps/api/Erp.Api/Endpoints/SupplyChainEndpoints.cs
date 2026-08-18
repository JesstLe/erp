using Erp.Application.Common;
using Erp.Application.Identity;
using Erp.Application.Inventory;
using Erp.Application.Security;

namespace Erp.Api.Endpoints;

public static class SupplyChainEndpoints
{
    private static readonly string[] Readers = [SystemRoles.Owner, SystemRoles.StoreManager];

    public static IEndpointRouteBuilder MapSupplyChainEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/supply-chain").WithTags("SupplyChain")
            .RequireAuthorization(policy => policy.RequireRole(Readers));

        group.MapGet("/suppliers", async (string? keyword, bool? includeDisabled, int? page,
            int? pageSize, IIdentityService identity, ISupplyChainService service,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!Pagination.TryNormalize(page, pageSize, out var normalizedPage, out var normalizedPageSize))
                return EndpointResults.InvalidPagination();
            return Results.Ok(await service.ListSuppliersAsync(current.TenantId, keyword,
                includeDisabled == true, normalizedPage, normalizedPageSize, cancellationToken));
        });

        group.MapPost("/suppliers", async (SaveSupplierRequest request, IIdentityService identity,
            ISupplyChainService service, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!IsOwner(current)) return Results.Forbid();
            return EndpointResults.From(await service.SaveSupplierAsync(current.TenantId,
                new SaveSupplierCommand(null, request.Code, request.Name, request.ContactName,
                    request.Mobile, request.SettlementTerms, null, current.Id), cancellationToken),
                value => Results.Created($"/api/v1/supply-chain/suppliers/{value.Id}", value));
        });

        group.MapPut("/suppliers/{supplierId:guid}", async (Guid supplierId, SaveSupplierRequest request,
            IIdentityService identity, ISupplyChainService service, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!IsOwner(current)) return Results.Forbid();
            return EndpointResults.From(await service.SaveSupplierAsync(current.TenantId,
                new SaveSupplierCommand(supplierId, request.Code, request.Name, request.ContactName,
                    request.Mobile, request.SettlementTerms, request.ExpectedVersion, current.Id),
                cancellationToken));
        });

        group.MapPatch("/suppliers/{supplierId:guid}/status", async (Guid supplierId,
            SupplierStatusRequest request, IIdentityService identity, ISupplyChainService service,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!IsOwner(current)) return Results.Forbid();
            return EndpointResults.From(await service.ChangeSupplierStatusAsync(current.TenantId,
                new ChangeSupplierStatusCommand(supplierId, request.Enable, request.ExpectedVersion,
                    current.Id), cancellationToken));
        });

        group.MapGet("/lots", async (Guid storeId, Guid? productItemId, bool? expiringOnly,
            int? page, int? pageSize, IIdentityService identity, ISupplyChainService service,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            if (!Pagination.TryNormalize(page, pageSize, out var normalizedPage, out var normalizedPageSize))
                return EndpointResults.InvalidPagination();
            var result = await service.ListLotsAsync(current.TenantId, storeId, productItemId,
                expiringOnly == true, normalizedPage, normalizedPageSize, cancellationToken);
            if (IsOwner(current)) return Results.Ok(result);
            return Results.Ok(result with
            {
                Items = result.Items.Select(x => x with { UnitCostMinor = 0 }).ToList(),
            });
        });

        group.MapGet("/purchase-receipts", async (Guid storeId, int? page, int? pageSize,
            IIdentityService identity, ISupplyChainService service, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!IsOwner(current) || !HasStore(current, storeId)) return Results.Forbid();
            if (!Pagination.TryNormalize(page, pageSize, out var normalizedPage, out var normalizedPageSize))
                return EndpointResults.InvalidPagination();
            return Results.Ok(await service.ListPurchaseReceiptsAsync(current.TenantId, storeId,
                normalizedPage, normalizedPageSize, cancellationToken));
        });

        group.MapPost("/purchase-receipts", async (PostPurchaseReceiptRequest request,
            IIdentityService identity, ISupplyChainService service, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!IsOwner(current) || !HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await service.PostPurchaseReceiptAsync(current.TenantId,
                new PostPurchaseReceiptCommand(request.StoreId, request.SupplierId, request.ExternalNo,
                    request.Note, (request.Lines ?? []).Select(x => new PostPurchaseReceiptLineCommand(
                        x.ProductItemId, x.Quantity, x.UnitCostMinor, x.BatchNo, x.ExpiresOn)).ToList(),
                    request.CommandId, current.Id), cancellationToken), value => Results.Created(
                        $"/api/v1/supply-chain/purchase-receipts/{value.Id}", value));
        });

        group.MapGet("/stocktakes", async (Guid storeId, string? status, int? page, int? pageSize,
            IIdentityService identity, ISupplyChainService service, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            if (!Pagination.TryNormalize(page, pageSize, out var normalizedPage, out var normalizedPageSize))
                return EndpointResults.InvalidPagination();
            return Results.Ok(await service.ListStocktakesAsync(current.TenantId, storeId, status,
                normalizedPage, normalizedPageSize, cancellationToken));
        });

        group.MapPost("/stocktakes", async (CreateStocktakeRequest request, IIdentityService identity,
            ISupplyChainService service, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await service.CreateStocktakeAsync(current.TenantId,
                new CreateStocktakeCommand(request.StoreId, request.Reason,
                    (request.Lines ?? []).Select(x => new CreateStocktakeLineCommand(x.ProductItemId,
                        x.CountedQuantity)).ToList(), request.CommandId, current.Id), cancellationToken),
                value => Results.Created($"/api/v1/supply-chain/stocktakes/{value.Id}", value));
        });

        group.MapPost("/stocktakes/{stocktakeId:guid}/approve", async (Guid stocktakeId,
            StocktakeDecisionRequest request, IIdentityService identity, ISupplyChainService service,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!IsOwner(current) || !HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await service.ApproveStocktakeAsync(current.TenantId,
                new DecideStocktakeCommand(stocktakeId, request.StoreId, request.Reason, request.ExpectedVersion,
                    request.CommandId, current.Id), cancellationToken));
        });

        group.MapPost("/stocktakes/{stocktakeId:guid}/cancel", async (Guid stocktakeId,
            StocktakeDecisionRequest request, IIdentityService identity, ISupplyChainService service,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await service.CancelStocktakeAsync(current.TenantId,
                new DecideStocktakeCommand(stocktakeId, request.StoreId, request.Reason, request.ExpectedVersion,
                    request.CommandId, current.Id), cancellationToken));
        });

        group.MapGet("/transfers", async (Guid? storeId, string? status, int? page, int? pageSize,
            IIdentityService identity, ISupplyChainService service, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (storeId.HasValue && !HasStore(current, storeId.Value)) return Results.Forbid();
            if (!IsOwner(current) && !storeId.HasValue) return Results.Forbid();
            if (!Pagination.TryNormalize(page, pageSize, out var normalizedPage, out var normalizedPageSize))
                return EndpointResults.InvalidPagination();
            return Results.Ok(await service.ListTransfersAsync(current.TenantId, storeId, status,
                normalizedPage, normalizedPageSize, cancellationToken));
        });

        group.MapPost("/transfers", async (CreateTransferRequest request, IIdentityService identity,
            ISupplyChainService service, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!IsOwner(current) || !HasStore(current, request.SourceStoreId) ||
                !HasStore(current, request.DestinationStoreId)) return Results.Forbid();
            return EndpointResults.From(await service.CreateTransferAsync(current.TenantId,
                new CreateInventoryTransferCommand(request.SourceStoreId, request.DestinationStoreId,
                    request.Reason, (request.Lines ?? []).Select(x =>
                        new CreateInventoryTransferLineCommand(x.ProductItemId, x.Quantity)).ToList(),
                    request.CommandId, current.Id), cancellationToken), value => Results.Created(
                        $"/api/v1/supply-chain/transfers/{value.Id}", value));
        });

        MapTransferTransition(group, "ship", (service, tenantId, command, cancellationToken) =>
            service.ShipTransferAsync(tenantId, command, cancellationToken));
        MapTransferTransition(group, "receive", (service, tenantId, command, cancellationToken) =>
            service.ReceiveTransferAsync(tenantId, command, cancellationToken));
        MapTransferTransition(group, "cancel", (service, tenantId, command, cancellationToken) =>
            service.CancelTransferAsync(tenantId, command, cancellationToken));
        return endpoints;
    }

    private static void MapTransferTransition(RouteGroupBuilder group, string action,
        Func<ISupplyChainService, Guid, TransitionInventoryTransferCommand, CancellationToken,
            Task<Result<InventoryTransferDto>>> transition) =>
        group.MapPost($"/transfers/{{transferId:guid}}/{action}", async (Guid transferId,
            DecisionRequest request, IIdentityService identity, ISupplyChainService service,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!IsOwner(current)) return Results.Forbid();
            return EndpointResults.From(await transition(service, current.TenantId,
                new TransitionInventoryTransferCommand(transferId, request.Reason,
                    request.ExpectedVersion, request.CommandId, current.Id), cancellationToken));
        });

    private static bool IsOwner(CurrentUserDto user) => user.Roles.Contains(SystemRoles.Owner);
    private static bool HasStore(CurrentUserDto user, Guid storeId) => user.Stores.Any(x => x.Id == storeId);
    private sealed record SaveSupplierRequest(string Code, string Name, string? ContactName,
        string? Mobile, string? SettlementTerms, uint? ExpectedVersion);
    private sealed record SupplierStatusRequest(bool Enable, uint ExpectedVersion);
    private sealed record PurchaseLineRequest(Guid ProductItemId, int Quantity, long UnitCostMinor,
        string BatchNo, DateOnly? ExpiresOn);
    private sealed record PostPurchaseReceiptRequest(Guid StoreId, Guid SupplierId, string? ExternalNo,
        string Note, IReadOnlyList<PurchaseLineRequest>? Lines, Guid CommandId);
    private sealed record StocktakeLineRequest(Guid ProductItemId, int CountedQuantity);
    private sealed record CreateStocktakeRequest(Guid StoreId, string Reason,
        IReadOnlyList<StocktakeLineRequest>? Lines, Guid CommandId);
    private sealed record TransferLineRequest(Guid ProductItemId, int Quantity);
    private sealed record CreateTransferRequest(Guid SourceStoreId, Guid DestinationStoreId, string Reason,
        IReadOnlyList<TransferLineRequest>? Lines, Guid CommandId);
    private sealed record DecisionRequest(string Reason, uint ExpectedVersion, Guid CommandId);
    private sealed record StocktakeDecisionRequest(Guid StoreId, string Reason, uint ExpectedVersion,
        Guid CommandId);
}
