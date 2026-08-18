using Erp.Application.Catalog;
using Erp.Application.Identity;
using Erp.Application.Facilities;
using Erp.Application.Customers;
using Erp.Application.Cashier;
using Erp.Application.Auditing;
using Erp.Application.Reports;
using Erp.Application.Inventory;
using Erp.Application.Notifications;
using Erp.Application.Common;
using Erp.Infrastructure.Catalog;
using Erp.Infrastructure.Customers;
using Erp.Infrastructure.Cashier;
using Erp.Infrastructure.Auditing;
using Erp.Infrastructure.Reports;
using Erp.Infrastructure.Inventory;
using Erp.Infrastructure.Files;
using Erp.Infrastructure.Facilities;
using Erp.Infrastructure.Identity;
using Erp.Infrastructure.Notifications;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
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
        var customerLookupPepper = configuration["CustomerPrivacy:LookupPepper"];
        var memberVerificationPepper = configuration["MemberVerification:CodePepper"];
        var fileStorageRoot = configuration["FileStorage:RootPath"];
        var dataProtectionKeyRingPath = configuration["DataProtection:KeyRingPath"];
        if (!environment.IsDevelopment() && (string.IsNullOrWhiteSpace(customerLookupPepper) || customerLookupPepper.Length < 32 ||
            customerLookupPepper.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("生产环境必须配置至少32字符的 CustomerPrivacy:LookupPepper，且不能使用模板占位值");
        if (!environment.IsDevelopment() && (string.IsNullOrWhiteSpace(memberVerificationPepper) ||
            memberVerificationPepper.Length < 32 ||
            memberVerificationPepper.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("生产环境必须配置至少32字符的 MemberVerification:CodePepper，且不能使用模板占位值");
        if (!environment.IsDevelopment() && string.IsNullOrWhiteSpace(fileStorageRoot))
            throw new InvalidOperationException("生产环境必须配置独立持久化目录 FileStorage:RootPath");
        if (!environment.IsDevelopment() && string.IsNullOrWhiteSpace(dataProtectionKeyRingPath))
            throw new InvalidOperationException("生产环境必须配置持久化 DataProtection:KeyRingPath");

        services.AddDbContext<ErpDbContext>(options => options.UseNpgsql(connectionString));
        var dataProtection = services.AddDataProtection().SetApplicationName("Erp");
        if (!string.IsNullOrWhiteSpace(dataProtectionKeyRingPath))
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(Path.GetFullPath(dataProtectionKeyRingPath)));
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
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddSingleton<SecureFileStorage>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IFacilityService, FacilityService>();
        services.AddScoped<CustomerPrivacyService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IDatabaseReadinessService, DatabaseReadinessService>();
        services.AddScoped<IServiceRecordService, ServiceRecordService>();
        services.AddScoped<IMemberTopupService, MemberTopupService>();
        services.AddScoped<MemberVerificationCodeService>();
        services.AddScoped<IMemberVerificationService, MemberVerificationService>();
        services.AddScoped<ICashierService, CashierService>();
        services.AddScoped<InventoryPostingService>();
        services.AddScoped<IInventoryService>(provider => provider.GetRequiredService<InventoryPostingService>());
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddSingleton<PaymentChannelCredentialResolver>();
        services.AddHttpClient<WechatPayGateway>(client =>
        {
            client.BaseAddress = new Uri("https://api.mch.weixin.qq.com");
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddHttpClient<AlipayGateway>(client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddScoped<IPaymentChannelGateway>(provider => provider.GetRequiredService<WechatPayGateway>());
        services.AddScoped<IPaymentChannelGateway>(provider => provider.GetRequiredService<AlipayGateway>());
        services.AddScoped<PaymentChannelGatewayRegistry>();
        services.AddScoped<IPaymentChannelConfigurationService, PaymentChannelConfigurationService>();
        services.AddScoped<IPaymentChannelPaymentService, PaymentChannelPaymentService>();
        services.AddScoped<IPaymentChannelReconciliationService, PaymentChannelReconciliationService>();
        services.AddScoped<IRefundService, RefundService>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<Seed.DevelopmentSeeder>();
        return services;
    }
}
