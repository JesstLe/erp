using Erp.Application.Common;

namespace Erp.Application.Cashier;

public sealed record RefundLineDto(Guid Id, Guid OriginalAllocationId, long AmountMinor,
    string Category, Guid? MemberAccountId, string Route, Guid? CashShiftId, DateTimeOffset? CompletedAtUtc);
public sealed record RefundDto(Guid Id, Guid PaymentId, string BusinessType, Guid BusinessId,
    string RefundNo, string Status, long AmountMinor,
    string Reason, Guid RequestedBy, DateTimeOffset RequestedAtUtc, Guid? ApprovedBy,
    DateTimeOffset? CompletedAtUtc, string? RejectionReason, uint Version,
    IReadOnlyList<RefundLineDto> Lines);
public sealed record RequestRefundLineCommand(Guid OriginalAllocationId, long AmountMinor);
public sealed record RequestRefundCommand(Guid StoreId, Guid PaymentId, uint ExpectedPaymentVersion,
    string Reason, IReadOnlyList<RequestRefundLineCommand> Lines, Guid CommandId, Guid OperatorId);
public sealed record ApproveRefundCommand(Guid StoreId, Guid RefundId, uint ExpectedVersion,
    Guid CommandId, Guid ApproverId);
public sealed record RejectRefundCommand(Guid StoreId, Guid RefundId, uint ExpectedVersion,
    string Reason, Guid CommandId, Guid ApproverId);

public interface IRefundService
{
    Task<IReadOnlyList<RefundDto>> ListAsync(Guid tenantId, Guid storeId, Guid? paymentId,
        CancellationToken cancellationToken);
    Task<Result<RefundDto>> RequestAsync(Guid tenantId, RequestRefundCommand command,
        CancellationToken cancellationToken);
    Task<Result<RefundDto>> ApproveAsync(Guid tenantId, ApproveRefundCommand command,
        CancellationToken cancellationToken);
    Task<Result<RefundDto>> RejectAsync(Guid tenantId, RejectRefundCommand command,
        CancellationToken cancellationToken);
}
