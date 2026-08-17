using Erp.Application.Catalog;
using Erp.Application.Identity;
using Erp.Infrastructure.Catalog;
using Erp.Infrastructure.Identity;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Erp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddErpInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString("ErpDatabase")
            ?? throw new InvalidOperationException("缺少 ConnectionStrings:ErpDatabase 配置");

        services.AddDbContext<ErpDbContext>(options => options.UseNpgsql(connectionString));
        services.AddHttpContextAccessor();
        services.AddScoped<IPasswordHasher<ApplicationUser>, Argon2IdPasswordHasher>();
        services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
            {
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.User.RequireUniqueEmail = false;
            })
            .AddEntityFrameworkStores<ErpDbContext>()
            .AddDefaultTokenProviders();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = environment.IsDevelopment() ? "Erp.Session.Dev" : "__Host-Erp.Session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Path = "/";
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<Seed.DevelopmentSeeder>();
        return services;
    }
}
