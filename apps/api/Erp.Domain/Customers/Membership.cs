using Erp.Domain.Common;

namespace Erp.Domain.Customers;

public enum MemberCardTypeStatus { Published, Disabled }
public enum MemberCardStatus { Active, Expired, Disabled }
public enum MemberAccountType { Principal, Bonus, Points }
public enum MemberAccountStatus { Active, Frozen, Closed }
public enum LedgerDirection { Credit, Debit }
public enum MemberTopupStatus { Paid, Cancelled, PartiallyRefunded, Refunded }

public sealed class MemberCardType : Entity
{
    private MemberCardType() { }

    public MemberCardType(Guid tenantId, string code, string name, int? validityDays)
        : base(tenantId)
    {
        Code = Required(code, 40, "卡类编号").ToUpperInvariant();
        Name = Required(name, 80, "卡类名称");
        if (validityDays is < 1 or > 3650) throw new DomainRuleException("VALIDATION_FAILED", "有效期天数必须为1到3650");
        ValidityDays = validityDays;
        Status = MemberCardTypeStatus.Published;
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public int? ValidityDays { get; private set; }
    public MemberCardTypeStatus Status { get; private set; }

    private static string Required(string value, int max, string field)
    {
        var normalized = value.Trim();
        if (normalized.Length is 0 || normalized.Length > max) throw new DomainRuleException("VALIDATION_FAILED", $"{field}长度不正确");
        return normalized;
    }
}

public sealed class MemberCard : Entity
{
    private MemberCard() { }

    public MemberCard(Guid tenantId, Guid customerId, Guid cardTypeId, Guid storeId, string cardNo,
        DateOnly validFrom, DateOnly? validTo, string? note)
        : base(tenantId)
    {
        CustomerId = customerId;
        CardTypeId = cardTypeId;
        StoreId = storeId;
        CardNo = cardNo.Trim().ToUpperInvariant();
        if (CardNo.Length is < 5 or > 40) throw new DomainRuleException("VALIDATION_FAILED", "会员卡号长度必须为5到40位");
        if (validTo < validFrom) throw new DomainRuleException("VALIDATION_FAILED", "到期日不能早于生效日");
        ValidFrom = validFrom;
        ValidTo = validTo;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (Note?.Length > 500) throw new DomainRuleException("VALIDATION_FAILED", "备注不能超过500字");
        Status = MemberCardStatus.Active;
    }

    public Guid CustomerId { get; private set; }
    public Guid CardTypeId { get; private set; }
    public Guid StoreId { get; private set; }
    public string CardNo { get; private set; } = string.Empty;
    public DateOnly ValidFrom { get; private set; }
    public DateOnly? ValidTo { get; private set; }
    public string? Note { get; private set; }
    public MemberCardStatus Status { get; private set; }
}

public sealed class MemberAccount : Entity
{
    private MemberAccount() { }

    public MemberAccount(Guid tenantId, Guid customerId, Guid cardId, MemberAccountType accountType)
        : base(tenantId)
    {
        CustomerId = customerId;
        CardId = cardId;
        AccountType = accountType;
        BalanceUnits = 0;
        Status = MemberAccountStatus.Active;
    }

    public Guid CustomerId { get; private set; }
    public Guid CardId { get; private set; }
    public MemberAccountType AccountType { get; private set; }
    public long BalanceUnits { get; private set; }
    public MemberAccountStatus Status { get; private set; }

    public MemberAccountLedger Credit(string businessType, Guid businessId, long units, Guid commandId,
        DateTimeOffset occurredAtUtc)
    {
        if (Status != MemberAccountStatus.Active)
            throw new DomainRuleException("MEMBER_ACCOUNT_NOT_ACTIVE", "会员账户当前不可入账");
        if (units <= 0 || units > 10_000_000_000)
            throw new DomainRuleException("VALIDATION_FAILED", "入账金额必须大于0且不超过允许范围");
        var before = BalanceUnits;
        BalanceUnits = checked(BalanceUnits + units);
        Touch();
        return new MemberAccountLedger(TenantId, Id, businessType, businessId, LedgerDirection.Credit,
            units, before, BalanceUnits, commandId, occurredAtUtc);
    }

    public MemberAccountLedger Debit(string businessType, Guid businessId, long units, Guid commandId,
        DateTimeOffset occurredAtUtc)
    {
        if (Status != MemberAccountStatus.Active)
            throw new DomainRuleException("MEMBER_ACCOUNT_NOT_ACTIVE", "会员账户当前不可扣款");
        if (units <= 0 || units > 10_000_000_000)
            throw new DomainRuleException("VALIDATION_FAILED", "扣款金额必须大于0且不超过允许范围");
        if (BalanceUnits < units)
            throw new DomainRuleException("INSUFFICIENT_MEMBER_BALANCE", "会员账户余额不足");
        var before = BalanceUnits;
        BalanceUnits -= units;
        Touch();
        return new MemberAccountLedger(TenantId, Id, businessType, businessId, LedgerDirection.Debit,
            units, before, BalanceUnits, commandId, occurredAtUtc);
    }
}

public sealed class MemberAccountLedger : Entity
{
    private MemberAccountLedger() { }

    internal MemberAccountLedger(Guid tenantId, Guid accountId, string businessType, Guid businessId,
        LedgerDirection direction, long units, long balanceBefore, long balanceAfter, Guid commandId,
        DateTimeOffset occurredAtUtc) : base(tenantId)
    {
        AccountId = accountId;
        BusinessType = Required(businessType, 40, "流水业务类型");
        BusinessId = businessId;
        Direction = direction;
        Units = units;
        BalanceBefore = balanceBefore;
        BalanceAfter = balanceAfter;
        CommandId = commandId;
        OccurredAtUtc = occurredAtUtc;
    }

    public Guid AccountId { get; private set; }
    public string BusinessType { get; private set; } = string.Empty;
    public Guid BusinessId { get; private set; }
    public LedgerDirection Direction { get; private set; }
    public long Units { get; private set; }
    public long BalanceBefore { get; private set; }
    public long BalanceAfter { get; private set; }
    public Guid CommandId { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }

    private static string Required(string value, int max, string field)
    {
        var normalized = value.Trim();
        if (normalized.Length is 0 || normalized.Length > max)
            throw new DomainRuleException("VALIDATION_FAILED", $"{field}长度不正确");
        return normalized;
    }
}

public sealed class MemberTopupOrder : Entity
{
    private MemberTopupOrder() { }

    public MemberTopupOrder(Guid tenantId, Guid storeId, Guid customerId, Guid cardId, string topupNo,
        long principalMinor, long bonusMinor, string? note, DateTimeOffset paidAtUtc) : base(tenantId)
    {
        if (principalMinor <= 0 || principalMinor > 10_000_000_000)
            throw new DomainRuleException("VALIDATION_FAILED", "储值本金必须大于0且不超过允许范围");
        if (bonusMinor < 0 || bonusMinor > 10_000_000_000)
            throw new DomainRuleException("VALIDATION_FAILED", "奖励金不能为负且不能超过允许范围");
        _ = checked(principalMinor + bonusMinor);
        StoreId = storeId;
        CustomerId = customerId;
        CardId = cardId;
        TopupNo = Required(topupNo, 40, "储值单号");
        PrincipalMinor = principalMinor;
        BonusMinor = bonusMinor;
        ReceivableMinor = principalMinor;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (Note?.Length > 500) throw new DomainRuleException("VALIDATION_FAILED", "备注不能超过500字");
        Status = MemberTopupStatus.Paid;
        PaidAtUtc = paidAtUtc;
    }

    public Guid StoreId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid CardId { get; private set; }
    public string TopupNo { get; private set; } = string.Empty;
    public long PrincipalMinor { get; private set; }
    public long BonusMinor { get; private set; }
    public long ReceivableMinor { get; private set; }
    public MemberTopupStatus Status { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset PaidAtUtc { get; private set; }

    private static string Required(string value, int max, string field)
    {
        var normalized = value.Trim();
        if (normalized.Length is 0 || normalized.Length > max)
            throw new DomainRuleException("VALIDATION_FAILED", $"{field}长度不正确");
        return normalized;
    }
}
