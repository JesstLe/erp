using Erp.Application.Security;
using Erp.Domain.Authorization;
using Erp.Domain.Organization;
using Erp.Infrastructure.Identity;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Erp.Infrastructure.Seed;

public sealed class DevelopmentSeeder(ErpDbContext dbContext, UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager, IConfiguration configuration)
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
        foreach (var roleName in roleNames)
        {
            if (await roleManager.FindByNameAsync(roleName) is null)
            {
                EnsureSucceeded(await roleManager.CreateAsync(new ApplicationRole { Id = Guid.CreateVersion7(), TenantId = tenant.Id, Name = roleName }), $"创建角色 {roleName}");
            }
        }

        var owner = await userManager.FindByNameAsync("owner01");
        if (owner is null)
        {
            owner = new ApplicationUser { Id = Guid.CreateVersion7(), TenantId = tenant.Id, UserName = "owner01", DisplayName = "系统负责人", IsEnabled = true };
            EnsureSucceeded(await userManager.CreateAsync(owner, password), "创建开发种子账号");
            EnsureSucceeded(await userManager.AddToRoleAsync(owner, SystemRoles.Owner), "分配开发种子角色");
        }

        if (!await dbContext.UserStores.AnyAsync(x => x.UserId == owner.Id && x.StoreId == store.Id, cancellationToken))
        {
            dbContext.UserStores.Add(new UserStore(tenant.Id, owner.Id, store.Id, true));
        }

        var ownerRole = await roleManager.FindByNameAsync(SystemRoles.Owner) ?? throw new InvalidOperationException("OWNER角色不存在");
        var ownerActions = new[] { SystemActions.CatalogRead, SystemActions.CatalogWrite, SystemActions.PricePublish, SystemActions.FacilityOperate, SystemActions.CashierCheckout, SystemActions.AuditRead };
        foreach (var action in ownerActions)
        {
            if (!await dbContext.RoleActionGrants.AnyAsync(x => x.RoleId == ownerRole.Id && x.Action == action, cancellationToken))
            {
                dbContext.RoleActionGrants.Add(new RoleActionGrant(tenant.Id, ownerRole.Id, action));
            }
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
