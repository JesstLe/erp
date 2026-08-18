using Erp.Application.Common;

namespace Erp.Application.Customers;

public sealed record ServicePassLedgerDto(Guid Id, Guid StoreId, string Action, int PurchasedUsesDelta,
    int BonusUsesDelta, int PurchasedUsesAfter, int BonusUsesAfter, Guid? ServiceOrderId,
    Guid? ReversedLedgerId, string Reason, DateTimeOffset OccurredAtUtc);

public sealed record ServicePassDto(Guid Id, Guid StoreId, Guid CustomerId, Guid CardId,
    Guid ServiceItemId, string ServiceItemName, string PassName, int PurchasedUses, int BonusUses,
    int RemainingPurchasedUses, int RemainingBonusUses, int RemainingUses, DateOnly ValidFrom,
    DateOnly? ValidTo, string Status, uint Version, IReadOnlyList<ServicePassLedgerDto> Ledgers);

public sealed record PointGrantDto(Guid Id, long OriginalUnits, long RemainingUnits, DateOnly? ExpiresOn,
    string SourceType, string Status);

public sealed record PointLedgerDto(Guid Id, string BusinessType, Guid BusinessId, string Direction,
    long Units, long BalanceBefore, long BalanceAfter, DateTimeOffset OccurredAtUtc);

public sealed record MemberPointSummaryDto(Guid CardId, Guid AccountId, long BalanceUnits, uint AccountVersion,
    IReadOnlyList<PointGrantDto> Grants, IReadOnlyList<PointLedgerDto> Ledgers);

public sealed record MembershipBenefitsDto(IReadOnlyList<ServicePassDto> ServicePasses,
    IReadOnlyList<MemberPointSummaryDto> PointAccounts);

public sealed record IssueServicePassCommand(Guid StoreId, Guid CustomerId, Guid CardId,
    Guid ServiceItemId, string PassName, int PurchasedUses, int BonusUses, DateOnly ValidFrom,
    DateOnly? ValidTo, string Reason, Guid CommandId, Guid OperatorId);

public sealed record RedeemServicePassCommand(Guid StoreId, Guid PassId, int Uses,
    Guid? ServiceOrderId, string Reason, uint ExpectedVersion, Guid CommandId, Guid OperatorId);

public sealed record ReverseServicePassCommand(Guid StoreId, Guid PassId, Guid LedgerId, string Reason,
    uint ExpectedVersion, Guid CommandId, Guid OperatorId);

public sealed record ExpireServicePassCommand(Guid StoreId, Guid PassId, string Reason,
    uint ExpectedVersion, Guid CommandId, Guid OperatorId);

public sealed record AdjustMemberPointsCommand(Guid StoreId, Guid CustomerId, Guid CardId, long Units,
    bool Credit, DateOnly? ExpiresOn, string Reason, Guid CommandId, Guid OperatorId);

public sealed record ReverseMemberPointsCommand(Guid StoreId, Guid CardId, Guid LedgerId, string Reason,
    Guid CommandId, Guid OperatorId);

public sealed record ExpireMemberPointsCommand(Guid StoreId, Guid CardId, string Reason,
    Guid CommandId, Guid OperatorId);

public interface IMembershipBenefitService
{
    Task<Result<MembershipBenefitsDto>> GetAsync(Guid tenantId, Guid storeId, Guid customerId,
        CancellationToken cancellationToken);
    Task<Result<ServicePassDto>> IssuePassAsync(Guid tenantId, IssueServicePassCommand command,
        CancellationToken cancellationToken);
    Task<Result<ServicePassDto>> RedeemPassAsync(Guid tenantId, RedeemServicePassCommand command,
        CancellationToken cancellationToken);
    Task<Result<ServicePassDto>> ReversePassAsync(Guid tenantId, ReverseServicePassCommand command,
        CancellationToken cancellationToken);
    Task<Result<ServicePassDto>> ExpirePassAsync(Guid tenantId, ExpireServicePassCommand command,
        CancellationToken cancellationToken);
    Task<Result<MemberPointSummaryDto>> AdjustPointsAsync(Guid tenantId, AdjustMemberPointsCommand command,
        CancellationToken cancellationToken);
    Task<Result<MemberPointSummaryDto>> ReversePointsAsync(Guid tenantId, ReverseMemberPointsCommand command,
        CancellationToken cancellationToken);
    Task<Result<MemberPointSummaryDto>> ExpirePointsAsync(Guid tenantId, ExpireMemberPointsCommand command,
        CancellationToken cancellationToken);
}
