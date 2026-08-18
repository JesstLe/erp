using Erp.Application.Customers;
using Erp.Application.Identity;
using Erp.Application.Security;

namespace Erp.Api.Endpoints;

public static class MemberVerificationEndpoints
{
    public static IEndpointRouteBuilder MapMemberVerificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/member-verifications").WithTags("Member verification")
            .RequireAuthorization(SystemPermissions.MembershipManage)
            .RequireRateLimiting("member-verification");

        group.MapPost("", async (IssueRequest request, IIdentityService identity,
            IMemberVerificationService verification, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await verification.IssueAsync(current.TenantId,
                new IssueMemberVerificationCommand(request.StoreId, request.OrderId,
                    request.MemberAmountMinor, request.FullMobile ?? string.Empty, current.Id),
                cancellationToken));
        });

        group.MapPost("/{challengeId:guid}/verify", async (Guid challengeId, VerifyRequest request,
            IIdentityService identity, IMemberVerificationService verification,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await verification.VerifyAsync(current.TenantId,
                new VerifyMemberChallengeCommand(request.StoreId, challengeId,
                    request.Code ?? string.Empty, current.Id), cancellationToken));
        });

        return endpoints;
    }

    private static bool HasStore(CurrentUserDto user, Guid storeId) => user.Stores.Any(x => x.Id == storeId);
    private sealed record IssueRequest(Guid StoreId, Guid OrderId, long MemberAmountMinor,
        string? FullMobile);
    private sealed record VerifyRequest(Guid StoreId, string? Code);
}
