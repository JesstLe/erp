using Erp.Application.Platform;
using Erp.Infrastructure.Platform;
using Microsoft.AspNetCore.Antiforgery;

namespace Erp.Api.Endpoints;

public static class PlatformEndpoints
{
    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/public/merchant-registration-applications", async (
            MerchantRegistrationRequest request, HttpContext context, IMerchantRegistrationService registrations,
            CancellationToken cancellationToken) => EndpointResults.From(await registrations.SubmitAsync(
                new SubmitMerchantRegistrationCommand(request.MerchantName ?? string.Empty,
                    request.StoreName ?? string.Empty, request.ContactName ?? string.Empty,
                    request.ContactMobile ?? string.Empty, request.ContactEmail,
                    request.DesiredOwnerAccount ?? string.Empty, request.Note, request.AcceptedTerms,
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown"), cancellationToken),
                value => Results.Created($"/api/v1/public/merchant-registration-applications/{value.ApplicationNo}",
                    value)))
            .AllowAnonymous().RequireRateLimiting("merchant-registration");

        var auth = endpoints.MapGroup("/api/v1/platform/auth").WithTags("Platform Authentication");
        auth.MapPost("/login", async (PlatformLoginRequest request, IPlatformIdentityService identity,
            CancellationToken cancellationToken) => EndpointResults.From(await identity.LoginAsync(
                new PlatformLoginCommand(request.Account ?? string.Empty, request.Password ?? string.Empty,
                    request.RememberMe), cancellationToken)))
            .AllowAnonymous().RequireRateLimiting("platform-login");
        auth.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { token = tokens.RequestToken });
        }).RequireAuthorization(PlatformAuthentication.Policy);
        auth.MapGet("/me", async (IPlatformIdentityService identity, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            return current is null ? Results.Unauthorized() : Results.Ok(current);
        }).RequireAuthorization(PlatformAuthentication.Policy);
        auth.MapPost("/change-password", async (PlatformChangePasswordRequest request,
            IPlatformIdentityService identity, CancellationToken cancellationToken) => EndpointResults.From(
            await identity.ChangePasswordAsync(new PlatformChangePasswordCommand(request.CurrentPassword ?? string.Empty,
                request.NewPassword ?? string.Empty), cancellationToken)))
            .RequireAuthorization(PlatformAuthentication.Policy);
        auth.MapPost("/logout", async (IPlatformIdentityService identity, CancellationToken cancellationToken) =>
        {
            await identity.LogoutAsync(cancellationToken);
            return Results.NoContent();
        }).RequireAuthorization(PlatformAuthentication.Policy);

        var platform = endpoints.MapGroup("/api/v1/platform").WithTags("Platform Administration")
            .RequireAuthorization(PlatformAuthentication.Policy);
        platform.MapGet("/registration-applications", async (string? status, string? query, int? page,
            int? pageSize, IPlatformIdentityService identity, IPlatformAdminService service,
            CancellationToken cancellationToken) =>
        {
            var current = await ReadyPlatformUser(identity, cancellationToken);
            if (current is null) return Results.Forbid();
            if (!ValidPagination(page, pageSize)) return EndpointResults.InvalidPagination();
            return Results.Ok(await service.ListRegistrationsAsync(status, query, page ?? 1, pageSize ?? 20,
                cancellationToken));
        });
        platform.MapPost("/registration-applications/{applicationId:guid}/approval", async (Guid applicationId,
            ApproveRegistrationRequest request, IPlatformIdentityService identity, IPlatformAdminService service,
            CancellationToken cancellationToken) =>
        {
            var current = await ReadyPlatformUser(identity, cancellationToken);
            return current is null ? Results.Forbid() : EndpointResults.From(await service.ApproveAsync(current.Id,
                new ApproveMerchantRegistrationCommand(applicationId, request.InitialPassword ?? string.Empty,
                    request.Reason ?? string.Empty, request.ExpectedVersion), cancellationToken));
        });
        platform.MapPost("/registration-applications/{applicationId:guid}/rejection", async (Guid applicationId,
            RejectRegistrationRequest request, IPlatformIdentityService identity, IPlatformAdminService service,
            CancellationToken cancellationToken) =>
        {
            var current = await ReadyPlatformUser(identity, cancellationToken);
            return current is null ? Results.Forbid() : EndpointResults.From(await service.RejectAsync(current.Id,
                new RejectMerchantRegistrationCommand(applicationId, request.Reason ?? string.Empty,
                    request.ExpectedVersion), cancellationToken));
        });
        platform.MapGet("/merchants", async (string? status, string? query, int? page, int? pageSize,
            IPlatformIdentityService identity, IPlatformAdminService service,
            CancellationToken cancellationToken) =>
        {
            var current = await ReadyPlatformUser(identity, cancellationToken);
            if (current is null) return Results.Forbid();
            if (!ValidPagination(page, pageSize)) return EndpointResults.InvalidPagination();
            return Results.Ok(await service.ListMerchantsAsync(status, query, page ?? 1, pageSize ?? 20,
                cancellationToken));
        });
        platform.MapPost("/merchants/{tenantId:guid}/status-change", async (Guid tenantId,
            ChangeMerchantStatusRequest request, IPlatformIdentityService identity, IPlatformAdminService service,
            CancellationToken cancellationToken) =>
        {
            var current = await ReadyPlatformUser(identity, cancellationToken);
            return current is null ? Results.Forbid() : EndpointResults.From(await service.ChangeMerchantStatusAsync(
                current.Id, new ChangeMerchantStatusCommand(tenantId, request.Enable,
                    request.Reason ?? string.Empty, request.ExpectedVersion), cancellationToken));
        });
        platform.MapGet("/security-events", async (string? scope, string? resultCode, Guid? tenantId,
            string? account, DateOnly? fromDate, DateOnly? toDate, int? page, int? pageSize,
            IPlatformIdentityService identity, IPlatformAdminService service,
            CancellationToken cancellationToken) =>
        {
            var current = await ReadyPlatformUser(identity, cancellationToken);
            if (current is null) return Results.Forbid();
            if (!ValidPagination(page, pageSize)) return EndpointResults.InvalidPagination();
            if (fromDate is not null && toDate is not null && fromDate > toDate)
                return EndpointResults.From(Erp.Application.Common.ResultFactory.Failure<object>(
                    "VALIDATION_FAILED", "开始日期不得晚于结束日期"));
            return Results.Ok(await service.ListSecurityEventsAsync(scope, resultCode, tenantId, account, fromDate,
                toDate, page ?? 1, pageSize ?? 50, cancellationToken));
        });
        return endpoints;
    }

    private static async Task<PlatformCurrentUserDto?> ReadyPlatformUser(IPlatformIdentityService identity,
        CancellationToken cancellationToken)
    {
        var current = await identity.GetCurrentAsync(cancellationToken);
        return current is { MustChangePassword: false } ? current : null;
    }

    private static bool ValidPagination(int? page, int? pageSize) => (page ?? 1) > 0 &&
        (pageSize ?? 20) is > 0 and <= 100;

    private sealed record MerchantRegistrationRequest(string? MerchantName, string? StoreName, string? ContactName,
        string? ContactMobile, string? ContactEmail, string? DesiredOwnerAccount, string? Note, bool AcceptedTerms);
    private sealed record PlatformLoginRequest(string? Account, string? Password, bool RememberMe);
    private sealed record PlatformChangePasswordRequest(string? CurrentPassword, string? NewPassword);
    private sealed record ApproveRegistrationRequest(string? InitialPassword, string? Reason, uint ExpectedVersion);
    private sealed record RejectRegistrationRequest(string? Reason, uint ExpectedVersion);
    private sealed record ChangeMerchantStatusRequest(bool Enable, string? Reason, uint ExpectedVersion);
}
