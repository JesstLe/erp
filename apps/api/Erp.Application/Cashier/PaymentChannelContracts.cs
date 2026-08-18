using Erp.Application.Common;
using Erp.Domain.Cashier;

namespace Erp.Application.Cashier;

public sealed record PaymentChannelConfigurationDto(Guid Id, Guid StoreId, string Provider, string Environment,
    string DisplayName, string CredentialProfile, bool IsEnabled, bool CredentialsPresent,
    IReadOnlyList<string> MissingRequirements, uint Version);

public sealed record ConfigurePaymentChannelCommand(Guid StoreId, PaymentChannelProvider Provider,
    PaymentChannelEnvironment Environment, string DisplayName, string CredentialProfile, bool IsEnabled,
    uint ExpectedVersion, Guid OperatorId);

public interface IPaymentChannelConfigurationService
{
    Task<IReadOnlyList<PaymentChannelConfigurationDto>> ListAsync(Guid tenantId, Guid storeId,
        CancellationToken cancellationToken);

    Task<Result<PaymentChannelConfigurationDto>> ConfigureAsync(Guid tenantId,
        ConfigurePaymentChannelCommand command, CancellationToken cancellationToken);
}

public sealed record PaymentChannelOrderDto(Guid Id, Guid ConfigurationId, Guid PaymentId,
    Guid PaymentAllocationId, string Provider, string OutTradeNo, long AmountMinor, string Status,
    string? QrPayload, string? ProviderTradeNo, string? FailureCode, DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? PaidAtUtc, DateTimeOffset? ClosedAtUtc, DateTimeOffset? LastQueriedAtUtc,
    string PaymentStatus, string ServiceOrderStatus, uint Version);

public sealed record InitiatePaymentChannelCommand(Guid StoreId, Guid OrderId, uint ExpectedOrderVersion,
    Guid MethodId, Guid CommandId, Guid OperatorId);
public sealed record OperatePaymentChannelCommand(Guid StoreId, Guid ChannelOrderId, Guid OperatorId);
public sealed record PaymentChannelNotificationCommand(PaymentChannelProvider Provider, Guid ConfigurationId,
    IReadOnlyDictionary<string, string> Headers, string Body, IReadOnlyDictionary<string, string>? Form);
public sealed record PaymentChannelNotificationResult(bool Acknowledge, string? ErrorCode);

public interface IPaymentChannelPaymentService
{
    Task<Result<PaymentChannelOrderDto>> GetByServiceOrderAsync(Guid tenantId, Guid storeId,
        Guid orderId, CancellationToken cancellationToken);
    Task<Result<PaymentChannelOrderDto>> InitiateAsync(Guid tenantId, InitiatePaymentChannelCommand command,
        CancellationToken cancellationToken);
    Task<Result<PaymentChannelOrderDto>> QueryAsync(Guid tenantId, OperatePaymentChannelCommand command,
        CancellationToken cancellationToken);
    Task<Result<PaymentChannelOrderDto>> CloseAsync(Guid tenantId, OperatePaymentChannelCommand command,
        CancellationToken cancellationToken);
    Task<PaymentChannelNotificationResult> ProcessNotificationAsync(PaymentChannelNotificationCommand command,
        CancellationToken cancellationToken);
}
