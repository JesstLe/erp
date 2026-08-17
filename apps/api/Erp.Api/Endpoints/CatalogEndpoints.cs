using Erp.Application.Catalog;
using Erp.Application.Identity;
using Erp.Application.Security;

namespace Erp.Api.Endpoints;

public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/catalog").WithTags("Catalog").RequireAuthorization();

        group.MapGet("/service-items", async (IIdentityService identity, ICatalogService catalog, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            return current is null ? Results.Unauthorized() : Results.Ok(await catalog.ListServiceItemsAsync(current.TenantId, cancellationToken));
        });

        group.MapPost("/service-items", async (CreateServiceItemRequest request, IIdentityService identity, ICatalogService catalog, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null)
            {
                return Results.Unauthorized();
            }

            return EndpointResults.From(await catalog.CreateServiceItemAsync(current.TenantId,
                new CreateServiceItemCommand(request.Code ?? string.Empty, request.Name ?? string.Empty,
                    request.StandardDurationMinutes, current.Id, DefaultStoreId(current)), cancellationToken),
                value => Results.Created($"/api/v1/catalog/service-items/{value.Id}", value));
        }).RequireAuthorization(policy => policy.RequireRole(SystemRoles.Owner));

        group.MapGet("/products", async (IIdentityService identity, ICatalogService catalog, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            return current is null ? Results.Unauthorized() : Results.Ok(await catalog.ListProductItemsAsync(current.TenantId, cancellationToken));
        });

        group.MapPost("/products", async (CreateProductItemRequest request, IIdentityService identity, ICatalogService catalog,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            return EndpointResults.From(await catalog.CreateProductItemAsync(current.TenantId,
                new CreateProductItemCommand(request.Code ?? string.Empty, request.Name ?? string.Empty,
                    request.UnitName ?? string.Empty, request.TrackInventory, current.Id, DefaultStoreId(current)), cancellationToken),
                value => Results.Created($"/api/v1/catalog/products/{value.Id}", value));
        }).RequireAuthorization(policy => policy.RequireRole(SystemRoles.Owner));

        group.MapGet("/price-books", async (IIdentityService identity, ICatalogService catalog, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            return current is null ? Results.Unauthorized() : Results.Ok(await catalog.ListPriceBooksAsync(current.TenantId, cancellationToken));
        });

        group.MapPost("/price-books", async (CreatePriceBookRequest request, IIdentityService identity, ICatalogService catalog, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null)
            {
                return Results.Unauthorized();
            }

            var lines = request.Lines?.Select(x => new CreatePriceBookLineCommand(x.ServiceItemId, x.UnitPriceMinor)).ToList() ?? [];
            var productLines = request.ProductLines?.Select(x => new CreateProductPriceBookLineCommand(x.ProductItemId,
                x.UnitPriceMinor)).ToList() ?? [];
            return EndpointResults.From(await catalog.CreatePriceBookAsync(current.TenantId,
                new CreatePriceBookCommand(request.Name ?? string.Empty, request.EffectiveFrom, lines, productLines,
                    current.Id, DefaultStoreId(current)), cancellationToken),
                value => Results.Created($"/api/v1/catalog/price-books/{value.Id}", value));
        }).RequireAuthorization(policy => policy.RequireRole(SystemRoles.Owner));

        group.MapPost("/price-books/{id:guid}/publish", async (Guid id, IIdentityService identity, ICatalogService catalog, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            return current is null
                ? Results.Unauthorized()
                : EndpointResults.From(await catalog.PublishPriceBookAsync(current.TenantId, id, current.Id,
                    DefaultStoreId(current), cancellationToken));
        }).RequireAuthorization(policy => policy.RequireRole(SystemRoles.Owner));

        return endpoints;
    }

    private static Guid? DefaultStoreId(CurrentUserDto current)
    {
        var defaultStore = current.Stores.FirstOrDefault(x => x.IsDefault);
        return defaultStore?.Id ?? (current.Stores.Count > 0 ? current.Stores[0].Id : null);
    }

    private sealed record CreateServiceItemRequest(string? Code, string? Name, int StandardDurationMinutes);
    private sealed record CreateProductItemRequest(string? Code, string? Name, string? UnitName, bool TrackInventory);

    private sealed record CreatePriceBookRequest(string? Name, DateOnly EffectiveFrom, IReadOnlyList<CreatePriceBookLineRequest>? Lines,
        IReadOnlyList<CreateProductPriceBookLineRequest>? ProductLines);

    private sealed record CreatePriceBookLineRequest(Guid ServiceItemId, long UnitPriceMinor);
    private sealed record CreateProductPriceBookLineRequest(Guid ProductItemId, long UnitPriceMinor);
}
