using Erp.Application.Cashier;
using Erp.Application.Common;

namespace Erp.Application.Customers;

public sealed record MemberTopupDto(Guid Id, string TopupNo, Guid StoreId, Guid CustomerId, Guid CardId,
    long PrincipalMinor, long BonusMinor, long ReceivableMinor, string Status, string? Note,
    DateTimeOffset PaidAtUtc, Guid PaymentId, string PaymentNo, string PaymentStatus,
    long PaymentRefundedMinor, uint PaymentVersion,
    IReadOnlyList<PaymentAllocationDto> Allocations);

public sealed record CreateMemberTopupCommand(Guid StoreId, Guid CustomerId, Guid CardId,
    long PrincipalMinor, long BonusMinor, string? Note,
    IReadOnlyList<SettleAllocationCommand> Allocations, Guid CommandId, Guid OperatorId,
    bool CanGrantBonus);

public interface IMemberTopupService
{
    Task<IReadOnlyList<MemberTopupDto>> ListAsync(Guid tenantId, Guid storeId, Guid? customerId,
        CancellationToken cancellationToken);
    Task<Result<MemberTopupDto>> CreateAndSettleAsync(Guid tenantId, CreateMemberTopupCommand command,
        CancellationToken cancellationToken);
}
