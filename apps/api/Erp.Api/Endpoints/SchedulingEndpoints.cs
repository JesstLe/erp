using Erp.Application.Common;
using Erp.Application.Identity;
using Erp.Application.Scheduling;
using Erp.Application.Security;
using Erp.Domain.Scheduling;

namespace Erp.Api.Endpoints;

public static class SchedulingEndpoints
{
    public static IEndpointRouteBuilder MapSchedulingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/scheduling").WithTags("Scheduling")
            .RequireAuthorization(SystemPermissions.SchedulingOperate);

        group.MapGet("/appointments", async (Guid storeId, DateTimeOffset? fromUtc, DateTimeOffset? toUtc,
            string? status, string? query, int? page, int? pageSize, IIdentityService identity,
            ISchedulingService scheduling, TimeProvider clock, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            var from = fromUtc ?? clock.GetUtcNow().AddDays(-1);
            var to = toUtc ?? clock.GetUtcNow().AddDays(30);
            if (!ValidRange(from, to)) return InvalidRange();
            if (!string.IsNullOrWhiteSpace(status) && !Enum.TryParse<AppointmentStatus>(status, true, out _))
                return Results.UnprocessableEntity(new { error = new { code = "VALIDATION_FAILED", message = "预约状态无效" } });
            if (query?.Trim().Length > 100)
                return Results.UnprocessableEntity(new { error = new { code = "VALIDATION_FAILED", message = "查询关键词不能超过100个字符" } });
            if (!Pagination.TryNormalize(page, pageSize, out var normalizedPage, out var normalizedSize))
                return EndpointResults.InvalidPagination();
            return Results.Ok(await scheduling.ListAppointmentsAsync(current.TenantId, storeId, from, to,
                status, query, normalizedPage, normalizedSize, cancellationToken));
        }).RequireAuthorization(SystemPermissions.SchedulingOperate);

        group.MapGet("/shifts", async (Guid storeId, DateTimeOffset? fromUtc, DateTimeOffset? toUtc,
            int? page, int? pageSize, IIdentityService identity, ISchedulingService scheduling,
            TimeProvider clock, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, storeId)) return Results.Forbid();
            var from = fromUtc ?? clock.GetUtcNow().AddDays(-1);
            var to = toUtc ?? clock.GetUtcNow().AddDays(30);
            if (!ValidRange(from, to)) return InvalidRange();
            if (!Pagination.TryNormalize(page, pageSize, out var normalizedPage, out var normalizedSize))
                return EndpointResults.InvalidPagination();
            return Results.Ok(await scheduling.ListShiftsAsync(current.TenantId, storeId, from, to,
                normalizedPage, normalizedSize, cancellationToken));
        }).RequireAuthorization(SystemPermissions.SchedulingShiftManage);

        group.MapGet("/employees", async (Guid storeId, IIdentityService identity,
            ISchedulingService scheduling, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            return HasStore(current, storeId)
                ? Results.Ok(await scheduling.ListEmployeesAsync(current.TenantId, storeId, cancellationToken))
                : Results.Forbid();
        }).RequireAuthorization(SystemPermissions.SchedulingOperate);

        group.MapGet("/facilities", async (Guid storeId, IIdentityService identity,
            ISchedulingService scheduling, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            return HasStore(current, storeId)
                ? Results.Ok(await scheduling.ListFacilitiesAsync(current.TenantId, storeId, cancellationToken))
                : Results.Forbid();
        }).RequireAuthorization(SystemPermissions.SchedulingOperate);

        group.MapPost("/appointments", async (CreateAppointmentRequest request, IIdentityService identity,
            ISchedulingService scheduling, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await scheduling.CreateAppointmentAsync(current.TenantId,
                new CreateAppointmentCommand(request.StoreId, request.CustomerId, request.ServiceItemId,
                    request.EmployeeId, request.FacilityId, request.StartsAtUtc, request.EndsAtUtc,
                    request.Note, request.CommandId, current.Id), cancellationToken));
        }).RequireAuthorization(SystemPermissions.SchedulingOperate);

        group.MapPut("/appointments/{appointmentId:guid}", async (Guid appointmentId,
            UpdateAppointmentRequest request, IIdentityService identity, ISchedulingService scheduling,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await scheduling.UpdateAppointmentAsync(current.TenantId,
                new UpdateAppointmentCommand(request.StoreId, appointmentId, request.ServiceItemId,
                    request.EmployeeId, request.FacilityId, request.StartsAtUtc, request.EndsAtUtc,
                    request.Note, request.ExpectedVersion, current.Id), cancellationToken));
        }).RequireAuthorization(SystemPermissions.SchedulingOperate);

        group.MapPost("/appointments/{appointmentId:guid}/cancel", async (Guid appointmentId,
            TransitionRequest request, IIdentityService identity, ISchedulingService scheduling,
            CancellationToken cancellationToken) => await AppointmentTransition(appointmentId, request, identity,
                scheduling, (service, tenantId, command, token) => service.CancelAppointmentAsync(tenantId, command, token),
                cancellationToken)).RequireAuthorization(SystemPermissions.SchedulingOperate);

        group.MapPost("/appointments/{appointmentId:guid}/no-show", async (Guid appointmentId,
            TransitionRequest request, IIdentityService identity, ISchedulingService scheduling,
            CancellationToken cancellationToken) => await AppointmentTransition(appointmentId, request, identity,
                scheduling, (service, tenantId, command, token) => service.MarkNoShowAsync(tenantId, command, token),
                cancellationToken)).RequireAuthorization(SystemPermissions.SchedulingOperate);

        group.MapPost("/appointments/{appointmentId:guid}/arrive", async (Guid appointmentId,
            TransitionRequest request, IIdentityService identity, ISchedulingService scheduling,
            CancellationToken cancellationToken) => await AppointmentTransition(appointmentId, request, identity,
                scheduling, (service, tenantId, command, token) => service.ArriveAsync(tenantId, command, token),
                cancellationToken)).RequireAuthorization(SystemPermissions.SchedulingOperate);

        group.MapPost("/shifts", async (CreateShiftRequest request, IIdentityService identity,
            ISchedulingService scheduling, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await scheduling.CreateShiftAsync(current.TenantId,
                new CreateEmployeeShiftCommand(request.StoreId, request.EmployeeId, request.StartsAtUtc,
                    request.EndsAtUtc, request.Note, request.CommandId, current.Id), cancellationToken));
        }).RequireAuthorization(SystemPermissions.SchedulingShiftManage);

        group.MapPut("/shifts/{shiftId:guid}", async (Guid shiftId, UpdateShiftRequest request,
            IIdentityService identity, ISchedulingService scheduling, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await scheduling.UpdateShiftAsync(current.TenantId,
                new UpdateEmployeeShiftCommand(request.StoreId, shiftId, request.StartsAtUtc, request.EndsAtUtc,
                    request.Note, request.ExpectedVersion, current.Id), cancellationToken));
        }).RequireAuthorization(SystemPermissions.SchedulingShiftManage);

        group.MapPost("/shifts/{shiftId:guid}/cancel", async (Guid shiftId, CancelShiftRequest request,
            IIdentityService identity, ISchedulingService scheduling, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!HasStore(current, request.StoreId)) return Results.Forbid();
            return EndpointResults.From(await scheduling.CancelShiftAsync(current.TenantId,
                new CancelEmployeeShiftCommand(request.StoreId, shiftId, request.Reason ?? string.Empty,
                    request.ExpectedVersion, request.CommandId, current.Id), cancellationToken));
        }).RequireAuthorization(SystemPermissions.SchedulingShiftManage);

        return endpoints;
    }

    private static async Task<IResult> AppointmentTransition(Guid appointmentId, TransitionRequest request,
        IIdentityService identity, ISchedulingService scheduling,
        Func<ISchedulingService, Guid, TransitionAppointmentCommand, CancellationToken,
            Task<Result<AppointmentDto>>> action, CancellationToken cancellationToken)
    {
        var current = await identity.GetCurrentAsync(cancellationToken);
        if (current is null) return Results.Unauthorized();
        if (!HasStore(current, request.StoreId)) return Results.Forbid();
        return EndpointResults.From(await action(scheduling, current.TenantId,
            new TransitionAppointmentCommand(request.StoreId, appointmentId, request.Reason,
                request.ExpectedVersion, request.CommandId, current.Id), cancellationToken));
    }

    private static bool HasStore(CurrentUserDto current, Guid storeId) =>
        current.Roles.Contains(SystemRoles.Owner) || current.Stores.Any(store => store.Id == storeId);
    private static bool ValidRange(DateTimeOffset from, DateTimeOffset to) =>
        to > from && to - from <= TimeSpan.FromDays(92);
    private static IResult InvalidRange() => Results.UnprocessableEntity(new
    {
        error = new { code = "VALIDATION_FAILED", message = "查询时间范围必须为正且不能超过92天" },
    });

    private sealed record CreateAppointmentRequest(Guid StoreId, Guid CustomerId, Guid ServiceItemId,
        Guid? EmployeeId, Guid? FacilityId, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc,
        string? Note, Guid CommandId);
    private sealed record UpdateAppointmentRequest(Guid StoreId, Guid ServiceItemId, Guid? EmployeeId,
        Guid? FacilityId, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc, string? Note,
        uint ExpectedVersion);
    private sealed record TransitionRequest(Guid StoreId, string? Reason, uint ExpectedVersion, Guid CommandId);
    private sealed record CreateShiftRequest(Guid StoreId, Guid EmployeeId, DateTimeOffset StartsAtUtc,
        DateTimeOffset EndsAtUtc, string? Note, Guid CommandId);
    private sealed record UpdateShiftRequest(Guid StoreId, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc,
        string? Note, uint ExpectedVersion);
    private sealed record CancelShiftRequest(Guid StoreId, string? Reason, uint ExpectedVersion, Guid CommandId);
}
