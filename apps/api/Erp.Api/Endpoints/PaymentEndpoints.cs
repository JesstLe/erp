using Erp.Application.Cashier;
using Erp.Application.Identity;
using Erp.Application.Security;

namespace Erp.Api.Endpoints;

public static class PaymentEndpoints
{
    private static readonly string[] CashierRoles = [SystemRoles.Owner, SystemRoles.StoreManager, SystemRoles.Cashier];
    private static readonly string[] ReviewerRoles = [SystemRoles.Owner, SystemRoles.StoreManager];

    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/payments").WithTags("Payments")
            .RequireAuthorization(policy => policy.RequireRole(CashierRoles));

        group.MapGet("/methods", async (IIdentityService identity, IPaymentService payments,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            return current is null ? Results.Unauthorized() : Results.Ok(await payments.ListMethodsAsync(current.TenantId, cancellationToken));
        });

        group.MapGet("", async (Guid storeId, IIdentityService identity, IPaymentService payments,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            return HasStore(current, storeId) ? Results.Ok(await payments.ListPaymentsAsync(current.TenantId, storeId, cancellationToken)) : Results.Forbid();
        });

        group.MapPost("/orders/{orderId:guid}/settle", async (Guid orderId, SettleRequest request,
            IIdentityService identity, IPaymentService payments, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await payments.SettleOrderAsync(current.TenantId,
                new SettleOrderCommand(request.StoreId, orderId, request.ExpectedVersion,
                    (request.Allocations ?? []).Select(x => new SettleAllocationCommand(x.MethodId, x.AmountMinor,
                        x.ExternalReference, x.MemberAccountId)).ToList(), request.VerifiedMobile,
                    request.VerificationChallengeId, request.CommandId, current.Id), cancellationToken));
        });

        group.MapGet("/shifts/current", async (Guid storeId, IIdentityService identity, IPaymentService payments,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            var shift = await payments.GetCurrentShiftAsync(current.TenantId, storeId, current.Id, cancellationToken);
            return Results.Ok(shift);
        });

        group.MapGet("/shifts", async (Guid storeId, IIdentityService identity, IPaymentService payments,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            return Results.Ok(await payments.ListShiftsAsync(current.TenantId, storeId, cancellationToken));
        }).RequireAuthorization(policy => policy.RequireRole(ReviewerRoles));

        group.MapPost("/shifts/open", async (OpenShiftRequest request, IIdentityService identity,
            IPaymentService payments, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await payments.OpenShiftAsync(current.TenantId,
                new OpenShiftCommand(request.StoreId, request.OpeningCashMinor, request.CommandId, current.Id), cancellationToken));
        });

        group.MapPost("/shifts/{shiftId:guid}/submit", async (Guid shiftId, SubmitShiftRequest request,
            IIdentityService identity, IPaymentService payments, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await payments.SubmitShiftAsync(current.TenantId,
                new SubmitShiftCommand(request.StoreId, shiftId, request.ExpectedVersion, request.SubmittedCashMinor,
                    request.Note, request.CommandId, current.Id), cancellationToken));
        });

        group.MapPost("/shifts/{shiftId:guid}/review", async (Guid shiftId, ReviewShiftRequest request,
            IIdentityService identity, IPaymentService payments, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await payments.ReviewShiftAsync(current.TenantId,
                new ReviewShiftCommand(request.StoreId, shiftId, request.ExpectedVersion, request.Reason,
                    request.CommandId, current.Id, current.Roles.Contains(SystemRoles.Owner)), cancellationToken));
        }).RequireAuthorization(policy => policy.RequireRole(ReviewerRoles));

        return endpoints;
    }

    private static bool HasStore(CurrentUserDto user, Guid storeId) => user.Stores.Any(x => x.Id == storeId);
    private sealed record AllocationRequest(Guid MethodId, long AmountMinor, string? ExternalReference,
        Guid? MemberAccountId);
    private sealed record SettleRequest(Guid StoreId, uint ExpectedVersion, IReadOnlyList<AllocationRequest>? Allocations,
        string? VerifiedMobile, Guid? VerificationChallengeId, Guid CommandId);
    private sealed record OpenShiftRequest(Guid StoreId, long OpeningCashMinor, Guid CommandId);
    private sealed record SubmitShiftRequest(Guid StoreId, uint ExpectedVersion, long SubmittedCashMinor, string? Note,
        Guid CommandId);
    private sealed record ReviewShiftRequest(Guid StoreId, uint ExpectedVersion, string? Reason, Guid CommandId);
}
