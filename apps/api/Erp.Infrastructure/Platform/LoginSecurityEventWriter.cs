using System.Security.Cryptography;
using System.Text;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Erp.Infrastructure.Platform;

internal sealed class LoginSecurityEventWriter(
    ErpDbContext db,
    IHttpContextAccessor httpContextAccessor,
    IConfiguration configuration,
    IHostEnvironment environment,
    TimeProvider timeProvider)
{
    private readonly byte[] accountHashKey = BuildKey(configuration, environment);

    public byte[] HashAccount(string account) =>
        HMACSHA256.HashData(accountHashKey, Encoding.UTF8.GetBytes(NormalizeAccount(account)));

    public async Task RecordAsync(string scope, string account, string eventType, string resultCode,
        Guid? tenantId = null, Guid? merchantUserId = null, Guid? platformUserId = null,
        CancellationToken cancellationToken = default)
    {
        var context = httpContextAccessor.HttpContext;
        var agent = context?.Request.Headers.UserAgent.ToString() ?? string.Empty;
        agent = new string(agent.Where(character => !char.IsControl(character)).ToArray()).Trim();
        db.LoginSecurityEvents.Add(new LoginSecurityEventRecord
        {
            Scope = scope,
            TenantId = tenantId,
            MerchantUserId = merchantUserId,
            PlatformUserId = platformUserId,
            EventType = eventType,
            ResultCode = resultCode,
            AccountHash = HashAccount(account),
            AccountMask = MaskAccount(account),
            IpAddress = context?.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            UserAgentSummary = agent.Length > 200 ? agent[..200] : agent,
            TraceId = context?.TraceIdentifier ?? string.Empty,
            OccurredAtUtc = timeProvider.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string NormalizeAccount(string value) => value.Trim().ToUpperInvariant();

    private static string MaskAccount(string value)
    {
        var account = value.Trim();
        if (account.Length == 0) return "***";
        if (account.Length <= 4) return $"{account[0]}***";
        return $"{account[..2]}***{account[^2..]}";
    }

    private static byte[] BuildKey(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration["SecurityEvents:AccountHashPepper"];
        if (!environment.IsDevelopment() && (string.IsNullOrWhiteSpace(configured) || configured.Length < 32))
            throw new InvalidOperationException("生产环境必须配置至少32字符的 SecurityEvents:AccountHashPepper");
        return SHA256.HashData(Encoding.UTF8.GetBytes(configured ??
            "erp-development-only-login-security-account-pepper-v1"));
    }
}
