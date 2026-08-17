using Erp.Application.Common;

namespace Erp.Application.Cashier;

public sealed record PaymentMethodDto(Guid Id, string Code, string Name, string Category, bool RequiresOpenShift);
public sealed record PaymentAllocationDto(Guid Id, Guid MethodId, string MethodCode, string MethodName,
    string Category, long AmountMinor, string? ExternalReference, string ConfirmationStatus,
    string ReconciliationStatus, Guid? ShiftId);
public sealed record PaymentDto(Guid Id, string PaymentNo, Guid OrderId, string Status, string Currency,
    long ReceivableMinor, long PaidMinor, DateTimeOffset? PaidAtUtc, IReadOnlyList<PaymentAllocationDto> Allocations);
public sealed record CashierShiftDto(Guid Id, string ShiftNo, Guid OperatorId, string Status, long OpeningCashMinor,
    long? ExpectedCashMinor, long? SubmittedCashMinor, long? CashDifferenceMinor, long? PendingReconciliationMinor,
    string? HandoverNote, DateTimeOffset OpenedAtUtc, DateTimeOffset? SubmittedAtUtc, Guid? ReviewedBy,
    string? ReviewReason, DateTimeOffset? ClosedAtUtc, uint Version);
public sealed record CashierShiftReviewDto(CashierShiftDto Shift, string OperatorDisplayName);
public sealed record SettleAllocationCommand(Guid MethodId, long AmountMinor, string? ExternalReference);
public sealed record SettleOrderCommand(Guid StoreId, Guid OrderId, uint ExpectedVersion,
    IReadOnlyList<SettleAllocationCommand> Allocations, Guid CommandId, Guid OperatorId);
public sealed record OpenShiftCommand(Guid StoreId, long OpeningCashMinor, Guid CommandId, Guid OperatorId);
public sealed record SubmitShiftCommand(Guid StoreId, Guid ShiftId, uint ExpectedVersion, long SubmittedCashMinor,
    string? Note, Guid CommandId, Guid OperatorId);
public sealed record ReviewShiftCommand(Guid StoreId, Guid ShiftId, uint ExpectedVersion, string? Reason,
    Guid CommandId, Guid ReviewerId, bool IsOwner);

public interface IPaymentService
{
    Task<IReadOnlyList<PaymentMethodDto>> ListMethodsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PaymentDto>> ListPaymentsAsync(Guid tenantId, Guid storeId, CancellationToken cancellationToken);
    Task<Result<PaymentDto>> SettleOrderAsync(Guid tenantId, SettleOrderCommand command, CancellationToken cancellationToken);
    Task<CashierShiftDto?> GetCurrentShiftAsync(Guid tenantId, Guid storeId, Guid operatorId, CancellationToken cancellationToken);
    Task<IReadOnlyList<CashierShiftReviewDto>> ListShiftsAsync(Guid tenantId, Guid storeId, CancellationToken cancellationToken);
    Task<Result<CashierShiftDto>> OpenShiftAsync(Guid tenantId, OpenShiftCommand command, CancellationToken cancellationToken);
    Task<Result<CashierShiftDto>> SubmitShiftAsync(Guid tenantId, SubmitShiftCommand command, CancellationToken cancellationToken);
    Task<Result<CashierShiftDto>> ReviewShiftAsync(Guid tenantId, ReviewShiftCommand command, CancellationToken cancellationToken);
}
