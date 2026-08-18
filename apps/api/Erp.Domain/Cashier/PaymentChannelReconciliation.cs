using Erp.Domain.Common;

namespace Erp.Domain.Cashier;

public enum PaymentChannelReconciliationRunStatus { Running, Matched, Differences, Failed }
public enum PaymentChannelReconciliationItemType { Payment, Refund }
public enum PaymentChannelReconciliationItemStatus
{
    Matched,
    LocalOnly,
    ChannelOnly,
    AmountMismatch,
    StateMismatch,
    Resolved,
}

public sealed class PaymentChannelReconciliationRun : Entity
{
    private PaymentChannelReconciliationRun() { }

    public PaymentChannelReconciliationRun(Guid tenantId, Guid storeId, Guid configurationId,
        PaymentChannelProvider provider, DateOnly businessDate, int attemptNo, Guid startedBy,
        DateTimeOffset startedAtUtc) : base(tenantId)
    {
        if (attemptNo is < 1 or > 100)
            throw new DomainRuleException("VALIDATION_FAILED", "对账尝试次数必须为1到100");
        StoreId = storeId;
        ConfigurationId = configurationId;
        Provider = provider;
        BusinessDate = businessDate;
        AttemptNo = attemptNo;
        StartedBy = startedBy;
        StartedAtUtc = startedAtUtc;
        Status = PaymentChannelReconciliationRunStatus.Running;
    }

    public Guid StoreId { get; private set; }
    public Guid ConfigurationId { get; private set; }
    public PaymentChannelProvider Provider { get; private set; }
    public DateOnly BusinessDate { get; private set; }
    public int AttemptNo { get; private set; }
    public PaymentChannelReconciliationRunStatus Status { get; private set; }
    public Guid StartedBy { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public int ChannelEntryCount { get; private set; }
    public int MatchedCount { get; private set; }
    public int DifferenceCount { get; private set; }
    public byte[]? SourceSha256 { get; private set; }
    public string? FailureCode { get; private set; }

    public void Complete(int channelEntryCount, int matchedCount, int differenceCount,
        byte[] sourceSha256, DateTimeOffset now)
    {
        if (Status != PaymentChannelReconciliationRunStatus.Running)
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "当前对账任务不能完成");
        if (channelEntryCount < 0 || matchedCount < 0 || differenceCount < 0 ||
            matchedCount + differenceCount == 0 && channelEntryCount != 0 || sourceSha256.Length != 32)
            throw new DomainRuleException("VALIDATION_FAILED", "对账统计或账单摘要无效");
        ChannelEntryCount = channelEntryCount;
        MatchedCount = matchedCount;
        DifferenceCount = differenceCount;
        SourceSha256 = sourceSha256.ToArray();
        Status = differenceCount == 0
            ? PaymentChannelReconciliationRunStatus.Matched
            : PaymentChannelReconciliationRunStatus.Differences;
        CompletedAtUtc = now;
        FailureCode = null;
        Touch();
    }

    public void Fail(string failureCode, DateTimeOffset now)
    {
        if (Status != PaymentChannelReconciliationRunStatus.Running)
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "当前对账任务不能标记失败");
        FailureCode = Required(failureCode, 80, "对账失败代码");
        Status = PaymentChannelReconciliationRunStatus.Failed;
        CompletedAtUtc = now;
        Touch();
    }

    private static string Required(string value, int maximum, string field)
    {
        var normalized = value.Trim();
        if (normalized.Length is 0 || normalized.Length > maximum)
            throw new DomainRuleException("VALIDATION_FAILED", $"{field}长度不正确");
        return normalized;
    }
}

public sealed class PaymentChannelReconciliationItem : Entity
{
    private PaymentChannelReconciliationItem() { }

    public PaymentChannelReconciliationItem(Guid tenantId, Guid runId,
        PaymentChannelReconciliationItemType itemType, PaymentChannelReconciliationItemStatus status,
        string matchKey, string? outTradeNo, string? outRefundNo, string? providerTradeNo,
        Guid? paymentAllocationId, Guid? channelRefundId, long? localAmountMinor,
        long? channelAmountMinor, long channelFeeMinor, string? localStatus, string? channelStatus)
        : base(tenantId)
    {
        if (status == PaymentChannelReconciliationItemStatus.Resolved)
            throw new DomainRuleException("VALIDATION_FAILED", "新对账明细不能直接标记已处置");
        if (localAmountMinor is < 0 || channelAmountMinor is < 0 || channelFeeMinor < 0)
            throw new DomainRuleException("VALIDATION_FAILED", "对账金额不能为负");
        if ((itemType == PaymentChannelReconciliationItemType.Payment && string.IsNullOrWhiteSpace(outTradeNo)) ||
            (itemType == PaymentChannelReconciliationItemType.Refund && string.IsNullOrWhiteSpace(outRefundNo)))
            throw new DomainRuleException("VALIDATION_FAILED", "对账明细缺少商户单号");
        RunId = runId;
        ItemType = itemType;
        Status = status;
        MatchKey = Required(matchKey, 160, "对账匹配键");
        OutTradeNo = Optional(outTradeNo, 64, "商户订单号");
        OutRefundNo = Optional(outRefundNo, 64, "商户退款单号");
        ProviderTradeNo = Optional(providerTradeNo, 128, "渠道交易号");
        PaymentAllocationId = paymentAllocationId;
        ChannelRefundId = channelRefundId;
        LocalAmountMinor = localAmountMinor;
        ChannelAmountMinor = channelAmountMinor;
        ChannelFeeMinor = channelFeeMinor;
        LocalStatus = Optional(localStatus, 40, "本地状态");
        ChannelStatus = Optional(channelStatus, 80, "渠道状态");
    }

    public Guid RunId { get; private set; }
    public PaymentChannelReconciliationItemType ItemType { get; private set; }
    public PaymentChannelReconciliationItemStatus Status { get; private set; }
    public string MatchKey { get; private set; } = string.Empty;
    public string? OutTradeNo { get; private set; }
    public string? OutRefundNo { get; private set; }
    public string? ProviderTradeNo { get; private set; }
    public Guid? PaymentAllocationId { get; private set; }
    public Guid? ChannelRefundId { get; private set; }
    public long? LocalAmountMinor { get; private set; }
    public long? ChannelAmountMinor { get; private set; }
    public long ChannelFeeMinor { get; private set; }
    public string? LocalStatus { get; private set; }
    public string? ChannelStatus { get; private set; }
    public Guid? ResolvedBy { get; private set; }
    public DateTimeOffset? ResolvedAtUtc { get; private set; }
    public string? ResolutionReason { get; private set; }

    public void Resolve(Guid resolvedBy, string reason, DateTimeOffset now)
    {
        if (Status is PaymentChannelReconciliationItemStatus.Matched or
            PaymentChannelReconciliationItemStatus.Resolved)
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "当前对账明细无需处置");
        ResolutionReason = Required(reason, 500, "差异处置说明");
        ResolvedBy = resolvedBy;
        ResolvedAtUtc = now;
        Status = PaymentChannelReconciliationItemStatus.Resolved;
        Touch();
    }

    private static string Required(string value, int maximum, string field)
    {
        var normalized = value.Trim();
        if (normalized.Length is 0 || normalized.Length > maximum)
            throw new DomainRuleException("VALIDATION_FAILED", $"{field}长度不正确");
        return normalized;
    }

    private static string? Optional(string? value, int maximum, string field) =>
        string.IsNullOrWhiteSpace(value) ? null : Required(value, maximum, field);
}
