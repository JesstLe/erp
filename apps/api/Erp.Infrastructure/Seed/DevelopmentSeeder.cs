using Erp.Application.Security;
using Erp.Domain.Authorization;
using Erp.Domain.Facilities;
using Erp.Domain.Customers;
using Erp.Domain.Cashier;
using Erp.Domain.Organization;
using Erp.Infrastructure.Identity;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Erp.Infrastructure.Seed;

public sealed class DevelopmentSeeder(ErpDbContext dbContext, UserManager<ApplicationUser> userManager,
    IConfiguration configuration)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var password = configuration["ERP_SEED_OWNER_PASSWORD"];
        if (string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var tenant = await dbContext.Tenants.SingleOrDefaultAsync(x => x.Code == "B01", cancellationToken);
        if (tenant is null)
        {
            tenant = new Tenant("B01", "演示品牌");
            dbContext.Tenants.Add(tenant);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var store = await dbContext.Stores.SingleOrDefaultAsync(x => x.TenantId == tenant.Id && x.Code == "S01", cancellationToken);
        if (store is null)
        {
            store = new Store(tenant.Id, "S01", "演示门店");
            dbContext.Stores.Add(store);
        }

        if (dbContext.ChangeTracker.HasChanges())
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var roleNames = new[] { SystemRoles.Owner, SystemRoles.StoreManager, SystemRoles.FrontDesk, SystemRoles.Cashier, SystemRoles.Technician };
        var tenantRoles = await dbContext.Roles.Where(role => role.TenantId == tenant.Id && role.Name != null)
            .ToListAsync(cancellationToken);
        foreach (var roleName in roleNames)
        {
            if (!tenantRoles.Any(role => string.Equals(role.Name, roleName, StringComparison.OrdinalIgnoreCase)))
            {
                var role = new ApplicationRole
                {
                    Id = Guid.CreateVersion7(), TenantId = tenant.Id, Name = roleName,
                    NormalizedName = roleName.ToUpperInvariant(),
                };
                dbContext.Roles.Add(role);
                tenantRoles.Add(role);
            }
        }
        await dbContext.SaveChangesAsync(cancellationToken);

        var defaultPositions = new[]
        {
            new { Code = "OWNER", Name = "负责人", SortOrder = 10 },
            new { Code = "STORE_MANAGER", Name = "门店负责人", SortOrder = 20 },
            new { Code = "STAFF", Name = "员工", SortOrder = 30 },
            new { Code = "OTHER", Name = "其他岗位", SortOrder = 999 },
        };
        var existingPositionCodes = await dbContext.EmployeePositions.Where(x => x.TenantId == tenant.Id)
            .Select(x => x.Code).ToListAsync(cancellationToken);
        foreach (var position in defaultPositions.Where(position => !existingPositionCodes.Contains(position.Code)))
            dbContext.EmployeePositions.Add(new EmployeePosition(tenant.Id, position.Code, position.Name,
                position.SortOrder));
        await dbContext.SaveChangesAsync(cancellationToken);

        var owner = await userManager.FindByNameAsync("owner01");
        if (owner is null)
        {
            owner = new ApplicationUser { Id = Guid.CreateVersion7(), TenantId = tenant.Id, UserName = "owner01", DisplayName = "系统负责人", IsEnabled = true, MustChangePassword = false };
            EnsureSucceeded(await userManager.CreateAsync(owner, password), "创建开发种子账号");
        }
        var ownerRole = tenantRoles.Single(role => string.Equals(role.Name, SystemRoles.Owner,
            StringComparison.OrdinalIgnoreCase));
        if (!await dbContext.UserRoles.AnyAsync(link => link.UserId == owner.Id && link.RoleId == ownerRole.Id,
                cancellationToken))
        {
            dbContext.UserRoles.Add(new IdentityUserRole<Guid> { UserId = owner.Id, RoleId = ownerRole.Id });
        }

        if (!await dbContext.UserStores.AnyAsync(x => x.UserId == owner.Id && x.StoreId == store.Id, cancellationToken))
        {
            dbContext.UserStores.Add(new UserStore(tenant.Id, owner.Id, store.Id, true));
        }

        var ownerEmployee = await dbContext.Employees.SingleOrDefaultAsync(x => x.TenantId == tenant.Id && x.UserId == owner.Id, cancellationToken);
        if (ownerEmployee is null)
        {
            ownerEmployee = new Employee(tenant.Id, "E0001", owner.DisplayName, "OWNER", owner.Id);
            dbContext.Employees.Add(ownerEmployee);
            dbContext.EmployeeStores.Add(new EmployeeStore(tenant.Id, ownerEmployee.Id, store.Id, true));
        }

        foreach (var role in tenantRoles.Where(role => role.Name is not null))
        {
            foreach (var action in SystemPermissions.ForRole(role.Name!))
            {
                if (!await dbContext.RoleActionGrants.AnyAsync(x => x.RoleId == role.Id && x.Action == action,
                        cancellationToken))
                    dbContext.RoleActionGrants.Add(new RoleActionGrant(tenant.Id, role.Id, action));
            }
        }

        if (!await dbContext.PriceOverridePolicies.AnyAsync(x => x.TenantId == tenant.Id && x.IsActive,
                cancellationToken))
        {
            dbContext.PriceOverridePolicies.Add(PriceOverridePolicy.Default(tenant.Id, owner.Id,
                DateTimeOffset.UtcNow));
        }

        var facilityGroup = await dbContext.FacilityGroups.SingleOrDefaultAsync(x => x.StoreId == store.Id && x.DisplayName == "服务区 A", cancellationToken);
        if (facilityGroup is null)
        {
            facilityGroup = new FacilityGroup(tenant.Id, store.Id, "服务区 A", 10);
            dbContext.FacilityGroups.Add(facilityGroup);
        }

        var facilityType = await dbContext.FacilityTypes.SingleOrDefaultAsync(x => x.TenantId == tenant.Id && x.DisplayName == "通用服务位", cancellationToken);
        if (facilityType is null)
        {
            facilityType = new FacilityType(tenant.Id, "通用服务位");
            dbContext.FacilityTypes.Add(facilityType);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        if (!await dbContext.Facilities.AnyAsync(x => x.StoreId == store.Id, cancellationToken))
        {
            dbContext.Facilities.AddRange(
                new Facility(tenant.Id, store.Id, facilityGroup.Id, facilityType.Id, "F01", "服务位 01", 10, 0, false),
                new Facility(tenant.Id, store.Id, facilityGroup.Id, facilityType.Id, "F02", "服务位 02", 20, 5, false));
        }

        if (!await dbContext.MemberCardTypes.AnyAsync(x => x.TenantId == tenant.Id, cancellationToken))
        {
            dbContext.MemberCardTypes.Add(new MemberCardType(tenant.Id, "STANDARD", "标准会员", null));
        }

        if (!await dbContext.PaymentMethods.AnyAsync(x => x.TenantId == tenant.Id, cancellationToken))
        {
            dbContext.PaymentMethods.AddRange(
                new PaymentMethod(tenant.Id, "CASH", "现金", PaymentMethodCategory.Cash, true),
                new PaymentMethod(tenant.Id, "WECHAT_MANUAL", "微信人工登记", PaymentMethodCategory.ManualExternal, true),
                new PaymentMethod(tenant.Id, "ALIPAY_MANUAL", "支付宝人工登记", PaymentMethodCategory.ManualExternal, true));
        }

        if (!await dbContext.PaymentMethods.AnyAsync(x => x.TenantId == tenant.Id &&
            x.Code == "MEMBER_PRINCIPAL", cancellationToken))
        {
            dbContext.PaymentMethods.Add(new PaymentMethod(tenant.Id, "MEMBER_PRINCIPAL", "会员储值本金",
                PaymentMethodCategory.InternalAccount, false, MemberAccountType.Principal));
        }
        if (!await dbContext.PaymentMethods.AnyAsync(x => x.TenantId == tenant.Id &&
            x.Code == "MEMBER_BONUS", cancellationToken))
        {
            dbContext.PaymentMethods.Add(new PaymentMethod(tenant.Id, "MEMBER_BONUS", "会员奖励金",
                PaymentMethodCategory.InternalAccount, false, MemberAccountType.Bonus));
        }
        if (!await dbContext.PaymentMethods.AnyAsync(x => x.TenantId == tenant.Id &&
            x.Code == "WECHAT_NATIVE", cancellationToken))
        {
            var method = new PaymentMethod(tenant.Id, "WECHAT_NATIVE", "微信支付 Native",
                PaymentMethodCategory.ChannelExternal, true, channelProvider: PaymentChannelProvider.WeChatPay);
            method.SetEnabled(false);
            dbContext.PaymentMethods.Add(method);
        }
        if (!await dbContext.PaymentMethods.AnyAsync(x => x.TenantId == tenant.Id &&
            x.Code == "ALIPAY_QR", cancellationToken))
        {
            var method = new PaymentMethod(tenant.Id, "ALIPAY_QR", "支付宝订单码",
                PaymentMethodCategory.ChannelExternal, true, channelProvider: PaymentChannelProvider.Alipay);
            method.SetEnabled(false);
            dbContext.PaymentMethods.Add(method);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"{operation}失败：{string.Join(';', result.Errors.Select(x => x.Code))}");
        }
    }
}
