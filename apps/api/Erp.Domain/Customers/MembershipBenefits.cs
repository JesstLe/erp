using Erp.Domain.Common;

namespace Erp.Domain.Customers;

public enum ServicePassStatus { Active, Exhausted, Expired, Cancelled }
public enum ServicePassLedgerAction { Issue, Redeem, Reverse, Expire, Cancel }
public enum MemberPointGrantStatus { Active, Exhausted, Expired }

public sealed class ServicePass : Entity
{
    private ServicePass() { }

    public ServicePass(Guid tenantId, Guid storeId, Guid customerId, Guid cardId, Guid serviceItemId,
        string passName, int purchasedUses, int bonusUses, DateOnly validFrom, DateOnly? validTo,
        string reason) : base(tenantId)
    {
        if (purchasedUses < 0 || bonusUses < 0 || purchasedUses + bonusUses is < 1 or > 100_000)
            throw new DomainRuleException("VALIDATION_FAILED", "次卡购买次数和赠送次数合计必须为1到100000");
        if (validTo < validFrom)
            throw new DomainRuleException("VALIDATION_FAILED", "次卡到期日不能早于生效日");
        StoreId = storeId;
        CustomerId = customerId;
        CardId = cardId;
        ServiceItemId = serviceItemId;
        PassName = Required(passName, 100, "次卡名称");
        PurchasedUses = purchasedUses;
        BonusUses = bonusUses;
        RemainingPurchasedUses = purchasedUses;
        RemainingBonusUses = bonusUses;
        ValidFrom = validFrom;
        ValidTo = validTo;
        IssueReason = Required(reason, 500, "发放原因");
        Status = ServicePassStatus.Active;
    }

    public Guid StoreId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid CardId { get; private set; }
    public Guid ServiceItemId { get; private set; }
    public string PassName { get; private set; } = string.Empty;
    public int PurchasedUses { get; private set; }
    public int BonusUses { get; private set; }
    public int RemainingPurchasedUses { get; private set; }
    public int RemainingBonusUses { get; private set; }
    public int RemainingUses => RemainingPurchasedUses + RemainingBonusUses;
    public DateOnly ValidFrom { get; private set; }
    public DateOnly? ValidTo { get; private set; }
    public string IssueReason { get; private set; } = string.Empty;
    public ServicePassStatus Status { get; private set; }

    public ServicePassLedger CreateIssueLedger(Guid commandId, Guid operatorId, DateTimeOffset now) =>
        new(TenantId, Id, StoreId, CustomerId, ServicePassLedgerAction.Issue, PurchasedUses, BonusUses,
            RemainingPurchasedUses, RemainingBonusUses, null, null, commandId, operatorId, IssueReason, now);

    public ServicePassLedger Redeem(Guid operationStoreId, int uses, Guid? serviceOrderId, string reason, Guid commandId,
        Guid operatorId, DateOnly localDate, DateTimeOffset now)
    {
        EnsureActive(localDate);
        if (uses < 1 || uses > RemainingUses)
            throw new DomainRuleException("INSUFFICIENT_SERVICE_PASS_BALANCE", "核销次数必须大于0且不能超过剩余次数");
        var purchased = Math.Min(RemainingPurchasedUses, uses);
        var bonus = uses - purchased;
        RemainingPurchasedUses -= purchased;
        RemainingBonusUses -= bonus;
        Status = RemainingUses == 0 ? ServicePassStatus.Exhausted : ServicePassStatus.Active;
        Touch();
        return new ServicePassLedger(TenantId, Id, operationStoreId, CustomerId, ServicePassLedgerAction.Redeem,
            -purchased, -bonus, RemainingPurchasedUses, RemainingBonusUses, serviceOrderId, null,
            commandId, operatorId, Required(reason, 500, "核销原因"), now);
    }

    public ServicePassLedger Reverse(Guid operationStoreId, ServicePassLedger original, string reason, Guid commandId,
        Guid operatorId, DateOnly localDate, DateTimeOffset now)
    {
        if (original.PassId != Id || original.Action != ServicePassLedgerAction.Redeem)
            throw new DomainRuleException("SERVICE_PASS_LEDGER_NOT_REVERSIBLE", "只有本次卡的核销流水可以撤销");
        if (Status is ServicePassStatus.Expired or ServicePassStatus.Cancelled || ValidTo < localDate)
            throw new DomainRuleException("SERVICE_PASS_NOT_ACTIVE", "次卡已过期或已作废，不能恢复次数");
        var purchased = checked(-original.PurchasedUsesDelta);
        var bonus = checked(-original.BonusUsesDelta);
        RemainingPurchasedUses = checked(RemainingPurchasedUses + purchased);
        RemainingBonusUses = checked(RemainingBonusUses + bonus);
        Status = ServicePassStatus.Active;
        Touch();
        return new ServicePassLedger(TenantId, Id, operationStoreId, CustomerId, ServicePassLedgerAction.Reverse,
            purchased, bonus, RemainingPurchasedUses, RemainingBonusUses, original.ServiceOrderId,
            original.Id, commandId, operatorId, Required(reason, 500, "撤销原因"), now);
    }

    public ServicePassLedger Expire(Guid operationStoreId, string reason, Guid commandId, Guid operatorId, DateOnly localDate,
        DateTimeOffset now)
    {
        if (Status != ServicePassStatus.Active || ValidTo is null || ValidTo >= localDate)
            throw new DomainRuleException("SERVICE_PASS_NOT_DUE", "次卡尚未到期或当前状态不能过期处理");
        var purchased = RemainingPurchasedUses;
        var bonus = RemainingBonusUses;
        RemainingPurchasedUses = 0;
        RemainingBonusUses = 0;
        Status = ServicePassStatus.Expired;
        Touch();
        return new ServicePassLedger(TenantId, Id, operationStoreId, CustomerId, ServicePassLedgerAction.Expire,
            -purchased, -bonus, 0, 0, null, null, commandId, operatorId,
            Required(reason, 500, "过期处理原因"), now);
    }

    private void EnsureActive(DateOnly localDate)
    {
        if (Status != ServicePassStatus.Active || ValidFrom > localDate || ValidTo < localDate)
            throw new DomainRuleException("SERVICE_PASS_NOT_ACTIVE", "次卡尚未生效、已耗尽、已过期或已作废");
    }

    private static string Required(string value, int max, string field)
    {
        var normalized = value.Trim();
        if (normalized.Length is 0 || normalized.Length > max)
            throw new DomainRuleException("VALIDATION_FAILED", $"{field}长度不正确");
        return normalized;
    }
}

public sealed class ServicePassLedger : Entity
{
    private ServicePassLedger() { }

    internal ServicePassLedger(Guid tenantId, Guid passId, Guid storeId, Guid customerId,
        ServicePassLedgerAction action, int purchasedUsesDelta, int bonusUsesDelta,
        int purchasedUsesAfter, int bonusUsesAfter, Guid? serviceOrderId, Guid? reversedLedgerId,
        Guid commandId, Guid operatorId, string reason, DateTimeOffset occurredAtUtc) : base(tenantId)
    {
        PassId = passId;
        StoreId = storeId;
        CustomerId = customerId;
        Action = action;
        PurchasedUsesDelta = purchasedUsesDelta;
        BonusUsesDelta = bonusUsesDelta;
        PurchasedUsesAfter = purchasedUsesAfter;
        BonusUsesAfter = bonusUsesAfter;
        ServiceOrderId = serviceOrderId;
        ReversedLedgerId = reversedLedgerId;
        CommandId = commandId;
        OperatorId = operatorId;
        Reason = reason;
        OccurredAtUtc = occurredAtUtc;
    }

    public Guid PassId { get; private set; }
    public Guid StoreId { get; private set; }
    public Guid CustomerId { get; private set; }
    public ServicePassLedgerAction Action { get; private set; }
    public int PurchasedUsesDelta { get; private set; }
    public int BonusUsesDelta { get; private set; }
    public int PurchasedUsesAfter { get; private set; }
    public int BonusUsesAfter { get; private set; }
    public Guid? ServiceOrderId { get; private set; }
    public Guid? ReversedLedgerId { get; private set; }
    public Guid CommandId { get; private set; }
    public Guid OperatorId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }
}

public sealed class MemberPointGrant : Entity
{
    private MemberPointGrant() { }

    public MemberPointGrant(Guid tenantId, Guid storeId, Guid customerId, Guid cardId, Guid accountId,
        long units, DateOnly? expiresOn, string sourceType, Guid sourceId) : base(tenantId)
    {
        if (units is < 1 or > 1_000_000_000)
            throw new DomainRuleException("VALIDATION_FAILED", "积分增加数量必须为1到1000000000");
        StoreId = storeId;
        CustomerId = customerId;
        CardId = cardId;
        AccountId = accountId;
        OriginalUnits = units;
        RemainingUnits = units;
        ExpiresOn = expiresOn;
        SourceType = sourceType.Trim();
        SourceId = sourceId;
        Status = MemberPointGrantStatus.Active;
    }

    public Guid StoreId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid CardId { get; private set; }
    public Guid AccountId { get; private set; }
    public long OriginalUnits { get; private set; }
    public long RemainingUnits { get; private set; }
    public DateOnly? ExpiresOn { get; private set; }
    public string SourceType { get; private set; } = string.Empty;
    public Guid SourceId { get; private set; }
    public MemberPointGrantStatus Status { get; private set; }

    public long Consume(long requested)
    {
        if (requested <= 0 || Status != MemberPointGrantStatus.Active) return 0;
        var consumed = Math.Min(RemainingUnits, requested);
        RemainingUnits -= consumed;
        if (RemainingUnits == 0) Status = MemberPointGrantStatus.Exhausted;
        Touch();
        return consumed;
    }

    public void Restore(long units, DateOnly localDate)
    {
        if (units <= 0 || RemainingUnits + units > OriginalUnits || ExpiresOn < localDate)
            throw new DomainRuleException("POINT_REVERSAL_NOT_AVAILABLE", "原积分批次已过期或恢复数量无效");
        RemainingUnits += units;
        Status = MemberPointGrantStatus.Active;
        Touch();
    }

    public long Expire(DateOnly localDate)
    {
        if (Status != MemberPointGrantStatus.Active || ExpiresOn is null || ExpiresOn >= localDate)
            return 0;
        var units = RemainingUnits;
        RemainingUnits = 0;
        Status = MemberPointGrantStatus.Expired;
        Touch();
        return units;
    }
}

public sealed class MemberPointUseAllocation : Entity
{
    private MemberPointUseAllocation() { }

    public MemberPointUseAllocation(Guid tenantId, Guid debitLedgerId, Guid grantId, long units)
        : base(tenantId)
    {
        if (units <= 0) throw new DomainRuleException("VALIDATION_FAILED", "积分核销分摊必须大于0");
        DebitLedgerId = debitLedgerId;
        GrantId = grantId;
        Units = units;
    }

    public Guid DebitLedgerId { get; private set; }
    public Guid GrantId { get; private set; }
    public long Units { get; private set; }
}
