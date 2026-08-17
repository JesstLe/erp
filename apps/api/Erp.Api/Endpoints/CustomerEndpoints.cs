using Erp.Application.Customers;
using Erp.Application.Identity;
using Erp.Application.Security;

namespace Erp.Api.Endpoints;

public static class CustomerEndpoints
{
    private static readonly string[] CustomerOperators =
        [SystemRoles.Owner, SystemRoles.StoreManager, SystemRoles.FrontDesk, SystemRoles.Cashier];
    private static readonly string[] MembershipOperators = [SystemRoles.Owner, SystemRoles.StoreManager];

    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/customers").WithTags("Customers").RequireAuthorization();

        group.MapGet("", async (Guid storeId, string? query, IIdentityService identity, ICustomerService customers,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            return Results.Ok(await customers.SearchAsync(current.TenantId, storeId, query, cancellationToken));
        }).RequireAuthorization(policy => policy.RequireRole(CustomerOperators)).RequireRateLimiting("customer-search");

        group.MapGet("/{customerId:guid}", async (Guid customerId, Guid storeId, IIdentityService identity,
            ICustomerService customers, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            return EndpointResults.From(await customers.GetAsync(current.TenantId, storeId, customerId, cancellationToken));
        }).RequireAuthorization(policy => policy.RequireRole(CustomerOperators));

        group.MapPost("", async (CreateCustomerRequest request, IIdentityService identity, ICustomerService customers,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await customers.CreateAsync(current.TenantId,
                new CreateCustomerCommand(request.StoreId, request.Name ?? string.Empty, request.Mobile ?? string.Empty,
                    request.Gender, request.BirthDate, request.SourceCode, request.ServiceNotificationConsent,
                    request.MarketingConsent, request.CommandId, current.Id), cancellationToken));
        }).RequireAuthorization(policy => policy.RequireRole(CustomerOperators));

        group.MapGet("/membership/card-types", async (IIdentityService identity, ICustomerService customers,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            return current is null ? Results.Unauthorized() : Results.Ok(await customers.ListCardTypesAsync(current.TenantId, cancellationToken));
        }).RequireAuthorization(policy => policy.RequireRole(CustomerOperators));

        group.MapPost("/membership/card-types", async (CreateCardTypeRequest request, IIdentityService identity,
            ICustomerService customers, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            return current is null ? Results.Unauthorized() : EndpointResults.From(await customers.CreateCardTypeAsync(current.TenantId,
                new CreateMemberCardTypeCommand(request.Code ?? string.Empty, request.Name ?? string.Empty,
                    request.ValidityDays, request.CommandId, current.Id), cancellationToken));
        }).RequireAuthorization(policy => policy.RequireRole(SystemRoles.Owner));

        group.MapPost("/{customerId:guid}/membership", async (Guid customerId, OpenMembershipRequest request,
            IIdentityService identity, ICustomerService customers, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await customers.OpenMembershipAsync(current.TenantId,
                new OpenMembershipCommand(request.StoreId, customerId, request.CardTypeId, request.CardNo,
                    request.Note, request.CommandId, current.Id), cancellationToken));
        }).RequireAuthorization(policy => policy.RequireRole(MembershipOperators));

        return endpoints;
    }

    private static bool HasStore(CurrentUserDto user, Guid storeId) => user.Stores.Any(x => x.Id == storeId);

    private sealed record CreateCustomerRequest(Guid StoreId, string? Name, string? Mobile, string? Gender,
        DateOnly? BirthDate, string? SourceCode, bool ServiceNotificationConsent, bool MarketingConsent, Guid CommandId);
    private sealed record CreateCardTypeRequest(string? Code, string? Name, int? ValidityDays, Guid CommandId);
    private sealed record OpenMembershipRequest(Guid StoreId, Guid CardTypeId, string? CardNo, string? Note, Guid CommandId);
}
