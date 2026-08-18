using Erp.Application.Cashier;
using Erp.Application.Common;
using Erp.Application.Customers;
using Erp.Application.Identity;
using Erp.Application.Security;

namespace Erp.Api.Endpoints;

public static class MemberTopupEndpoints
{
    public static IEndpointRouteBuilder MapMemberTopupEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/member-topups").WithTags("Member top-ups")
            .RequireAuthorization(SystemPermissions.MembershipTopup);

        group.MapGet("", async (Guid storeId, Guid? customerId, int? page, int? pageSize,
            IIdentityService identity,
            IMemberTopupService topups, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            if (!Pagination.TryNormalize(page, pageSize, out var normalizedPage, out var normalizedPageSize))
                return EndpointResults.InvalidPagination();
            return Results.Ok(await topups.ListAsync(current.TenantId, storeId, customerId, normalizedPage,
                normalizedPageSize, cancellationToken));
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
                    current.Permissions.Contains(SystemPermissions.MembershipGrantBonus)),
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
