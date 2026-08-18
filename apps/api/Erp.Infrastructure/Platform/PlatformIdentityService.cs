using System.Security.Claims;
using Erp.Application.Common;
using Erp.Application.Platform;
using Erp.Infrastructure.Identity;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Erp.Infrastructure.Platform;

internal sealed class PlatformIdentityService(
    ErpDbContext db,
    IHttpContextAccessor httpContextAccessor,
    LoginSecurityEventWriter securityEvents,
    TimeProvider timeProvider) : IPlatformIdentityService
{
    private const int MaximumFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public async Task<Result<PlatformCurrentUserDto>> LoginAsync(PlatformLoginCommand command,
        CancellationToken cancellationToken)
    {
        var account = command.Account.Trim();
        if (account.Length is 0 or > 100 || command.Password.Length is 0 or > 256)
        {
            await securityEvents.RecordAsync("Platform", account, "LoginFailed", "VALIDATION_FAILED",
                cancellationToken: cancellationToken);
            return ResultFactory.Failure<PlatformCurrentUserDto>("INVALID_CREDENTIALS", "账号或密码不正确");
        }

        var normalized = account.ToUpperInvariant();
        var user = await db.PlatformAdminUsers.SingleOrDefaultAsync(x => x.NormalizedAccount == normalized,
            cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (user is null || !user.IsEnabled)
        {
            await securityEvents.RecordAsync("Platform", account, "LoginFailed", "INVALID_CREDENTIALS",
                platformUserId: user?.Id, cancellationToken: cancellationToken);
            return ResultFactory.Failure<PlatformCurrentUserDto>("INVALID_CREDENTIALS", "账号或密码不正确");
        }
        if (user.LockoutEndUtc > now)
        {
            await securityEvents.RecordAsync("Platform", account, "AccountLocked", "ACCOUNT_LOCKED",
                platformUserId: user.Id, cancellationToken: cancellationToken);
            return ResultFactory.Failure<PlatformCurrentUserDto>("ACCOUNT_LOCKED", "登录失败次数过多，请稍后再试");
        }

        var verification = Argon2IdPasswordCodec.Verify(user.PasswordHash, command.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            user.AccessFailedCount++;
            user.UpdatedAtUtc = now;
            user.Version++;
            var locked = user.AccessFailedCount >= MaximumFailedAttempts;
            if (locked)
            {
                user.LockoutEndUtc = now.Add(LockoutDuration);
                user.AccessFailedCount = 0;
            }
            await securityEvents.RecordAsync("Platform", account, locked ? "AccountLocked" : "LoginFailed",
                locked ? "ACCOUNT_LOCKED" : "INVALID_CREDENTIALS", platformUserId: user.Id,
                cancellationToken: cancellationToken);
            return locked
                ? ResultFactory.Failure<PlatformCurrentUserDto>("ACCOUNT_LOCKED", "登录失败次数过多，请稍后再试")
                : ResultFactory.Failure<PlatformCurrentUserDto>("INVALID_CREDENTIALS", "账号或密码不正确");
        }

        user.AccessFailedCount = 0;
        user.LockoutEndUtc = null;
        user.UpdatedAtUtc = now;
        user.Version++;
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = Argon2IdPasswordCodec.Hash(command.Password);
        await securityEvents.RecordAsync("Platform", account, "LoginSucceeded", "SUCCESS",
            platformUserId: user.Id, cancellationToken: cancellationToken);
        await SignInAsync(user, command.RememberMe);
        return ResultFactory.Success(ToDto(user));
    }

    public async Task<PlatformCurrentUserDto?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var context = httpContextAccessor.HttpContext;
        if (context is null) return null;
        var authentication = await context.AuthenticateAsync(PlatformAuthentication.Scheme);
        if (!authentication.Succeeded || authentication.Principal is null) return null;
        var idValue = authentication.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = authentication.Principal.FindFirstValue(PlatformAuthentication.SecurityStampClaim);
        if (!Guid.TryParse(idValue, out var id) || string.IsNullOrWhiteSpace(stamp)) return null;
        var user = await db.PlatformAdminUsers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id,
            cancellationToken);
        return user is not null && user.IsEnabled && string.Equals(user.SecurityStamp, stamp,
            StringComparison.Ordinal) ? ToDto(user) : null;
    }

    public async Task<Result<PlatformCurrentUserDto>> ChangePasswordAsync(
        PlatformChangePasswordCommand command, CancellationToken cancellationToken)
    {
        var current = await GetCurrentAsync(cancellationToken);
        if (current is null)
            return ResultFactory.Failure<PlatformCurrentUserDto>("UNAUTHORIZED", "登录状态已失效");
        var user = await db.PlatformAdminUsers.SingleAsync(x => x.Id == current.Id, cancellationToken);
        if (Argon2IdPasswordCodec.Verify(user.PasswordHash, command.CurrentPassword) ==
            PasswordVerificationResult.Failed)
            return ResultFactory.Failure<PlatformCurrentUserDto>("PASSWORD_CHANGE_FAILED",
                "当前密码不正确，或新密码不符合安全要求");
        if (!ValidPassword(command.NewPassword))
            return ResultFactory.Failure<PlatformCurrentUserDto>("PASSWORD_CHANGE_FAILED",
                "新密码至少12位，并包含大小写字母、数字和特殊字符");

        user.PasswordHash = Argon2IdPasswordCodec.Hash(command.NewPassword);
        user.MustChangePassword = false;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.UpdatedAtUtc = timeProvider.GetUtcNow();
        user.Version++;
        await securityEvents.RecordAsync("Platform", user.Account, "PasswordChanged", "SUCCESS",
            platformUserId: user.Id, cancellationToken: cancellationToken);
        await SignInAsync(user, false);
        return ResultFactory.Success(ToDto(user));
    }

    public async Task LogoutAsync(CancellationToken cancellationToken)
    {
        var current = await GetCurrentAsync(cancellationToken);
        if (current is not null)
            await securityEvents.RecordAsync("Platform", current.Account, "LogoutSucceeded", "SUCCESS",
                platformUserId: current.Id, cancellationToken: cancellationToken);
        var context = httpContextAccessor.HttpContext;
        if (context is not null) await context.SignOutAsync(PlatformAuthentication.Scheme);
    }

    internal static bool ValidPassword(string password) => password.Length is >= 12 and <= 256 &&
        password.Any(char.IsUpper) && password.Any(char.IsLower) && password.Any(char.IsDigit) &&
        password.Any(character => !char.IsLetterOrDigit(character));

    private async Task SignInAsync(PlatformAdminUserRecord user, bool persistent)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Account),
            new Claim(ClaimTypes.Role, PlatformAuthentication.Role),
            new Claim(PlatformAuthentication.SecurityStampClaim, user.SecurityStamp),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, PlatformAuthentication.Scheme));
        var context = httpContextAccessor.HttpContext ?? throw new InvalidOperationException("缺少 HTTP 上下文");
        await context.SignInAsync(PlatformAuthentication.Scheme, principal, new AuthenticationProperties
        {
            IsPersistent = persistent,
            AllowRefresh = true,
            ExpiresUtc = timeProvider.GetUtcNow().AddHours(persistent ? 12 : 4),
        });
    }

    private static PlatformCurrentUserDto ToDto(PlatformAdminUserRecord user) =>
        new(user.Id, user.Account, user.DisplayName, user.MustChangePassword);
}
