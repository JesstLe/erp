using System.Text.RegularExpressions;
using Erp.Infrastructure.Identity;
using Erp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Erp.Infrastructure.Seed;

public sealed partial class PlatformAdminBootstrapper(
    ErpDbContext db,
    IConfiguration configuration,
    TimeProvider timeProvider)
{
    public const string RequiredConfirmation = "CREATE_PLATFORM_ADMIN";

    public async Task<PlatformAdminBootstrapResult> BootstrapAsync(CancellationToken cancellationToken)
    {
        if (!string.Equals(configuration["ERP_PLATFORM_BOOTSTRAP_CONFIRM"], RequiredConfirmation,
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"平台管理员初始化需要设置 ERP_PLATFORM_BOOTSTRAP_CONFIRM={RequiredConfirmation}");
        if (await db.PlatformAdminUsers.AnyAsync(cancellationToken))
            throw new InvalidOperationException("平台管理员已经存在，拒绝重复初始化");
        var account = Required("ERP_PLATFORM_ADMIN_ACCOUNT", 100);
        var displayName = Required("ERP_PLATFORM_ADMIN_DISPLAY_NAME", 100);
        var password = Required("ERP_PLATFORM_ADMIN_PASSWORD", 256);
        if (!AccountPattern().IsMatch(account)) throw new InvalidOperationException("平台管理员账号格式不正确");
        if (!Platform.PlatformIdentityService.ValidPassword(password))
            throw new InvalidOperationException(PasswordPolicy.RequirementText);
        var now = timeProvider.GetUtcNow();
        var user = new PlatformAdminUserRecord
        {
            Account = account,
            NormalizedAccount = account.ToUpperInvariant(),
            DisplayName = displayName,
            PasswordHash = Argon2IdPasswordCodec.Hash(password),
            IsEnabled = true,
            MustChangePassword = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.PlatformAdminUsers.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        return new PlatformAdminBootstrapResult(user.Id, user.Account, user.DisplayName);
    }

    private string Required(string key, int maximum)
    {
        var value = configuration[key]?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum)
            throw new InvalidOperationException($"平台管理员初始化缺少或无效的环境变量 {key}");
        return value;
    }

    [GeneratedRegex("^[A-Za-z0-9._@-]{4,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex AccountPattern();
}

public sealed record PlatformAdminBootstrapResult(Guid Id, string Account, string DisplayName);
