using Erp.Domain.Common;

namespace Erp.Domain.Cashier;

public enum PriceAuthorizationState
{
    NotRequired,
    DirectAuthorized,
    PendingApproval,
    Approved,
    Rejected,
    Cancelled,
}

public enum PriceOverrideApprovalStatus { Pending, Approved, Rejected, Cancelled }

public sealed class PriceOverridePolicy : Entity
{
    private PriceOverridePolicy() { }

    public PriceOverridePolicy(Guid tenantId, int policyVersion, int managerLineDiscountBasisPoints,
        long managerOrderDiscountMinor, bool allowManagerPriceIncrease, Guid createdBy,
        DateTimeOffset effectiveFromUtc) : base(tenantId)
    {
        if (policyVersion < 1)
            throw new DomainRuleException("VALIDATION_FAILED", "改价策略版本必须大于0");
        if (managerLineDiscountBasisPoints is < 0 or > 10_000)
            throw new DomainRuleException("VALIDATION_FAILED", "店长单行优惠比例必须为0%到100%");
        if (managerOrderDiscountMinor is < 0 or > 10_000_000_000)
            throw new DomainRuleException("VALIDATION_FAILED", "店长整单优惠额度超出允许范围");
        if (createdBy == Guid.Empty)
            throw new DomainRuleException("VALIDATION_FAILED", "改价策略创建人无效");
        PolicyVersion = policyVersion;
        ManagerLineDiscountBasisPoints = managerLineDiscountBasisPoints;
        ManagerOrderDiscountMinor = managerOrderDiscountMinor;
        AllowManagerPriceIncrease = allowManagerPriceIncrease;
        CreatedBy = createdBy;
        EffectiveFromUtc = effectiveFromUtc;
        IsActive = true;
    }

    public int PolicyVersion { get; private set; }
    public int ManagerLineDiscountBasisPoints { get; private set; }
    public long ManagerOrderDiscountMinor { get; private set; }
    public bool AllowManagerPriceIncrease { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset EffectiveFromUtc { get; private set; }
    public bool IsActive { get; private set; }

    public void Retire()
    {
        if (!IsActive)
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "改价策略已经停用");
        IsActive = false;
        Touch();
    }

    public bool ManagerRequiresApproval(ServiceOrder order)
    {
        if (!order.HasPriceOverride) return false;
        if (!AllowManagerPriceIncrease && order.Lines.Any(x => x.EnteredPriceMinor > x.ReferencePriceMinor))
            return true;
        if (order.MaximumLineDiscountBasisPoints > ManagerLineDiscountBasisPoints)
            return true;
        return order.ManualPriceOverrideDiscountMinor > ManagerOrderDiscountMinor;
    }

    public static PriceOverridePolicy Default(Guid tenantId, Guid createdBy, DateTimeOffset now) =>
        new(tenantId, 1, 1_000, 5_000, false, createdBy, now);
}

public sealed class PriceOverrideApproval : Entity
{
    private PriceOverrideApproval() { }

    public PriceOverrideApproval(Guid tenantId, Guid storeId, Guid serviceOrderId, Guid requesterId,
        string requesterRole, Guid policyId, int policyVersion, long referenceAmountMinor,
        long receivableMinor, int maximumLineDiscountBasisPoints, int managerLineDiscountBasisPoints,
        long managerOrderDiscountMinor, bool allowManagerPriceIncrease, DateTimeOffset now) : base(tenantId)
    {
        if (storeId == Guid.Empty || serviceOrderId == Guid.Empty || requesterId == Guid.Empty || policyId == Guid.Empty)
            throw new DomainRuleException("VALIDATION_FAILED", "改价审批关联信息无效");
        var role = requesterRole.Trim();
        if (role.Length is 0 or > 64)
            throw new DomainRuleException("VALIDATION_FAILED", "改价申请角色快照无效");
        if (policyVersion < 1 || referenceAmountMinor < 0 || receivableMinor < 0 ||
            maximumLineDiscountBasisPoints is < 0 or > 10_000 ||
            managerLineDiscountBasisPoints is < 0 or > 10_000 || managerOrderDiscountMinor < 0)
            throw new DomainRuleException("VALIDATION_FAILED", "改价审批金额或策略快照无效");
        StoreId = storeId;
        ServiceOrderId = serviceOrderId;
        RequesterId = requesterId;
        RequesterRoleSnapshot = role;
        PolicyId = policyId;
        PolicyVersion = policyVersion;
        ReferenceAmountMinor = referenceAmountMinor;
        ReceivableMinor = receivableMinor;
        DifferenceMinor = checked(receivableMinor - referenceAmountMinor);
        MaximumLineDiscountBasisPoints = maximumLineDiscountBasisPoints;
        ManagerLineDiscountBasisPoints = managerLineDiscountBasisPoints;
        ManagerOrderDiscountMinor = managerOrderDiscountMinor;
        AllowManagerPriceIncrease = allowManagerPriceIncrease;
        RequestedAtUtc = now;
        Status = PriceOverrideApprovalStatus.Pending;
    }

    public Guid StoreId { get; private set; }
    public Guid ServiceOrderId { get; private set; }
    public Guid RequesterId { get; private set; }
    public string RequesterRoleSnapshot { get; private set; } = string.Empty;
    public Guid PolicyId { get; private set; }
    public int PolicyVersion { get; private set; }
    public long ReferenceAmountMinor { get; private set; }
    public long ReceivableMinor { get; private set; }
    public long DifferenceMinor { get; private set; }
    public int MaximumLineDiscountBasisPoints { get; private set; }
    public int ManagerLineDiscountBasisPoints { get; private set; }
    public long ManagerOrderDiscountMinor { get; private set; }
    public bool AllowManagerPriceIncrease { get; private set; }
    public PriceOverrideApprovalStatus Status { get; private set; }
    public DateTimeOffset RequestedAtUtc { get; private set; }
    public Guid? DecidedBy { get; private set; }
    public DateTimeOffset? DecidedAtUtc { get; private set; }
    public string? DecisionNote { get; private set; }

    public void Approve(Guid decidedBy, string? note, DateTimeOffset now)
    {
        EnsureCanDecide(decidedBy);
        Status = PriceOverrideApprovalStatus.Approved;
        DecidedBy = decidedBy;
        DecidedAtUtc = now;
        DecisionNote = Optional(note, 500);
        Touch();
    }

    public void Reject(Guid decidedBy, string reason, DateTimeOffset now)
    {
        EnsureCanDecide(decidedBy);
        var normalized = reason.Trim();
        if (normalized.Length is < 2 or > 500)
            throw new DomainRuleException("VALIDATION_FAILED", "驳回原因必须为2到500字");
        Status = PriceOverrideApprovalStatus.Rejected;
        DecidedBy = decidedBy;
        DecidedAtUtc = now;
        DecisionNote = normalized;
        Touch();
    }

    public void Cancel(DateTimeOffset now, string? note = null)
    {
        if (Status != PriceOverrideApprovalStatus.Pending) return;
        Status = PriceOverrideApprovalStatus.Cancelled;
        DecidedAtUtc = now;
        DecisionNote = string.IsNullOrWhiteSpace(note) ? "消费单已作废" : Optional(note, 500);
        Touch();
    }

    private void EnsureCanDecide(Guid decidedBy)
    {
        if (Status != PriceOverrideApprovalStatus.Pending)
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "只有待审批的改价申请可以处理");
        if (decidedBy == Guid.Empty || decidedBy == RequesterId)
            throw new DomainRuleException("PRICE_APPROVAL_SELF_REVIEW_FORBIDDEN", "改价申请人不能审批自己的申请");
    }

    private static string? Optional(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > max)
            throw new DomainRuleException("VALIDATION_FAILED", $"审批说明不能超过{max}字");
        return normalized;
    }
}
