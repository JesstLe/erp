using Erp.Application.Common;
using Erp.Domain.Cashier;

namespace Erp.Application.Cashier;

public sealed record PaymentChannelReconciliationItemDto(Guid Id, string ItemType, string Status,
    string MatchKey, string? OutTradeNo, string? OutRefundNo, string? ProviderTradeNo,
    Guid? PaymentAllocationId, Guid? ChannelRefundId, long? LocalAmountMinor,
    long? ChannelAmountMinor, long ChannelFeeMinor, string? LocalStatus, string? ChannelStatus,
    Guid? ResolvedBy, DateTimeOffset? ResolvedAtUtc, string? ResolutionReason, uint Version);

public sealed record PaymentChannelReconciliationRunDto(Guid Id, Guid ConfigurationId, string Provider,
    DateOnly BusinessDate, int AttemptNo, string Status, int ChannelEntryCount, int MatchedCount,
    int DifferenceCount, string? SourceSha256, string? FailureCode, Guid StartedBy,
    DateTimeOffset StartedAtUtc, DateTimeOffset? CompletedAtUtc, uint Version,
    IReadOnlyList<PaymentChannelReconciliationItemDto> Items);

public sealed record StartPaymentChannelReconciliationCommand(Guid StoreId,
    PaymentChannelProvider Provider, DateOnly BusinessDate, Guid OperatorId);
public sealed record ResolvePaymentChannelReconciliationItemCommand(Guid StoreId, Guid ItemId,
    uint ExpectedVersion, string Reason, Guid OperatorId);

public interface IPaymentChannelReconciliationService
{
    Task<IReadOnlyList<PaymentChannelReconciliationRunDto>> ListAsync(Guid tenantId, Guid storeId,
        DateOnly? fromDate, DateOnly? toDate, CancellationToken cancellationToken);
    Task<Result<PaymentChannelReconciliationRunDto>> StartAsync(Guid tenantId,
        StartPaymentChannelReconciliationCommand command, CancellationToken cancellationToken);
    Task<Result<PaymentChannelReconciliationItemDto>> ResolveAsync(Guid tenantId,
        ResolvePaymentChannelReconciliationItemCommand command, CancellationToken cancellationToken);
}
