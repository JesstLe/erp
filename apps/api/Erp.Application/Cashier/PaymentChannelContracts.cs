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
