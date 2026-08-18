using Erp.Application.Auditing;
using Erp.Application.Identity;
using Erp.Application.Security;

namespace Erp.Api.Endpoints;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/audit").WithTags("Audit")
            .RequireAuthorization(SystemPermissions.AuditRead);

        group.MapGet("/events", async (Guid storeId, string? action, string? entityType, DateOnly? fromDate,
            DateOnly? toDate, int? page, int? pageSize, IIdentityService identity, IAuditQueryService audit,
            CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!current.Stores.Any(x => x.Id == storeId)) return Results.Forbid();
            if (fromDate is not null && toDate is not null && fromDate > toDate)
                return EndpointResults.From(Erp.Application.Common.ResultFactory.Failure<object>("VALIDATION_FAILED", "开始日期不得晚于结束日期"));
            if (toDate == DateOnly.MaxValue)
                return EndpointResults.From(Erp.Application.Common.ResultFactory.Failure<object>("VALIDATION_FAILED", "结束日期超出允许范围"));
            return Results.Ok(await audit.QueryAsync(current.TenantId, storeId, action, entityType, fromDate, toDate,
                page ?? 1, pageSize ?? 50, cancellationToken));
        });

        return endpoints;
    }
}
