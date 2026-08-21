using Erp.Application.Catalog;
using Erp.Application.Common;
using Erp.Application.Identity;
using Erp.Application.Security;
using Erp.Domain.Catalog;

namespace Erp.Api.Endpoints;

public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/catalog").WithTags("Catalog")
            .RequireAuthorization(SystemPermissions.CatalogRead);

        group.MapGet("/service-items", async (string? query, string? status, IIdentityService identity,
            ICatalogService catalog, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (query?.Trim().Length > 100) return InvalidQuery();
            if (!TryParseStatus(status, out var parsedStatus)) return InvalidStatus();
            return Results.Ok(await catalog.ListServiceItemsAsync(current.TenantId, query, parsedStatus,
                current.Permissions.Contains(SystemPermissions.CatalogWrite), cancellationToken));
        });

        group.MapPost("/service-items", async (CreateServiceItemRequest request, IIdentityService identity, ICatalogService catalog, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null)
            {
                return Results.Unauthorized();
            }
            if (!TryParseCommissionMode(request.CommissionMode, out var commissionMode))
                return InvalidCommissionMode();

            return EndpointResults.From(await catalog.CreateServiceItemAsync(current.TenantId,
                new CreateServiceItemCommand(request.Name ?? string.Empty,
                    request.StandardDurationMinutes, commissionMode,
                    request.CommissionRateBasisPoints, request.CommissionFixedMinor, current.Id,
                    DefaultStoreId(current)), cancellationToken),
                value => Results.Created($"/api/v1/catalog/service-items/{value.Id}", value));
        }).RequireAuthorization(SystemPermissions.CatalogWrite);

        group.MapPut("/service-items/{id:guid}", async (Guid id, UpdateServiceItemRequest request,
            IIdentityService identity, ICatalogService catalog, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!TryParseRequiredStatus(request.Status, out var parsedStatus)) return InvalidStatus();
            if (!TryParseCommissionMode(request.CommissionMode, out var commissionMode))
                return InvalidCommissionMode();
            return EndpointResults.From(await catalog.UpdateServiceItemAsync(current.TenantId,
                new UpdateServiceItemCommand(id, request.Name ?? string.Empty, request.StandardDurationMinutes,
                    parsedStatus, commissionMode, request.CommissionRateBasisPoints,
                    request.CommissionFixedMinor, request.ExpectedVersion, current.Id, DefaultStoreId(current)),
                cancellationToken));
        }).RequireAuthorization(SystemPermissions.CatalogWrite);

        group.MapDelete("/service-items/{id:guid}", async (Guid id, uint expectedVersion,
            IIdentityService identity, ICatalogService catalog, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            return EndpointResults.From(await catalog.DeleteServiceItemAsync(current.TenantId,
                new DeleteCatalogItemCommand(id, expectedVersion, current.Id, DefaultStoreId(current)),
                cancellationToken), _ => Results.NoContent());
        }).RequireAuthorization(SystemPermissions.CatalogWrite);

        group.MapGet("/products", async (string? query, string? status, IIdentityService identity,
            ICatalogService catalog, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (query?.Trim().Length > 100) return InvalidQuery();
            if (!TryParseStatus(status, out var parsedStatus)) return InvalidStatus();
            return Results.Ok(await catalog.ListProductItemsAsync(current.TenantId, query, parsedStatus,
                cancellationToken));
        });

        group.MapPost("/products", async (CreateProductItemRequest request, IIdentityService identity, ICatalogService catalog,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            return EndpointResults.From(await catalog.CreateProductItemAsync(current.TenantId,
                new CreateProductItemCommand(request.Name ?? string.Empty,
                    request.UnitName ?? string.Empty, request.TrackInventory, current.Id, DefaultStoreId(current)), cancellationToken),
                value => Results.Created($"/api/v1/catalog/products/{value.Id}", value));
        }).RequireAuthorization(SystemPermissions.CatalogWrite);

        group.MapPut("/products/{id:guid}", async (Guid id, UpdateProductItemRequest request,
            IIdentityService identity, ICatalogService catalog, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!TryParseRequiredStatus(request.Status, out var parsedStatus)) return InvalidStatus();
            return EndpointResults.From(await catalog.UpdateProductItemAsync(current.TenantId,
                new UpdateProductItemCommand(id, request.Name ?? string.Empty, request.UnitName ?? string.Empty,
                    request.TrackInventory, parsedStatus, request.ExpectedVersion, current.Id, DefaultStoreId(current)),
                cancellationToken));
        }).RequireAuthorization(SystemPermissions.CatalogWrite);

        group.MapDelete("/products/{id:guid}", async (Guid id, uint expectedVersion,
            IIdentityService identity, ICatalogService catalog, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            return EndpointResults.From(await catalog.DeleteProductItemAsync(current.TenantId,
                new DeleteCatalogItemCommand(id, expectedVersion, current.Id, DefaultStoreId(current)),
                cancellationToken), _ => Results.NoContent());
        }).RequireAuthorization(SystemPermissions.CatalogWrite);

        group.MapPost("/products/{id:guid}/image", async (Guid id, HttpRequest request,
            IIdentityService identity, ICatalogService catalog, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!request.HasFormContentType)
                return Results.Json(new { error = new { code = "VALIDATION_FAILED", message = "请使用表单上传图片" } },
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("image");
            if (file is null)
                return Results.Json(new { error = new { code = "VALIDATION_FAILED", message = "请选择产品图片" } },
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            await using var stream = file.OpenReadStream();
            return EndpointResults.From(await catalog.SetProductImageAsync(current.TenantId, id, current.Id,
                DefaultStoreId(current), new FileUploadInput(file.FileName, file.ContentType, file.Length, stream),
                cancellationToken));
        }).RequireAuthorization(SystemPermissions.CatalogWrite)
            .RequireRateLimiting("file-upload")
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(6 * 1024 * 1024));

        group.MapGet("/products/{id:guid}/image", async (Guid id, HttpResponse response,
            IIdentityService identity, ICatalogService catalog, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            var result = await catalog.ReadProductImageAsync(current.TenantId, id, cancellationToken);
            if (!result.IsSuccess || result.Value is null) return EndpointResults.From(result);
            response.Headers.CacheControl = "private, max-age=300";
            return Results.File(result.Value.Content, result.Value.ContentType, enableRangeProcessing: false);
        });

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
        }).RequireAuthorization(SystemPermissions.PricePublish);

        group.MapPost("/price-books/{id:guid}/publish", async (Guid id, IIdentityService identity, ICatalogService catalog, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            return current is null
                ? Results.Unauthorized()
                : EndpointResults.From(await catalog.PublishPriceBookAsync(current.TenantId, id, current.Id,
                    DefaultStoreId(current), cancellationToken));
        }).RequireAuthorization(SystemPermissions.PricePublish);

        group.MapPut("/price-books/{id:guid}", async (Guid id, UpdatePriceBookRequest request,
            IIdentityService identity, ICatalogService catalog, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            var lines = request.Lines?.Select(x => new CreatePriceBookLineCommand(x.ServiceItemId,
                x.UnitPriceMinor)).ToList() ?? [];
            var productLines = request.ProductLines?.Select(x => new CreateProductPriceBookLineCommand(
                x.ProductItemId, x.UnitPriceMinor)).ToList() ?? [];
            return EndpointResults.From(await catalog.UpdatePriceBookAsync(current.TenantId,
                new UpdatePriceBookCommand(id, request.Name ?? string.Empty, request.EffectiveFrom, lines,
                    productLines, request.ExpectedVersion, current.Id, DefaultStoreId(current)), cancellationToken));
        }).RequireAuthorization(SystemPermissions.PricePublish);

        group.MapPost("/price-books/{id:guid}/cancel", async (Guid id, CancelPriceBookRequest request,
            IIdentityService identity, ICatalogService catalog, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            return current is null ? Results.Unauthorized() : EndpointResults.From(await catalog.CancelPriceBookAsync(
                current.TenantId, new CancelPriceBookCommand(id, request.ExpectedVersion, current.Id,
                    DefaultStoreId(current)), cancellationToken));
        }).RequireAuthorization(SystemPermissions.PricePublish);

        return endpoints;
    }

    private static Guid? DefaultStoreId(CurrentUserDto current)
    {
        var defaultStore = current.Stores.FirstOrDefault(x => x.IsDefault);
        return defaultStore?.Id ?? (current.Stores.Count > 0 ? current.Stores[0].Id : null);
    }

    private static bool TryParseStatus(string? value, out CatalogItemStatus? status)
    {
        status = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!TryParseRequiredStatus(value, out var parsed)) return false;
        status = parsed;
        return true;
    }

    private static bool TryParseRequiredStatus(string? value, out CatalogItemStatus status)
    {
        status = default;
        switch (value?.Trim().ToUpperInvariant())
        {
            case "ENABLED": status = CatalogItemStatus.Enabled; return true;
            case "DISABLED": status = CatalogItemStatus.Disabled; return true;
            default: return false;
        }
    }

    private static IResult InvalidStatus() => Results.Json(
        new { error = new { code = "VALIDATION_FAILED", message = "状态只能是 ENABLED 或 DISABLED" } },
        statusCode: StatusCodes.Status422UnprocessableEntity);

    private static IResult InvalidQuery() => Results.Json(
        new { error = new { code = "VALIDATION_FAILED", message = "查询关键字不能超过100个字符" } },
        statusCode: StatusCodes.Status422UnprocessableEntity);

    private static bool TryParseCommissionMode(string? value, out CommissionMode mode)
    {
        mode = CommissionMode.None;
        switch (value?.Trim().ToUpperInvariant())
        {
            case null or "" or "NONE": return true;
            case "PERCENTAGE": mode = CommissionMode.Percentage; return true;
            case "FIXEDAMOUNT" or "FIXED_AMOUNT": mode = CommissionMode.FixedAmount; return true;
            default: return false;
        }
    }

    private static IResult InvalidCommissionMode() => Results.Json(
        new { error = new { code = "VALIDATION_FAILED", message = "提成方式只能是不计提、按比例或固定金额" } },
        statusCode: StatusCodes.Status422UnprocessableEntity);

    private sealed record CreateServiceItemRequest(string? Name, int StandardDurationMinutes,
        string? CommissionMode, int? CommissionRateBasisPoints, long? CommissionFixedMinor);
    private sealed record UpdateServiceItemRequest(string? Name, int StandardDurationMinutes, string? Status,
        string? CommissionMode, int? CommissionRateBasisPoints, long? CommissionFixedMinor, uint ExpectedVersion);
    private sealed record CreateProductItemRequest(string? Name, string? UnitName, bool TrackInventory);
    private sealed record UpdateProductItemRequest(string? Name, string? UnitName, bool TrackInventory, string? Status,
        uint ExpectedVersion);

    private sealed record CreatePriceBookRequest(string? Name, DateOnly EffectiveFrom, IReadOnlyList<CreatePriceBookLineRequest>? Lines,
        IReadOnlyList<CreateProductPriceBookLineRequest>? ProductLines);

    private sealed record CreatePriceBookLineRequest(Guid ServiceItemId, long UnitPriceMinor);
    private sealed record CreateProductPriceBookLineRequest(Guid ProductItemId, long UnitPriceMinor);
    private sealed record UpdatePriceBookRequest(string? Name, DateOnly EffectiveFrom,
        IReadOnlyList<CreatePriceBookLineRequest>? Lines,
        IReadOnlyList<CreateProductPriceBookLineRequest>? ProductLines, uint ExpectedVersion);
    private sealed record CancelPriceBookRequest(uint ExpectedVersion);
}
