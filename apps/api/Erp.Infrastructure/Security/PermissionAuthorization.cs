using Erp.Application.Security;
using Erp.Infrastructure.Identity;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Erp.Infrastructure.Security;

internal sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

internal static class LocalAuthorizationBypass
{
    public static bool IsEnabled(string environmentName, string? configured) =>
        string.Equals(environmentName, Environments.Development, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(configured, "true", StringComparison.OrdinalIgnoreCase);
}

internal sealed class PermissionAuthorizationHandler(ErpDbContext db, UserManager<ApplicationUser> userManager,
    IHostEnvironment environment, IConfiguration configuration)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true) return;

        var localBypassEnabled = LocalAuthorizationBypass.IsEnabled(environment.EnvironmentName,
            configuration["ERP_LOCAL_AUTHORIZATION_BYPASS"]);
        if (localBypassEnabled)
        {
            context.Succeed(requirement);
            return;
        }

        var userIdText = userManager.GetUserId(context.User);
        if (!Guid.TryParse(userIdText, out var userId)) return;

        var authorized = await (
            from user in db.Users.AsNoTracking()
            join userRole in db.UserRoles.AsNoTracking() on user.Id equals userRole.UserId
            join role in db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            join grant in db.RoleActionGrants.AsNoTracking() on role.Id equals grant.RoleId
            where user.Id == userId && user.IsEnabled && role.TenantId == user.TenantId &&
                  grant.TenantId == user.TenantId && grant.Action == requirement.Permission
            select grant.Id).AnyAsync();

        if (authorized) context.Succeed(requirement);
    }
}

internal static class PermissionAuthorizationRegistration
{
    public static IServiceCollection AddErpPermissionAuthorization(this IServiceCollection services)
    {
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddAuthorization(options =>
        {
            foreach (var permission in SystemPermissions.All)
                options.AddPolicy(permission, policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.AddRequirements(new PermissionRequirement(permission));
                });
        });
        return services;
    }
}
