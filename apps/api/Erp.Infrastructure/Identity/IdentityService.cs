using Erp.Application.Common;
using Erp.Application.Identity;
using Erp.Domain.Organization;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Erp.Infrastructure.Identity;

public sealed class IdentityService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IHttpContextAccessor httpContextAccessor,
    ErpDbContext dbContext) : IIdentityService
{
    public async Task<Result<CurrentUserDto>> LoginAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        var account = command.Account.Trim();
        if (account.Length is 0 or > 100 || command.Password.Length is 0 or > 256)
        {
            return ResultFactory.Failure<CurrentUserDto>("VALIDATION_FAILED", "账号或密码格式不正确");
        }

        var user = await userManager.FindByNameAsync(account);
        if (user is null || !user.IsEnabled)
        {
            return ResultFactory.Failure<CurrentUserDto>("INVALID_CREDENTIALS", "账号或密码不正确");
        }

        var result = await signInManager.PasswordSignInAsync(user, command.Password, command.RememberMe, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            return ResultFactory.Failure<CurrentUserDto>("ACCOUNT_LOCKED", "登录失败次数过多，请稍后再试");
        }

        if (!result.Succeeded)
        {
            return ResultFactory.Failure<CurrentUserDto>("INVALID_CREDENTIALS", "账号或密码不正确");
        }

        return ResultFactory.Success(await BuildCurrentAsync(user, cancellationToken));
    }

    public Task LogoutAsync(CancellationToken cancellationToken) => signInManager.SignOutAsync();

    public async Task<CurrentUserDto?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var user = await userManager.GetUserAsync(principal);
        return user is null ? null : await BuildCurrentAsync(user, cancellationToken);
    }

    private async Task<CurrentUserDto> BuildCurrentAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var roles = await userManager.GetRolesAsync(user);
        var stores = await (
            from userStore in dbContext.UserStores.AsNoTracking()
            join store in dbContext.Stores.AsNoTracking() on userStore.StoreId equals store.Id
            where userStore.UserId == user.Id && store.Status == StoreStatus.Enabled
            orderby userStore.IsDefault descending, store.Name
            select new AuthorizedStoreDto(store.Id, store.Code, store.Name, userStore.IsDefault))
            .ToListAsync(cancellationToken);

        return new CurrentUserDto(user.Id, user.TenantId, user.DisplayName, user.UserName ?? string.Empty, roles.ToList(), stores);
    }
}
