using Erp.Application.Customers;
using Erp.Application.Identity;
using Erp.Application.Security;

namespace Erp.Api.Endpoints;

public static class MembershipBenefitEndpoints
{
    public static IEndpointRouteBuilder MapMembershipBenefitEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/membership-benefits").WithTags("Membership benefits")
            .RequireAuthorization(SystemPermissions.MembershipManage);

        group.MapGet("", async (Guid storeId, Guid customerId, IIdentityService identity,
            IMembershipBenefitService service, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            return EndpointResults.From(await service.GetAsync(current.TenantId, storeId, customerId,
                cancellationToken));
        });

        group.MapPost("/service-passes", async (IssuePassRequest request, IIdentityService identity,
            IMembershipBenefitService service, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await service.IssuePassAsync(current.TenantId,
                new IssueServicePassCommand(request.StoreId, request.CustomerId, request.CardId,
                    request.ServiceItemId, request.PassName ?? string.Empty, request.PurchasedUses,
                    request.BonusUses, request.ValidFrom, request.ValidTo, request.Reason ?? string.Empty,
                    request.CommandId, current.Id), cancellationToken));
        }).RequireAuthorization(SystemPermissions.MembershipAdmin);

        group.MapPost("/service-passes/{passId:guid}/redeem", async (Guid passId,
            RedeemPassRequest request, IIdentityService identity, IMembershipBenefitService service,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await service.RedeemPassAsync(current.TenantId,
                new RedeemServicePassCommand(request.StoreId, passId, request.Uses,
                    request.ServiceOrderId, request.Reason ?? string.Empty, request.ExpectedVersion,
                    request.CommandId, current.Id), cancellationToken));
        }).RequireAuthorization(SystemPermissions.MembershipManage);

        group.MapPost("/service-passes/{passId:guid}/reverse", async (Guid passId,
            ReversePassRequest request, IIdentityService identity, IMembershipBenefitService service,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await service.ReversePassAsync(current.TenantId,
                new ReverseServicePassCommand(request.StoreId, passId, request.LedgerId,
                    request.Reason ?? string.Empty, request.ExpectedVersion, request.CommandId, current.Id),
                cancellationToken));
        }).RequireAuthorization(SystemPermissions.MembershipReverse);

        group.MapPost("/service-passes/{passId:guid}/expire", async (Guid passId,
            ExpirePassRequest request, IIdentityService identity, IMembershipBenefitService service,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await service.ExpirePassAsync(current.TenantId,
                new ExpireServicePassCommand(request.StoreId, passId, request.Reason ?? string.Empty,
                    request.ExpectedVersion, request.CommandId, current.Id), cancellationToken));
        }).RequireAuthorization(SystemPermissions.MembershipAdmin);

        group.MapPost("/points/adjust", async (AdjustPointsRequest request, IIdentityService identity,
            IMembershipBenefitService service, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await service.AdjustPointsAsync(current.TenantId,
                new AdjustMemberPointsCommand(request.StoreId, request.CustomerId, request.CardId,
                    request.Units, request.Credit, request.ExpiresOn, request.Reason ?? string.Empty,
                    request.CommandId, current.Id), cancellationToken));
        }).RequireAuthorization(SystemPermissions.MembershipAdmin);

        group.MapPost("/points/reverse", async (ReversePointsRequest request, IIdentityService identity,
            IMembershipBenefitService service, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await service.ReversePointsAsync(current.TenantId,
                new ReverseMemberPointsCommand(request.StoreId, request.CardId, request.LedgerId,
                    request.Reason ?? string.Empty, request.CommandId, current.Id), cancellationToken));
        }).RequireAuthorization(SystemPermissions.MembershipReverse);

        group.MapPost("/points/expire", async (ExpirePointsRequest request, IIdentityService identity,
            IMembershipBenefitService service, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await service.ExpirePointsAsync(current.TenantId,
                new ExpireMemberPointsCommand(request.StoreId, request.CardId,
                    request.Reason ?? string.Empty, request.CommandId, current.Id), cancellationToken));
        }).RequireAuthorization(SystemPermissions.MembershipAdmin);

        return endpoints;
    }

    private static bool HasStore(CurrentUserDto user, Guid storeId) => user.Stores.Any(x => x.Id == storeId);

    private sealed record IssuePassRequest(Guid StoreId, Guid CustomerId, Guid CardId,
        Guid ServiceItemId, string? PassName, int PurchasedUses, int BonusUses, DateOnly ValidFrom,
        DateOnly? ValidTo, string? Reason, Guid CommandId);
    private sealed record RedeemPassRequest(Guid StoreId, int Uses, Guid? ServiceOrderId,
        string? Reason, uint ExpectedVersion, Guid CommandId);
    private sealed record ReversePassRequest(Guid StoreId, Guid LedgerId, string? Reason,
        uint ExpectedVersion, Guid CommandId);
    private sealed record ExpirePassRequest(Guid StoreId, string? Reason, uint ExpectedVersion,
        Guid CommandId);
    private sealed record AdjustPointsRequest(Guid StoreId, Guid CustomerId, Guid CardId,
        long Units, bool Credit, DateOnly? ExpiresOn, string? Reason, Guid CommandId);
    private sealed record ReversePointsRequest(Guid StoreId, Guid CardId, Guid LedgerId,
        string? Reason, Guid CommandId);
    private sealed record ExpirePointsRequest(Guid StoreId, Guid CardId, string? Reason,
        Guid CommandId);
}
