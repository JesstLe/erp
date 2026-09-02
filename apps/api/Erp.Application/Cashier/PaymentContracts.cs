using Erp.Application.Common;

namespace Erp.Application.Cashier;

public sealed record PaymentMethodDto(Guid Id, string Code, string Name, string Category, bool RequiresOpenShift,
    string? InternalAccountType, string? ChannelProvider);
public sealed record PaymentAllocationDto(Guid Id, Guid MethodId, string MethodCode, string MethodName,
    string Category, long AmountMinor, string? ExternalReference, string ConfirmationStatus,
    string ReconciliationStatus, Guid? ShiftId, Guid? MemberAccountId, string? ChannelProvider);
public sealed record PaymentDto(Guid Id, string PaymentNo, Guid? OrderId, string BusinessType, Guid BusinessId,
    string Status, string Currency,
    long ReceivableMinor, long PaidMinor, long RefundedMinor, long? CashTenderedMinor, long? CashChangeMinor,
    DateTimeOffset? PaidAtUtc, uint Version,
    IReadOnlyList<PaymentAllocationDto> Allocations);
public sealed record CashierShiftDto(Guid Id, string ShiftNo, Guid OperatorId, string Status, long OpeningCashMinor,
    long? ExpectedCashMinor, long? SubmittedCashMinor, long? CashDifferenceMinor, long? PendingReconciliationMinor,
    string? HandoverNote, DateTimeOffset OpenedAtUtc, DateTimeOffset? SubmittedAtUtc, Guid? ReviewedBy,
    string? ReviewReason, DateTimeOffset? ClosedAtUtc, uint Version);
public sealed record CashierShiftReviewDto(CashierShiftDto Shift, string OperatorDisplayName);
public sealed record PaymentReceiptLineDto(string LineType, string ItemCode, string ItemName, string? UnitName,
    int Quantity, long UnitPriceMinor, long AmountMinor, string? EmployeeName);
public sealed record PaymentReceiptDto(Guid PaymentId, string PaymentNo, string OrderNo, string StoreName,
    string? StoreAddress, IReadOnlyList<string> FacilityNumbers, string CustomerName, string CustomerMobile,
    string OperatorName, DateTimeOffset PaidAtUtc, DateTimeOffset PrintedAtUtc,
    int PrintSequence, string PrintLabel, long ReferenceAmountMinor, long DiscountMinor, long ReceivableMinor,
    long GroupBuyAmountMinor, string? GroupBuyPlatform, long? CashTenderedMinor, long? CashChangeMinor,
    long? MemberPrincipalBalanceAfterMinor, long? MemberBonusBalanceAfterMinor,
    IReadOnlyList<PaymentReceiptLineDto> Lines, IReadOnlyList<PaymentAllocationDto> Allocations);
public sealed record SettleAllocationCommand(Guid MethodId, long AmountMinor, string? ExternalReference,
    Guid? MemberAccountId = null);
public sealed record SettleOrderCommand(Guid StoreId, Guid OrderId, uint ExpectedVersion,
    IReadOnlyList<SettleAllocationCommand> Allocations, string? VerifiedMobile,
    Guid? VerificationChallengeId, long? CashTenderedMinor, Guid CommandId, Guid OperatorId);
public sealed record OpenShiftCommand(Guid StoreId, long OpeningCashMinor, Guid CommandId, Guid OperatorId);
public sealed record SubmitShiftCommand(Guid StoreId, Guid ShiftId, uint ExpectedVersion, long SubmittedCashMinor,
    string? Note, Guid CommandId, Guid OperatorId);
public sealed record ReviewShiftCommand(Guid StoreId, Guid ShiftId, uint ExpectedVersion, string? Reason,
    Guid CommandId, Guid ReviewerId, bool IsOwner);
public sealed record PrintPaymentReceiptCommand(Guid StoreId, Guid PaymentId, Guid CommandId, Guid OperatorId);

public interface IPaymentService
{
    Task<IReadOnlyList<PaymentMethodDto>> ListMethodsAsync(Guid tenantId, Guid? storeId,
        CancellationToken cancellationToken);
    Task<PageResult<PaymentDto>> ListPaymentsAsync(Guid tenantId, Guid storeId, int page, int pageSize,
        CancellationToken cancellationToken);
    Task<Result<PaymentReceiptDto>> PrintReceiptAsync(Guid tenantId, PrintPaymentReceiptCommand command,
        CancellationToken cancellationToken);
    Task<Result<PaymentDto>> SettleOrderAsync(Guid tenantId, SettleOrderCommand command, CancellationToken cancellationToken);
    Task<CashierShiftDto?> GetCurrentShiftAsync(Guid tenantId, Guid storeId, Guid operatorId, CancellationToken cancellationToken);
    Task<PageResult<CashierShiftReviewDto>> ListShiftsAsync(Guid tenantId, Guid storeId, int page, int pageSize,
        CancellationToken cancellationToken);
    Task<Result<CashierShiftDto>> OpenShiftAsync(Guid tenantId, OpenShiftCommand command, CancellationToken cancellationToken);
    Task<Result<CashierShiftDto>> SubmitShiftAsync(Guid tenantId, SubmitShiftCommand command, CancellationToken cancellationToken);
    Task<Result<CashierShiftDto>> ReviewShiftAsync(Guid tenantId, ReviewShiftCommand command, CancellationToken cancellationToken);
}
