using Erp.Application.Facilities;
using Erp.Application.Identity;
using Erp.Application.Security;

namespace Erp.Api.Endpoints;

public static class FacilityEndpoints
{
    private static readonly string[] Operators = [SystemRoles.Owner, SystemRoles.StoreManager, SystemRoles.FrontDesk];

    public static IEndpointRouteBuilder MapFacilityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/facilities").WithTags("Facilities").RequireAuthorization();

        group.MapGet("/board", async (Guid storeId, IIdentityService identity, IFacilityService facilities, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            return EndpointResults.From(await facilities.GetBoardAsync(current.TenantId, storeId, cancellationToken));
        });

        group.MapGet("/groups", async (Guid storeId, IIdentityService identity, IFacilityService facilities, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            return HasStore(current, storeId) ? Results.Ok(await facilities.ListGroupsAsync(current.TenantId, storeId, cancellationToken)) : Results.Forbid();
        });

        group.MapGet("/types", async (IIdentityService identity, IFacilityService facilities, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            return current is null ? Results.Unauthorized() : Results.Ok(await facilities.ListTypesAsync(current.TenantId, cancellationToken));
        });

        group.MapPost("/groups", async (CreateGroupRequest request, IIdentityService identity, IFacilityService facilities, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await facilities.CreateGroupAsync(current.TenantId,
                new CreateFacilityGroupCommand(request.StoreId, request.DisplayName ?? string.Empty, request.SortOrder), cancellationToken));
        }).RequireAuthorization(policy => policy.RequireRole(SystemRoles.Owner));

        group.MapPost("/types", async (CreateTypeRequest request, IIdentityService identity, IFacilityService facilities, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            return current is null ? Results.Unauthorized() : EndpointResults.From(await facilities.CreateTypeAsync(current.TenantId,
                new CreateFacilityTypeCommand(request.DisplayName ?? string.Empty), cancellationToken));
        }).RequireAuthorization(policy => policy.RequireRole(SystemRoles.Owner));

        group.MapPost("", async (CreateFacilityRequest request, IIdentityService identity, IFacilityService facilities, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await facilities.CreateFacilityAsync(current.TenantId,
                new CreateFacilityCommand(request.StoreId, request.GroupId, request.FacilityTypeId, request.Code ?? string.Empty,
                    request.DisplayName ?? string.Empty, request.SortOrder, request.DefaultCleaningMinutes, request.AllowReservation), cancellationToken));
        }).RequireAuthorization(policy => policy.RequireRole(SystemRoles.Owner));

        group.MapPost("/sessions/start", async (StartSessionRequest request, IIdentityService identity, IFacilityService facilities, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await facilities.StartAsync(current.TenantId,
                new StartFacilitySessionCommand(request.StoreId, request.FacilityId, request.ExpectedDurationMinutes,
                    request.Note, request.CommandId, current.Id), cancellationToken));
        }).RequireAuthorization(policy => policy.RequireRole(Operators));

        group.MapPost("/sessions/{sessionId:guid}/pause", (Guid sessionId, SessionCommandRequest request, IIdentityService identity,
            IFacilityService facilities, CancellationToken cancellationToken) => Operate(sessionId, request, identity, facilities,
                (service, tenantId, command, token) => service.PauseAsync(tenantId, command, token), cancellationToken))
            .RequireAuthorization(policy => policy.RequireRole(Operators));
        group.MapPost("/sessions/{sessionId:guid}/resume", (Guid sessionId, SessionCommandRequest request, IIdentityService identity,
            IFacilityService facilities, CancellationToken cancellationToken) => Operate(sessionId, request, identity, facilities,
                (service, tenantId, command, token) => service.ResumeAsync(tenantId, command, token), cancellationToken))
            .RequireAuthorization(policy => policy.RequireRole(Operators));
        group.MapPost("/sessions/{sessionId:guid}/end", (Guid sessionId, SessionCommandRequest request, IIdentityService identity,
            IFacilityService facilities, CancellationToken cancellationToken) => Operate(sessionId, request, identity, facilities,
                (service, tenantId, command, token) => service.EndAsync(tenantId, command, token), cancellationToken))
            .RequireAuthorization(policy => policy.RequireRole(Operators));

        group.MapPost("/sessions/{sessionId:guid}/switch", async (Guid sessionId, SwitchSessionRequest request, IIdentityService identity,
            IFacilityService facilities, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await facilities.SwitchAsync(current.TenantId,
                new SwitchFacilityCommand(request.StoreId, sessionId, request.TargetFacilityId, request.Reason,
                    request.CommandId, current.Id), cancellationToken));
        }).RequireAuthorization(policy => policy.RequireRole(Operators));

        group.MapPost("/{facilityId:guid}/cleaning/complete", async (Guid facilityId, SessionCommandRequest request,
            IIdentityService identity, IFacilityService facilities, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await facilities.CompleteCleaningAsync(current.TenantId, request.StoreId, facilityId,
                request.CommandId, current.Id, cancellationToken));
        }).RequireAuthorization(policy => policy.RequireRole(Operators));

        return endpoints;
    }

    private static async Task<IResult> Operate(Guid sessionId, SessionCommandRequest request, IIdentityService identity,
        IFacilityService facilities,
        Func<IFacilityService, Guid, OperateFacilitySessionCommand, CancellationToken, Task<Erp.Application.Common.Result<FacilityBoardItemDto>>> action,
        CancellationToken cancellationToken)
    {
        var current = await identity.GetCurrentAsync(cancellationToken);
        if (current is null) return Results.Unauthorized();
        if (!HasStore(current, request.StoreId)) return Results.Forbid();
        return EndpointResults.From(await action(facilities, current.TenantId,
            new OperateFacilitySessionCommand(request.StoreId, sessionId, request.CommandId, current.Id), cancellationToken));
    }

    private static bool HasStore(CurrentUserDto user, Guid storeId) => user.Stores.Any(x => x.Id == storeId);

    private sealed record CreateGroupRequest(Guid StoreId, string? DisplayName, int SortOrder);
    private sealed record CreateTypeRequest(string? DisplayName);
    private sealed record CreateFacilityRequest(Guid StoreId, Guid GroupId, Guid FacilityTypeId, string? Code, string? DisplayName,
        int SortOrder, int DefaultCleaningMinutes, bool AllowReservation);
    private sealed record StartSessionRequest(Guid StoreId, Guid FacilityId, int? ExpectedDurationMinutes, string? Note, Guid CommandId);
    private sealed record SessionCommandRequest(Guid StoreId, Guid CommandId);
    private sealed record SwitchSessionRequest(Guid StoreId, Guid TargetFacilityId, string? Reason, Guid CommandId);
}
