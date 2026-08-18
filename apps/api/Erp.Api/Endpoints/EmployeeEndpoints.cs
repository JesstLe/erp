using Erp.Application.Common;
using Erp.Application.Identity;
using Erp.Application.Security;

namespace Erp.Api.Endpoints;

public static class EmployeeEndpoints
{
    public static IEndpointRouteBuilder MapEmployeeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/employees").WithTags("Employees")
            .RequireAuthorization(SystemPermissions.EmployeeManage);

        group.MapGet("", async (string? query, int? page, int? pageSize, IIdentityService identity,
            IEmployeeService employees,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (query?.Trim().Length > 100)
                return Results.UnprocessableEntity(new
                {
                    error = new { code = "VALIDATION_FAILED", message = "员工查询关键词不能超过100个字符" },
                });
            if (!Pagination.TryNormalize(page, pageSize, out var normalizedPage, out var normalizedPageSize))
                return Results.UnprocessableEntity(new
                {
                    error = new { code = "INVALID_PAGINATION", message = "页码必须大于0，每页数量必须为1到100" },
                });
            return Results.Ok(await employees.ListAsync(current.TenantId, query, normalizedPage,
                normalizedPageSize, cancellationToken));
        });

        group.MapGet("/roles", async (IIdentityService identity, IEmployeeService employees, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            return current is null ? Results.Unauthorized() : Results.Ok(await employees.ListRolesAsync(current.TenantId, cancellationToken));
        });

        group.MapPost("", async (CreateEmployeeRequest request, IIdentityService identity, IEmployeeService employees,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            var storeIds = (request.StoreIds ?? []).Distinct().ToList();
            if (storeIds.Any(id => current.Stores.All(store => store.Id != id))) return Results.Forbid();
            return EndpointResults.From(await employees.CreateAsync(current.TenantId, new CreateEmployeeCommand(
                request.EmployeeNo ?? string.Empty, request.DisplayName ?? string.Empty, request.PositionCode ?? string.Empty,
                storeIds, request.CreateLoginAccount, request.Account, request.InitialPassword, request.Roles ?? [], current.Id),
                cancellationToken));
        });

        group.MapPost("/{employeeId:guid}/account-status", async (Guid employeeId, SetAccountStatusRequest request,
            IIdentityService identity, IEmployeeService employees, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            return current is null ? Results.Unauthorized() : EndpointResults.From(await employees.SetAccountStatusAsync(
                current.TenantId, new SetEmployeeAccountStatusCommand(employeeId, request.IsEnabled, current.Id), cancellationToken));
        });

        group.MapPut("/{employeeId:guid}", async (Guid employeeId, UpdateEmployeeRequest request,
            IIdentityService identity, IEmployeeService employees, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            var storeIds = (request.StoreIds ?? []).Distinct().ToList();
            if (storeIds.Any(id => current.Stores.All(store => store.Id != id))) return Results.Forbid();
            return EndpointResults.From(await employees.UpdateAsync(current.TenantId,
                new UpdateEmployeeCommand(employeeId, request.DisplayName ?? string.Empty,
                    request.PositionCode ?? string.Empty, storeIds, request.Roles ?? [], request.ExpectedVersion,
                    current.Id), cancellationToken));
        });

        group.MapPost("/{employeeId:guid}/employment-status", async (Guid employeeId,
            ChangeEmploymentStatusRequest request, IIdentityService identity, IEmployeeService employees,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            return current is null ? Results.Unauthorized() : EndpointResults.From(
                await employees.ChangeEmploymentStatusAsync(current.TenantId,
                    new ChangeEmploymentStatusCommand(employeeId, request.Reactivate,
                        request.Reason ?? string.Empty, request.ExpectedVersion, current.Id), cancellationToken));
        });

        group.MapPost("/{employeeId:guid}/reset-password", async (Guid employeeId,
            ResetEmployeePasswordRequest request, IIdentityService identity, IEmployeeService employees,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            return current is null ? Results.Unauthorized() : EndpointResults.From(
                await employees.ResetPasswordAsync(current.TenantId,
                    new ResetEmployeePasswordCommand(employeeId, request.NewInitialPassword ?? string.Empty,
                        request.Reason ?? string.Empty, current.Id), cancellationToken));
        }).RequireRateLimiting("login");

        return endpoints;
    }

    private sealed record CreateEmployeeRequest(string? EmployeeNo, string? DisplayName, string? PositionCode,
        IReadOnlyList<Guid>? StoreIds, bool CreateLoginAccount, string? Account, string? InitialPassword,
        IReadOnlyList<string>? Roles);
    private sealed record SetAccountStatusRequest(bool IsEnabled);
    private sealed record UpdateEmployeeRequest(string? DisplayName, string? PositionCode,
        IReadOnlyList<Guid>? StoreIds, IReadOnlyList<string>? Roles, uint ExpectedVersion);
    private sealed record ChangeEmploymentStatusRequest(bool Reactivate, string? Reason, uint ExpectedVersion);
    private sealed record ResetEmployeePasswordRequest(string? NewInitialPassword, string? Reason);
}
