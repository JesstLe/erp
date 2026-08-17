using Erp.Domain.Common;

namespace Erp.Domain.Customers;

public enum MemberCardTypeStatus { Published, Disabled }
public enum MemberCardStatus { Active, Expired, Disabled }
public enum MemberAccountType { Principal, Bonus, Points }
public enum MemberAccountStatus { Active, Frozen, Closed }
public enum LedgerDirection { Credit, Debit }

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
}

public sealed class MemberAccountLedger : Entity
{
    private MemberAccountLedger() { }

    public Guid AccountId { get; private set; }
    public string BusinessType { get; private set; } = string.Empty;
    public Guid BusinessId { get; private set; }
    public LedgerDirection Direction { get; private set; }
    public long Units { get; private set; }
    public long BalanceBefore { get; private set; }
    public long BalanceAfter { get; private set; }
    public Guid CommandId { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
}
