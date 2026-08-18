using System.Data;
using Erp.Application.Cashier;
using Erp.Application.Common;
using Erp.Domain.Cashier;
using Erp.Domain.Common;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Erp.Infrastructure.Cashier;

internal sealed class PaymentChannelConfigurationService(ErpDbContext db, IConfiguration configuration,
    IHttpContextAccessor httpContextAccessor) : IPaymentChannelConfigurationService
{
    public async Task<IReadOnlyList<PaymentChannelConfigurationDto>> ListAsync(Guid tenantId, Guid storeId,
        CancellationToken cancellationToken)
    {
        var items = await db.PaymentChannelConfigurations.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.StoreId == storeId)
            .OrderBy(x => x.Provider).ToListAsync(cancellationToken);
        return items.Select(Map).ToList();
    }

    public async Task<Result<PaymentChannelConfigurationDto>> ConfigureAsync(Guid tenantId,
        ConfigurePaymentChannelCommand command, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            if (!await db.Stores.AnyAsync(x => x.Id == command.StoreId && x.TenantId == tenantId,
                    cancellationToken))
                return await Fail(transaction, "STORE_NOT_FOUND", "门店不存在或当前账号无权访问",
                    cancellationToken);

            var existing = await db.PaymentChannelConfigurations.SingleOrDefaultAsync(x =>
                x.TenantId == tenantId && x.StoreId == command.StoreId && x.Provider == command.Provider,
                cancellationToken);
            if (existing is null && command.ExpectedVersion != 0)
                return await Fail(transaction, "VERSION_CONFLICT", "渠道配置已经变化，请刷新后重试",
                    cancellationToken);
            if (existing is not null && existing.Version != command.ExpectedVersion)
                return await Fail(transaction, "VERSION_CONFLICT", "渠道配置已经变化，请刷新后重试",
                    cancellationToken);

            PaymentChannelConfiguration item;
            var previous = existing is null ? null : $"{existing.Environment}:{existing.IsEnabled}";
            if (existing is null)
            {
                item = new PaymentChannelConfiguration(tenantId, command.StoreId, command.Provider,
                    command.Environment, command.DisplayName, command.CredentialProfile, command.IsEnabled);
                db.PaymentChannelConfigurations.Add(item);
            }
            else
            {
                item = existing;
                item.Reconfigure(command.Environment, command.DisplayName, command.CredentialProfile,
                    command.IsEnabled);
            }

            var readiness = Inspect(command.Provider, item.CredentialProfile);
            if (command.IsEnabled && !readiness.IsPresent)
                return await Fail(transaction, "PAYMENT_CHANNEL_CREDENTIALS_INCOMPLETE",
                    $"渠道凭据不完整：{string.Join('、', readiness.Missing)}", cancellationToken);

            db.AuditEvents.Add(new AuditEventRecord
            {
                TenantId = tenantId,
                StoreId = command.StoreId,
                OperatorId = command.OperatorId,
                Action = "payment_channel.configuration.configure",
                EntityType = "PaymentChannelConfiguration",
                EntityId = item.Id,
                PreviousState = previous,
                CurrentState = $"{item.Environment}:{item.IsEnabled}",
                TraceId = httpContextAccessor.HttpContext?.TraceIdentifier ?? "background",
                OccurredAtUtc = DateTimeOffset.UtcNow,
                Metadata = $$"""{"provider":"{{item.Provider}}","credentialProfile":"{{item.CredentialProfile}}"}""",
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(Map(item));
        }
        catch (DomainRuleException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ResultFactory.Failure<PaymentChannelConfigurationDto>(exception.Code, exception.Message);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ResultFactory.Failure<PaymentChannelConfigurationDto>("VERSION_CONFLICT",
                "渠道配置已经变化，请刷新后重试");
        }
        catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.SerializationFailure
            or PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ResultFactory.Failure<PaymentChannelConfigurationDto>("VERSION_CONFLICT",
                "渠道配置已经变化，请刷新后重试");
        }
    }

    private PaymentChannelConfigurationDto Map(PaymentChannelConfiguration item)
    {
        var readiness = Inspect(item.Provider, item.CredentialProfile);
        return new PaymentChannelConfigurationDto(item.Id, item.StoreId, item.Provider.ToString(),
            item.Environment.ToString(), item.DisplayName, item.CredentialProfile, item.IsEnabled,
            readiness.IsPresent, readiness.Missing, item.Version);
    }

    private CredentialReadiness Inspect(PaymentChannelProvider provider, string profile)
    {
        var section = configuration.GetSection($"PaymentChannels:Profiles:{profile}");
        var missing = new List<string>();
        var requiredValues = provider == PaymentChannelProvider.WeChatPay
            ? new[] { "AppId", "MerchantId", "MerchantCertificateSerial", "ApiV3Key" }
            : new[] { "AppId" };
        foreach (var key in requiredValues)
            if (string.IsNullOrWhiteSpace(section[key])) missing.Add(key);

        if (provider == PaymentChannelProvider.WeChatPay && section["ApiV3Key"] is { } apiV3Key &&
            !string.IsNullOrWhiteSpace(apiV3Key) && apiV3Key.Length != 32)
            missing.Add("ApiV3Key(必须32字符)");

        var requiredFiles = provider == PaymentChannelProvider.WeChatPay
            ? new[] { "MerchantPrivateKeyPath", "PlatformPublicKeyPath" }
            : new[] { "MerchantPrivateKeyPath", "AlipayPublicKeyPath" };
        foreach (var key in requiredFiles)
        {
            var path = section[key];
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) missing.Add($"{key}(文件不存在)");
        }

        var requiredUrls = provider == PaymentChannelProvider.WeChatPay
            ? new[] { "NotifyUrl" }
            : new[] { "NotifyUrl", "GatewayUrl" };
        foreach (var key in requiredUrls)
        {
            var value = section[key];
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                missing.Add($"{key}(必须为HTTPS)");
        }
        return new CredentialReadiness(missing.Count == 0, missing.Distinct().ToList());
    }

    private static async Task<Result<PaymentChannelConfigurationDto>> Fail(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, string code, string message,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        return ResultFactory.Failure<PaymentChannelConfigurationDto>(code, message);
    }

    private sealed record CredentialReadiness(bool IsPresent, IReadOnlyList<string> Missing);
}
