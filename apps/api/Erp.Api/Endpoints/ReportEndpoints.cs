using Erp.Application.Identity;
using Erp.Application.Common;
using Erp.Application.Reports;
using Erp.Application.Security;

namespace Erp.Api.Endpoints;

public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/reports").WithTags("Reports")
            .RequireAuthorization(SystemPermissions.ReportRead);

        group.MapGet("/daily-cashier", async (Guid storeId, DateOnly? date,
            IIdentityService identity, IReportService reports, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (current.Stores.All(x => x.Id != storeId)) return Results.Forbid();
            try { return Results.Ok(await reports.GetDailyCashierAsync(current.TenantId, storeId, date, cancellationToken)); }
            catch (ArgumentException exception)
            {
                return EndpointResults.From(ResultFactory.Failure<object>("VALIDATION_FAILED", exception.Message));
            }
        });

        group.MapGet("/employee-commissions", async (Guid? storeId, DateOnly? fromDate, DateOnly? toDate,
            string? query, int? page, int? pageSize, Guid? employeeId, IIdentityService identity, IReportService reports,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (storeId.HasValue && current.Stores.All(x => x.Id != storeId.Value)) return Results.Forbid();
            if (!Pagination.TryNormalize(page, pageSize, out var normalizedPage, out var normalizedSize))
                return EndpointResults.InvalidPagination();
            if (query?.Trim().Length > 100)
                return EndpointResults.From(ResultFactory.Failure<object>("VALIDATION_FAILED", "查询关键词最多100个字符"));
            if (employeeId == Guid.Empty)
                return EndpointResults.From(ResultFactory.Failure<object>("VALIDATION_FAILED", "员工编号无效"));
            var stores = current.Stores.Where(x => !storeId.HasValue || x.Id == storeId.Value)
                .Select(x => x.Id).ToList();
            try
            {
                return Results.Ok(await reports.GetEmployeeCommissionsAsync(current.TenantId, stores,
                    fromDate, toDate, query, normalizedPage, normalizedSize, employeeId, cancellationToken));
            }
            catch (ArgumentException exception)
            {
                return EndpointResults.From(ResultFactory.Failure<object>("VALIDATION_FAILED", exception.Message));
            }
        });

        group.MapGet("/operations", async (Guid storeId, DateOnly? fromDate, DateOnly? toDate,
            IIdentityService identity, IReportService reports, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!current.Stores.Any(x => x.Id == storeId)) return Results.Forbid();
            try { return Results.Ok(await reports.GetOperationsAsync(current.TenantId, storeId, fromDate, toDate, cancellationToken)); }
            catch (ArgumentException exception)
            {
                return EndpointResults.From(Erp.Application.Common.ResultFactory.Failure<object>("VALIDATION_FAILED", exception.Message));
            }
        });

        group.MapGet("/store-overview", async (DateOnly? fromDate, DateOnly? toDate,
            IIdentityService identity, IReportService reports, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!current.Roles.Contains(SystemRoles.Owner, StringComparer.OrdinalIgnoreCase))
                return Results.Forbid();
            try
            {
                return Results.Ok(await reports.GetStoreOverviewAsync(current.TenantId, fromDate, toDate,
                    cancellationToken));
            }
            catch (ArgumentException exception)
            {
                return EndpointResults.From(Erp.Application.Common.ResultFactory.Failure<object>(
                    "VALIDATION_FAILED", exception.Message));
            }
        });

        group.MapGet("/dashboard-overview", async (Guid? storeId, IIdentityService identity,
            IReportService reports, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            var isOwner = current.Roles.Contains(SystemRoles.Owner, StringComparer.OrdinalIgnoreCase);
            if (!storeId.HasValue && !isOwner) return Results.Forbid();
            if (storeId.HasValue && !current.Stores.Any(x => x.Id == storeId.Value)) return Results.Forbid();
            try
            {
                return Results.Ok(await reports.GetDashboardOverviewAsync(current.TenantId,
                    isOwner ? storeId : storeId!.Value, cancellationToken));
            }
            catch (ArgumentException exception)
            {
                return EndpointResults.From(Erp.Application.Common.ResultFactory.Failure<object>(
                    "VALIDATION_FAILED", exception.Message));
            }
        });

        return endpoints;
    }
}
