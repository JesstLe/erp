using Erp.Application.Cashier;
using Erp.Application.Customers;
using Erp.Application.Identity;
using Erp.Application.Security;

namespace Erp.Api.Endpoints;

public static class MemberTopupEndpoints
{
    private static readonly string[] Operators =
        [SystemRoles.Owner, SystemRoles.StoreManager, SystemRoles.Cashier];

    public static IEndpointRouteBuilder MapMemberTopupEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/member-topups").WithTags("Member top-ups")
            .RequireAuthorization(policy => policy.RequireRole(Operators));

        group.MapGet("", async (Guid storeId, Guid? customerId, IIdentityService identity,
            IMemberTopupService topups, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            return Results.Ok(await topups.ListAsync(current.TenantId, storeId, customerId, cancellationToken));
        });

        group.MapPost("", async (CreateTopupRequest request, IIdentityService identity,
            IMemberTopupService topups, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await topups.CreateAndSettleAsync(current.TenantId,
                new CreateMemberTopupCommand(request.StoreId, request.CustomerId, request.CardId,
                    request.PrincipalMinor, request.BonusMinor, request.Note,
                    (request.Allocations ?? []).Select(x => new SettleAllocationCommand(x.MethodId,
                        x.AmountMinor, x.ExternalReference)).ToList(), request.CommandId, current.Id,
                    current.Roles.Contains(SystemRoles.Owner)),
                cancellationToken));
        });

        return endpoints;
    }

    private static bool HasStore(CurrentUserDto user, Guid storeId) => user.Stores.Any(x => x.Id == storeId);
    private sealed record AllocationRequest(Guid MethodId, long AmountMinor, string? ExternalReference);
    private sealed record CreateTopupRequest(Guid StoreId, Guid CustomerId, Guid CardId,
        long PrincipalMinor, long BonusMinor, string? Note, IReadOnlyList<AllocationRequest>? Allocations,
        Guid CommandId);
}
