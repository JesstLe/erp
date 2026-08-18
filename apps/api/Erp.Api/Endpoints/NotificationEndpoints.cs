using Erp.Application.Identity;
using Erp.Application.Notifications;

namespace Erp.Api.Endpoints;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/notifications", async (Guid storeId, IIdentityService identity,
            INotificationService notifications, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            if (current is null) return Results.Unauthorized();
            if (!current.Stores.Any(x => x.Id == storeId)) return Results.Forbid();
            return Results.Ok(await notifications.GetInboxAsync(current.TenantId, storeId, current.Id,
                current.Roles, cancellationToken));
        }).RequireAuthorization().WithTags("Notifications");
        return endpoints;
    }
}
