using Erp.Application.Cashier;
using Erp.Application.Common;

namespace Erp.Application.Customers;

public sealed record MemberTopupDto(Guid Id, string TopupNo, Guid StoreId, Guid CustomerId, Guid CardId,
    long PrincipalMinor, long BonusMinor, long ReceivableMinor, string Status, string? Note,
    long RefundedPrincipalMinor, long RevokedBonusMinor, long RemainingPrincipalMinor,
    DateTimeOffset PaidAtUtc, Guid PaymentId, string PaymentNo, string PaymentStatus,
    long PaymentRefundedMinor, uint PaymentVersion,
    IReadOnlyList<PaymentAllocationDto> Allocations);

public sealed record CreateMemberTopupCommand(Guid StoreId, Guid CustomerId, Guid CardId,
    long PrincipalMinor, long BonusMinor, string? Note,
    IReadOnlyList<SettleAllocationCommand> Allocations, Guid CommandId, Guid OperatorId,
    bool CanGrantBonus);

public interface IMemberTopupService
{
    Task<PageResult<MemberTopupDto>> ListAsync(Guid tenantId, Guid storeId, Guid? customerId,
        int page, int pageSize, CancellationToken cancellationToken);
    Task<Result<MemberTopupDto>> CreateAndSettleAsync(Guid tenantId, CreateMemberTopupCommand command,
        CancellationToken cancellationToken);
}
