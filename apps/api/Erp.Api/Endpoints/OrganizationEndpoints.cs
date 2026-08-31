using Erp.Application.Identity;
using Erp.Application.Organization;
using Erp.Application.Security;

namespace Erp.Api.Endpoints;

public static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var navigation = endpoints.MapGroup("/api/v1/navigation").WithTags("Organization")
            .RequireAuthorization();

        navigation.MapGet("/labels", async (IIdentityService identity, IOrganizationService organization,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            var labels = await organization.GetNavigationLabelsAsync(current.TenantId, cancellationToken);
            return labels is null ? Results.NotFound() : Results.Ok(labels);
        });

        navigation.MapPut("/labels", async (NavigationLabelsRequest request, IIdentityService identity,
            IOrganizationService organization, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            return current is null ? Results.Unauthorized() : EndpointResults.From(
                await organization.UpdateNavigationLabelsAsync(current.TenantId,
                    new UpdateNavigationLabelsCommand(request.Labels ?? new Dictionary<string, string>(),
                        request.ExpectedVersion, current.Id), cancellationToken));
        }).RequireAuthorization(SystemPermissions.OrganizationManage);

        var group = endpoints.MapGroup("/api/v1/organization").WithTags("Organization")
            .RequireAuthorization(SystemPermissions.OrganizationManage);

        group.MapGet("/settings", async (IIdentityService identity, IOrganizationService organization,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            var settings = await organization.GetSettingsAsync(current.TenantId, cancellationToken);
            return settings is null ? Results.NotFound() : Results.Ok(settings);
        });

        group.MapPut("/brand", async (UpdateBrandRequest request, IIdentityService identity,
            IOrganizationService organization, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            return current is null ? Results.Unauthorized() : EndpointResults.From(await organization.UpdateBrandAsync(
                current.TenantId, new UpdateBrandProfileCommand(request.Code ?? string.Empty,
                    request.Name ?? string.Empty, request.ExpectedVersion, current.Id), cancellationToken));
        });

        group.MapPost("/stores", async (StoreProfileRequest request, IIdentityService identity,
            IOrganizationService organization, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            return current is null ? Results.Unauthorized() : EndpointResults.From(await organization.CreateStoreAsync(
                current.TenantId, new CreateStoreCommand(request.Name ?? string.Empty,
                    request.TimeZoneId ?? string.Empty, current.Id), cancellationToken));
        });

        group.MapPut("/stores/{storeId:guid}", async (Guid storeId, StoreProfileRequest request,
            IIdentityService identity, IOrganizationService organization, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            return current is null ? Results.Unauthorized() : EndpointResults.From(await organization.UpdateStoreAsync(
                current.TenantId, new UpdateStoreCommand(storeId, request.Code ?? string.Empty,
                    request.Name ?? string.Empty, request.TimeZoneId ?? string.Empty, request.ExpectedVersion,
                    current.Id), cancellationToken));
        });

        group.MapPost("/stores/{storeId:guid}/status", async (Guid storeId, ChangeStoreStatusRequest request,
            IIdentityService identity, IOrganizationService organization, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            return current is null ? Results.Unauthorized() : EndpointResults.From(
                await organization.ChangeStoreStatusAsync(current.TenantId,
                    new ChangeStoreStatusCommand(storeId, request.Enable, request.Reason ?? string.Empty,
                        request.ExpectedVersion, current.Id), cancellationToken));
        });

        return endpoints;
    }

    private sealed record UpdateBrandRequest(string? Code, string? Name, uint ExpectedVersion);
    private sealed record StoreProfileRequest(string? Code, string? Name, string? TimeZoneId, uint ExpectedVersion);
    private sealed record ChangeStoreStatusRequest(bool Enable, string? Reason, uint ExpectedVersion);
    private sealed record NavigationLabelsRequest(Dictionary<string, string>? Labels, uint ExpectedVersion);
}
