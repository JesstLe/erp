using Erp.Application.Identity;
using Erp.Application.Reports;
using Erp.Application.Security;

namespace Erp.Api.Endpoints;

public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/reports").WithTags("Reports")
            .RequireAuthorization(SystemPermissions.ReportRead);

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

        return endpoints;
    }
}
