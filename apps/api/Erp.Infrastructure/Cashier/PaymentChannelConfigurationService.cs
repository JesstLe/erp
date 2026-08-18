using System.Data;
using Erp.Application.Cashier;
using Erp.Application.Common;
using Erp.Domain.Cashier;
using Erp.Domain.Common;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Erp.Infrastructure.Cashier;

internal sealed class PaymentChannelConfigurationService(ErpDbContext db, PaymentChannelCredentialResolver credentialResolver,
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

            var readiness = credentialResolver.Inspect(command.Provider, item.CredentialProfile);
            if (command.IsEnabled && !readiness.IsPresent)
                return await Fail(transaction, "PAYMENT_CHANNEL_CREDENTIALS_INCOMPLETE",
                    $"渠道凭据不完整：{string.Join('、', readiness.Missing)}", cancellationToken);
            if (command.IsEnabled && credentialResolver.TryResolve(command.Provider, item.CredentialProfile,
                    out var resolved, out _) && resolved is not null &&
                !PaymentChannelCredentialResolver.IsEnvironmentCompatible(item.Environment, resolved,
                    out var environmentMessage))
                return await Fail(transaction, "PAYMENT_CHANNEL_ENVIRONMENT_MISMATCH", environmentMessage,
                    cancellationToken);

            var methodCode = command.Provider == PaymentChannelProvider.WeChatPay
                ? "WECHAT_NATIVE" : "ALIPAY_QR";
            var paymentMethod = await db.PaymentMethods.SingleOrDefaultAsync(x => x.TenantId == tenantId &&
                x.Code == methodCode, cancellationToken);
            if (paymentMethod is null)
                return await Fail(transaction, "PAYMENT_METHOD_NOT_FOUND",
                    "渠道支付方式尚未完成数据库初始化", cancellationToken);
            var enabledInAnotherStore = !command.IsEnabled && await db.PaymentChannelConfigurations
                .AnyAsync(x => x.TenantId == tenantId && x.Provider == command.Provider &&
                    x.StoreId != command.StoreId && x.IsEnabled, cancellationToken);
            paymentMethod.SetEnabled(command.IsEnabled || enabledInAnotherStore);

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
        var readiness = credentialResolver.Inspect(item.Provider, item.CredentialProfile);
        return new PaymentChannelConfigurationDto(item.Id, item.StoreId, item.Provider.ToString(),
            item.Environment.ToString(), item.DisplayName, item.CredentialProfile, item.IsEnabled,
            readiness.IsPresent, readiness.Missing, item.Version);
    }

    private static async Task<Result<PaymentChannelConfigurationDto>> Fail(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, string code, string message,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        return ResultFactory.Failure<PaymentChannelConfigurationDto>(code, message);
    }

}
