using System.Text.RegularExpressions;
using Erp.Domain.Common;

namespace Erp.Domain.Cashier;

public enum PaymentChannelProvider { WeChatPay, Alipay }
public enum PaymentChannelEnvironment { Sandbox, Production }
public enum PaymentChannelOrderStatus { Created, QrReady, Paid, Closed, Failed, Expired }
public enum PaymentChannelEventStatus { Received, Processed, Ignored, Failed }
public enum PaymentChannelRefundStatus { Created, Processing, Succeeded, Failed }

public sealed class PaymentChannelConfiguration : Entity
{
    private static readonly Regex ProfilePattern = new("^[A-Z][A-Z0-9_]{2,39}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private PaymentChannelConfiguration() { }

    public PaymentChannelConfiguration(Guid tenantId, Guid storeId, PaymentChannelProvider provider,
        PaymentChannelEnvironment environment, string displayName, string credentialProfile, bool isEnabled)
        : base(tenantId)
    {
        StoreId = storeId;
        Provider = provider;
        Apply(environment, displayName, credentialProfile, isEnabled);
    }

    public Guid StoreId { get; private set; }
    public PaymentChannelProvider Provider { get; private set; }
    public PaymentChannelEnvironment Environment { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string CredentialProfile { get; private set; } = string.Empty;
    public bool IsEnabled { get; private set; }

    public void Reconfigure(PaymentChannelEnvironment environment, string displayName, string credentialProfile,
        bool isEnabled)
    {
        Apply(environment, displayName, credentialProfile, isEnabled);
        Touch();
    }

    private void Apply(PaymentChannelEnvironment environment, string displayName, string credentialProfile,
        bool isEnabled)
    {
        var normalizedName = displayName.Trim();
        if (normalizedName.Length is 0 or > 80)
            throw new DomainRuleException("VALIDATION_FAILED", "渠道显示名称长度必须为1到80字");
        var normalizedProfile = credentialProfile.Trim().ToUpperInvariant();
        if (!ProfilePattern.IsMatch(normalizedProfile))
            throw new DomainRuleException("VALIDATION_FAILED", "凭据配置名必须为3到40位大写字母、数字或下划线，且以字母开头");
        Environment = environment;
        DisplayName = normalizedName;
        CredentialProfile = normalizedProfile;
        IsEnabled = isEnabled;
    }
}

public sealed class PaymentChannelOrder : Entity
{
    private PaymentChannelOrder() { }

    public PaymentChannelOrder(Guid tenantId, Guid configurationId, Guid paymentAllocationId,
        PaymentChannelProvider provider, string outTradeNo, int attemptNo, long amountMinor, string subject,
        DateTimeOffset expiresAtUtc) : base(tenantId)
    {
        var normalizedTradeNo = Required(outTradeNo, 64, "渠道商户订单号");
        var normalizedSubject = Required(subject, 120, "支付标题");
        if (attemptNo is < 1 or > 100)
            throw new DomainRuleException("VALIDATION_FAILED", "渠道订单尝试次数必须为1到100");
        if (amountMinor <= 0 || amountMinor > 10_000_000_000)
            throw new DomainRuleException("VALIDATION_FAILED", "渠道支付金额必须大于0且不超过允许范围");
        if (expiresAtUtc <= CreatedAtUtc || expiresAtUtc > CreatedAtUtc.AddHours(2))
            throw new DomainRuleException("VALIDATION_FAILED", "渠道订单有效期必须在创建后2小时以内");
        ConfigurationId = configurationId;
        PaymentAllocationId = paymentAllocationId;
        Provider = provider;
        OutTradeNo = normalizedTradeNo;
        AttemptNo = attemptNo;
        AmountMinor = amountMinor;
        Subject = normalizedSubject;
        Status = PaymentChannelOrderStatus.Created;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid ConfigurationId { get; private set; }
    public Guid PaymentAllocationId { get; private set; }
    public PaymentChannelProvider Provider { get; private set; }
    public string OutTradeNo { get; private set; } = string.Empty;
    public int AttemptNo { get; private set; }
    public long AmountMinor { get; private set; }
    public string Currency { get; private set; } = "CNY";
    public string Subject { get; private set; } = string.Empty;
    public PaymentChannelOrderStatus Status { get; private set; }
    public string? QrPayload { get; private set; }
    public string? ProviderTradeNo { get; private set; }
    public string? FailureCode { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? PaidAtUtc { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }
    public DateTimeOffset? LastQueriedAtUtc { get; private set; }

    public void MarkQrReady(string qrPayload, DateTimeOffset now)
    {
        if (Status != PaymentChannelOrderStatus.Created)
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "当前渠道订单不能写入付款码");
        var normalized = Required(qrPayload, 2048, "付款码内容");
        if (now >= ExpiresAtUtc)
            throw new DomainRuleException("CHANNEL_ORDER_EXPIRED", "渠道订单已经过期");
        QrPayload = normalized;
        Status = PaymentChannelOrderStatus.QrReady;
        Touch();
    }

    public void MarkPaid(string providerTradeNo, DateTimeOffset paidAtUtc)
    {
        if (Status == PaymentChannelOrderStatus.Paid)
        {
            if (!string.Equals(ProviderTradeNo, providerTradeNo.Trim(), StringComparison.Ordinal))
                throw new DomainRuleException("CHANNEL_RESULT_CONFLICT", "渠道订单已经由不同交易号确认");
            return;
        }
        if (Status is not (PaymentChannelOrderStatus.Created or PaymentChannelOrderStatus.QrReady))
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "当前渠道订单不能确认为已支付");
        ProviderTradeNo = Required(providerTradeNo, 128, "渠道交易号");
        PaidAtUtc = paidAtUtc;
        Status = PaymentChannelOrderStatus.Paid;
        Touch();
    }

    public void RecordQuery(DateTimeOffset now)
    {
        LastQueriedAtUtc = now;
        Touch();
    }

    public void Close(DateTimeOffset now)
    {
        if (Status is not (PaymentChannelOrderStatus.Created or PaymentChannelOrderStatus.QrReady))
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "当前渠道订单不能关闭");
        Status = PaymentChannelOrderStatus.Closed;
        ClosedAtUtc = now;
        Touch();
    }

    public void Fail(string failureCode)
    {
        if (Status is not (PaymentChannelOrderStatus.Created or PaymentChannelOrderStatus.QrReady))
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "当前渠道订单不能标记失败");
        FailureCode = Required(failureCode, 80, "渠道失败代码");
        Status = PaymentChannelOrderStatus.Failed;
        Touch();
    }

    public void Expire(DateTimeOffset now)
    {
        if (Status is not (PaymentChannelOrderStatus.Created or PaymentChannelOrderStatus.QrReady) ||
            now < ExpiresAtUtc)
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "当前渠道订单不能标记过期");
        Status = PaymentChannelOrderStatus.Expired;
        Touch();
    }

    private static string Required(string value, int max, string field)
    {
        var normalized = value.Trim();
        if (normalized.Length is 0 || normalized.Length > max)
            throw new DomainRuleException("VALIDATION_FAILED", $"{field}长度不正确");
        return normalized;
    }
}

public sealed class PaymentChannelEvent : Entity
{
    private PaymentChannelEvent() { }

    public PaymentChannelEvent(Guid tenantId, Guid configurationId, Guid? channelOrderId,
        PaymentChannelProvider provider, string providerEventId, string eventType, byte[] payloadSha256,
        DateTimeOffset receivedAtUtc) : base(tenantId)
    {
        if (payloadSha256.Length != 32)
            throw new DomainRuleException("VALIDATION_FAILED", "渠道通知摘要必须为SHA-256");
        ConfigurationId = configurationId;
        ChannelOrderId = channelOrderId;
        Provider = provider;
        ProviderEventId = Required(providerEventId, 128, "渠道事件号");
        EventType = Required(eventType, 80, "渠道事件类型");
        PayloadSha256 = payloadSha256.ToArray();
        Status = PaymentChannelEventStatus.Received;
        ReceivedAtUtc = receivedAtUtc;
    }

    public Guid ConfigurationId { get; private set; }
    public Guid? ChannelOrderId { get; private set; }
    public PaymentChannelProvider Provider { get; private set; }
    public string ProviderEventId { get; private set; } = string.Empty;
    public string EventType { get; private set; } = string.Empty;
    public byte[] PayloadSha256 { get; private set; } = [];
    public PaymentChannelEventStatus Status { get; private set; }
    public DateTimeOffset ReceivedAtUtc { get; private set; }
    public DateTimeOffset? ProcessedAtUtc { get; private set; }
    public string? ErrorCode { get; private set; }

    public void Complete(PaymentChannelEventStatus status, DateTimeOffset now, string? errorCode = null)
    {
        if (Status != PaymentChannelEventStatus.Received ||
            status is PaymentChannelEventStatus.Received)
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "渠道事件当前不能完成处理");
        var normalizedError = string.IsNullOrWhiteSpace(errorCode) ? null : errorCode.Trim();
        if (normalizedError?.Length > 80)
            throw new DomainRuleException("VALIDATION_FAILED", "渠道事件错误码不能超过80字");
        if (status == PaymentChannelEventStatus.Failed && normalizedError is null)
            throw new DomainRuleException("VALIDATION_FAILED", "渠道事件处理失败必须记录错误码");
        Status = status;
        ErrorCode = normalizedError;
        ProcessedAtUtc = now;
        Touch();
    }

    private static string Required(string value, int max, string field)
    {
        var normalized = value.Trim();
        if (normalized.Length is 0 || normalized.Length > max)
            throw new DomainRuleException("VALIDATION_FAILED", $"{field}长度不正确");
        return normalized;
    }
}

public sealed class PaymentChannelRefund : Entity
{
    private PaymentChannelRefund() { }

    public PaymentChannelRefund(Guid tenantId, Guid configurationId, Guid refundId,
        Guid originalChannelOrderId, PaymentChannelProvider provider, string outRefundNo,
        string outTradeNo, string providerTradeNo, long amountMinor) : base(tenantId)
    {
        if (amountMinor <= 0 || amountMinor > 10_000_000_000)
            throw new DomainRuleException("VALIDATION_FAILED", "渠道退款金额超出允许范围");
        ConfigurationId = configurationId;
        RefundId = refundId;
        OriginalChannelOrderId = originalChannelOrderId;
        Provider = provider;
        OutRefundNo = Required(outRefundNo, 64, "商户退款单号");
        OutTradeNo = Required(outTradeNo, 64, "原商户订单号");
        ProviderTradeNo = Required(providerTradeNo, 128, "原渠道交易号");
        AmountMinor = amountMinor;
        Status = PaymentChannelRefundStatus.Created;
        ReconciliationStatus = ReconciliationStatus.Pending;
    }

    public Guid ConfigurationId { get; private set; }
    public Guid RefundId { get; private set; }
    public Guid OriginalChannelOrderId { get; private set; }
    public PaymentChannelProvider Provider { get; private set; }
    public string OutRefundNo { get; private set; } = string.Empty;
    public string OutTradeNo { get; private set; } = string.Empty;
    public string ProviderTradeNo { get; private set; } = string.Empty;
    public string? ProviderRefundNo { get; private set; }
    public long AmountMinor { get; private set; }
    public PaymentChannelRefundStatus Status { get; private set; }
    public ReconciliationStatus ReconciliationStatus { get; private set; }
    public string? FailureCode { get; private set; }
    public DateTimeOffset? LastQueriedAtUtc { get; private set; }
    public DateTimeOffset? SucceededAtUtc { get; private set; }

    public void MarkProcessing(string? providerRefundNo)
    {
        if (Status == PaymentChannelRefundStatus.Succeeded) return;
        if (Status is not (PaymentChannelRefundStatus.Created or PaymentChannelRefundStatus.Failed or
            PaymentChannelRefundStatus.Processing))
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "渠道退款当前不能进入处理中");
        ProviderRefundNo = Optional(providerRefundNo, 128, "渠道退款号") ?? ProviderRefundNo;
        FailureCode = null;
        Status = PaymentChannelRefundStatus.Processing;
        Touch();
    }

    public void MarkSucceeded(string? providerRefundNo, DateTimeOffset now)
    {
        var normalized = Optional(providerRefundNo, 128, "渠道退款号");
        if (Status == PaymentChannelRefundStatus.Succeeded)
        {
            if (normalized is not null && ProviderRefundNo is not null &&
                !string.Equals(normalized, ProviderRefundNo, StringComparison.Ordinal))
                throw new DomainRuleException("CHANNEL_RESULT_CONFLICT", "渠道退款已由不同退款号确认");
            return;
        }
        ProviderRefundNo = normalized ?? ProviderRefundNo;
        FailureCode = null;
        Status = PaymentChannelRefundStatus.Succeeded;
        SucceededAtUtc = now;
        Touch();
    }

    public void MarkFailed(string failureCode)
    {
        if (Status == PaymentChannelRefundStatus.Succeeded)
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "已成功退款不能标记失败");
        FailureCode = Required(failureCode, 80, "渠道退款失败代码");
        Status = PaymentChannelRefundStatus.Failed;
        Touch();
    }

    public void RecordQuery(DateTimeOffset now)
    {
        LastQueriedAtUtc = now;
        Touch();
    }

    public void MarkReconciled(ReconciliationStatus status)
    {
        if (status is not (ReconciliationStatus.Matched or ReconciliationStatus.Difference or
                ReconciliationStatus.Resolved))
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "渠道退款对账状态无效");
        if (ReconciliationStatus == status) return;
        ReconciliationStatus = status;
        Touch();
    }

    private static string Required(string value, int max, string field)
    {
        var normalized = value.Trim();
        if (normalized.Length is 0 || normalized.Length > max)
            throw new DomainRuleException("VALIDATION_FAILED", $"{field}长度不正确");
        return normalized;
    }

    private static string? Optional(string? value, int max, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Required(value, max, field);
    }
}
