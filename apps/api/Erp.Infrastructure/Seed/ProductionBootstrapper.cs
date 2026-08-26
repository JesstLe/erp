using System.Text.RegularExpressions;
using Erp.Application.Security;
using Erp.Domain.Authorization;
using Erp.Domain.Cashier;
using Erp.Domain.Customers;
using Erp.Domain.Organization;
using Erp.Infrastructure.Identity;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Erp.Infrastructure.Seed;

public sealed record ProductionBootstrapResult(Guid TenantId, Guid StoreId, Guid OwnerUserId,
    string TenantCode, string StoreCode, string OwnerAccount);

/// <summary>
/// Creates the minimum production master data on a migrated, otherwise empty database.
/// This service is intentionally reachable only from the API process command line.
/// </summary>
public sealed partial class ProductionBootstrapper(ErpDbContext db, UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager, IConfiguration configuration)
{
    public const string RequiredConfirmation = "CREATE_NEW_ERP";

    public async Task<ProductionBootstrapResult> BootstrapAsync(CancellationToken cancellationToken)
    {
        var options = ReadAndValidateOptions();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await db.Tenants.AnyAsync(cancellationToken) || await db.Stores.AnyAsync(cancellationToken) ||
            await db.Users.AnyAsync(cancellationToken) || await db.Roles.AnyAsync(cancellationToken))
        {
            throw new InvalidOperationException("正式初始化已拒绝：数据库不是空库。该命令只能执行一次。");
        }

        try
        {
            var tenant = new Tenant(options.TenantCode, options.TenantName);
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(cancellationToken);

            var store = new Store(tenant.Id, options.StoreCode, options.StoreName, options.StoreTimeZone);
            db.Stores.Add(store);
            await db.SaveChangesAsync(cancellationToken);

            var roles = new Dictionary<string, ApplicationRole>(StringComparer.OrdinalIgnoreCase);
            foreach (var roleName in AllRoleNames)
            {
                var role = new ApplicationRole
                {
                    Id = Guid.CreateVersion7(), TenantId = tenant.Id, Name = roleName,
                };
                EnsureSucceeded(await roleManager.CreateAsync(role), $"创建角色 {roleName}");
                roles.Add(roleName, role);
            }

            var owner = new ApplicationUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant.Id,
                UserName = options.OwnerAccount,
                DisplayName = options.OwnerDisplayName,
                IsEnabled = true,
                MustChangePassword = true,
            };
            EnsureSucceeded(await userManager.CreateAsync(owner, options.OwnerPassword), "创建最高权限账号");
            EnsureSucceeded(await userManager.AddToRoleAsync(owner, SystemRoles.Owner), "分配最高权限角色");

            db.UserStores.Add(new UserStore(tenant.Id, owner.Id, store.Id, true));
            db.EmployeePositions.AddRange(
                new EmployeePosition(tenant.Id, "OWNER", options.OwnerPosition, 10),
                new EmployeePosition(tenant.Id, "STORE_MANAGER", "门店负责人", 20),
                new EmployeePosition(tenant.Id, "STAFF", "员工", 30),
                new EmployeePosition(tenant.Id, "OTHER", "其他岗位", 999));
            var employee = new Employee(tenant.Id, options.OwnerEmployeeNo, options.OwnerDisplayName,
                "OWNER", owner.Id);
            db.Employees.Add(employee);
            db.EmployeeStores.Add(new EmployeeStore(tenant.Id, employee.Id, store.Id, true));

            foreach (var (roleName, role) in roles)
            foreach (var action in SystemPermissions.ForRole(roleName))
                db.RoleActionGrants.Add(new RoleActionGrant(tenant.Id, role.Id, action));

            db.PriceOverridePolicies.Add(PriceOverridePolicy.Default(tenant.Id, owner.Id, DateTimeOffset.UtcNow));
            db.MemberCardTypes.Add(new MemberCardType(tenant.Id, "STANDARD", "标准会员", null));
            AddDefaultPaymentMethods(tenant.Id);

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new ProductionBootstrapResult(tenant.Id, store.Id, owner.Id, tenant.Code, store.Code,
                owner.UserName!);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private BootstrapOptions ReadAndValidateOptions()
    {
        if (!string.Equals(configuration["ERP_BOOTSTRAP_CONFIRM"], RequiredConfirmation,
                StringComparison.Ordinal))
            throw new InvalidOperationException($"正式初始化需要设置 ERP_BOOTSTRAP_CONFIRM={RequiredConfirmation}");

        var options = new BootstrapOptions(
            Required("ERP_BOOTSTRAP_TENANT_CODE", 32).ToUpperInvariant(),
            Required("ERP_BOOTSTRAP_TENANT_NAME", 100),
            Required("ERP_BOOTSTRAP_STORE_CODE", 32).ToUpperInvariant(),
            Required("ERP_BOOTSTRAP_STORE_NAME", 100),
            Optional("ERP_BOOTSTRAP_STORE_TIME_ZONE", "Asia/Shanghai", 64),
            Required("ERP_BOOTSTRAP_OWNER_ACCOUNT", 100),
            Required("ERP_BOOTSTRAP_OWNER_DISPLAY_NAME", 100),
            Required("ERP_BOOTSTRAP_OWNER_EMPLOYEE_NO", 32).ToUpperInvariant(),
            Optional("ERP_BOOTSTRAP_OWNER_POSITION", "负责人", 40),
            Required("ERP_BOOTSTRAP_OWNER_PASSWORD", 512));

        if (!CodePattern().IsMatch(options.TenantCode) || !CodePattern().IsMatch(options.StoreCode))
            throw new InvalidOperationException("品牌编号和门店编号只能包含大写字母、数字、下划线或连字符，长度为2到32位");
        if (!EmployeeNoPattern().IsMatch(options.OwnerEmployeeNo))
            throw new InvalidOperationException("负责人工号只能包含大写字母、数字、下划线或连字符，长度为2到32位");
        if (!AccountPattern().IsMatch(options.OwnerAccount))
            throw new InvalidOperationException("负责人账号格式不正确，长度为4到100位");
        if (options.OwnerDisplayName.Length < 2 || options.OwnerPosition.Length < 2)
            throw new InvalidOperationException("负责人姓名和岗位至少需要2个字符");
        return options;
    }

    private string Required(string key, int maxLength)
    {
        var value = configuration[key]?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
            throw new InvalidOperationException($"正式初始化缺少或无效的环境变量 {key}");
        return value;
    }

    private string Optional(string key, string fallback, int maxLength)
    {
        var value = configuration[key]?.Trim();
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        if (value.Length > maxLength)
            throw new InvalidOperationException($"正式初始化环境变量 {key} 长度超出限制");
        return value;
    }

    private void AddDefaultPaymentMethods(Guid tenantId)
    {
        db.PaymentMethods.AddRange(
            new PaymentMethod(tenantId, "CASH", "现金", PaymentMethodCategory.Cash, true),
            new PaymentMethod(tenantId, "WECHAT_MANUAL", "微信人工登记", PaymentMethodCategory.ManualExternal, true),
            new PaymentMethod(tenantId, "ALIPAY_MANUAL", "支付宝人工登记", PaymentMethodCategory.ManualExternal, true),
            new PaymentMethod(tenantId, "MEMBER_PRINCIPAL", "会员储值本金",
                PaymentMethodCategory.InternalAccount, false, MemberAccountType.Principal),
            new PaymentMethod(tenantId, "MEMBER_BONUS", "会员奖励金",
                PaymentMethodCategory.InternalAccount, false, MemberAccountType.Bonus));

        var wechat = new PaymentMethod(tenantId, "WECHAT_NATIVE", "微信支付 Native",
            PaymentMethodCategory.ChannelExternal, true, channelProvider: PaymentChannelProvider.WeChatPay);
        wechat.SetEnabled(false);
        var alipay = new PaymentMethod(tenantId, "ALIPAY_QR", "支付宝订单码",
            PaymentMethodCategory.ChannelExternal, true, channelProvider: PaymentChannelProvider.Alipay);
        alipay.SetEnabled(false);
        db.PaymentMethods.AddRange(wechat, alipay);
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"{operation}失败：{string.Join(';', result.Errors.Select(x => x.Code))}");
    }

    private static readonly string[] AllRoleNames =
    [
        SystemRoles.Owner, SystemRoles.StoreManager, SystemRoles.FrontDesk, SystemRoles.Cashier,
        SystemRoles.Technician,
    ];

    private sealed record BootstrapOptions(string TenantCode, string TenantName, string StoreCode,
        string StoreName, string StoreTimeZone, string OwnerAccount, string OwnerDisplayName,
        string OwnerEmployeeNo, string OwnerPosition, string OwnerPassword);

    [GeneratedRegex("^[A-Z0-9_-]{2,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    [GeneratedRegex("^[A-Z0-9_-]{2,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex EmployeeNoPattern();

    [GeneratedRegex("^[A-Za-z0-9._@-]{4,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex AccountPattern();
}
