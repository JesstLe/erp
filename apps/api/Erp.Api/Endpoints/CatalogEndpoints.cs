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
                new CreateServiceItemCommand(request.Code ?? string.Empty, request.Name ?? string.Empty, request.StandardDurationMinutes), cancellationToken),
                value => Results.Created($"/api/v1/catalog/service-items/{value.Id}", value));
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
            return EndpointResults.From(await catalog.CreatePriceBookAsync(current.TenantId,
                new CreatePriceBookCommand(request.Name ?? string.Empty, request.EffectiveFrom, lines), cancellationToken),
                value => Results.Created($"/api/v1/catalog/price-books/{value.Id}", value));
        }).RequireAuthorization(policy => policy.RequireRole(SystemRoles.Owner));

        group.MapPost("/price-books/{id:guid}/publish", async (Guid id, IIdentityService identity, ICatalogService catalog, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            return current is null
                ? Results.Unauthorized()
                : EndpointResults.From(await catalog.PublishPriceBookAsync(current.TenantId, id, cancellationToken));
        }).RequireAuthorization(policy => policy.RequireRole(SystemRoles.Owner));

        return endpoints;
    }

    private sealed record CreateServiceItemRequest(string? Code, string? Name, int StandardDurationMinutes);

    private sealed record CreatePriceBookRequest(string? Name, DateOnly EffectiveFrom, IReadOnlyList<CreatePriceBookLineRequest>? Lines);

    private sealed record CreatePriceBookLineRequest(Guid ServiceItemId, long UnitPriceMinor);
}

