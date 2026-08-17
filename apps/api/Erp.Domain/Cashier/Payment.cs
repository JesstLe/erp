using Erp.Domain.Common;

namespace Erp.Domain.Cashier;

public enum PaymentMethodCategory { Cash, ManualExternal, InternalAccount }
public enum PaymentStatus { Processing, Paid, Cancelled, ReversalRequired }
public enum PaymentConfirmationStatus { CashRecorded, ManualPendingReconciliation, InternalConfirmed, ChannelConfirmed, Failed, Cancelled }
public enum ReconciliationStatus { NotRequired, Pending, Matched, Difference, Resolved }

public sealed class PaymentMethod : Entity
{
    private PaymentMethod() { }

    public PaymentMethod(Guid tenantId, string code, string name, PaymentMethodCategory category, bool requiresOpenShift)
        : base(tenantId)
    {
        Code = Required(code, 40, "支付方式编号").ToUpperInvariant();
        Name = Required(name, 80, "支付方式名称");
        Category = category;
        RequiresOpenShift = requiresOpenShift;
        IsEnabled = true;
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public PaymentMethodCategory Category { get; private set; }
    public bool RequiresOpenShift { get; private set; }
    public bool IsEnabled { get; private set; }

    private static string Required(string value, int max, string field)
    {
        var normalized = value.Trim();
        if (normalized.Length is 0 || normalized.Length > max) throw new DomainRuleException("VALIDATION_FAILED", $"{field}长度不正确");
        return normalized;
    }
}

public sealed record PaymentAllocationDraft(Guid MethodId, string MethodCode, string MethodName,
    PaymentMethodCategory Category, long AmountMinor, string? ExternalReference, Guid? ShiftId);

public sealed class Payment : Entity
{
    private readonly List<PaymentAllocation> _allocations = [];
    private Payment() { }

    public Payment(Guid tenantId, Guid storeId, Guid orderId, string paymentNo, long receivableMinor,
        IEnumerable<PaymentAllocationDraft> allocations, DateTimeOffset now) : base(tenantId)
    {
        if (receivableMinor < 0) throw new DomainRuleException("VALIDATION_FAILED", "应收金额不能为负数");
        StoreId = storeId;
        OrderId = orderId;
        PaymentNo = Required(paymentNo, 40, "支付单号");
        ReceivableMinor = receivableMinor;
        Status = PaymentStatus.Processing;
        foreach (var draft in allocations)
            _allocations.Add(new PaymentAllocation(tenantId, Id, draft.MethodId, draft.MethodCode, draft.MethodName,
                draft.Category, draft.AmountMinor, draft.ExternalReference, draft.ShiftId, now));
        if (_allocations.Count is 0 or > 20) throw new DomainRuleException("PAYMENT_ALLOCATION_UNBALANCED", "支付分摊需要1到20行");
        PaidMinor = checked(_allocations.Sum(x => x.AmountMinor));
        if (PaidMinor != ReceivableMinor)
            throw new DomainRuleException("PAYMENT_ALLOCATION_UNBALANCED", "支付分摊合计必须等于消费单应收金额");
        Status = PaymentStatus.Paid;
        PaidAtUtc = now;
    }

    public Guid StoreId { get; private set; }
    public Guid OrderId { get; private set; }
    public string PaymentNo { get; private set; } = string.Empty;
    public PaymentStatus Status { get; private set; }
    public string Currency { get; private set; } = "CNY";
    public long ReceivableMinor { get; private set; }
    public long PaidMinor { get; private set; }
    public DateTimeOffset? PaidAtUtc { get; private set; }
    public IReadOnlyCollection<PaymentAllocation> Allocations => _allocations;

    private static string Required(string value, int max, string field)
    {
        var normalized = value.Trim();
        if (normalized.Length is 0 || normalized.Length > max) throw new DomainRuleException("VALIDATION_FAILED", $"{field}长度不正确");
        return normalized;
    }
}

public sealed class PaymentAllocation : Entity
{
    private PaymentAllocation() { }

    internal PaymentAllocation(Guid tenantId, Guid paymentId, Guid methodId, string methodCode, string methodName,
        PaymentMethodCategory category, long amountMinor, string? externalReference, Guid? shiftId, DateTimeOffset now)
        : base(tenantId)
    {
        if (amountMinor <= 0 || amountMinor > 10_000_000_000)
            throw new DomainRuleException("VALIDATION_FAILED", "支付分摊金额必须大于0且不超过允许范围");
        var reference = string.IsNullOrWhiteSpace(externalReference) ? null : externalReference.Trim();
        if (category == PaymentMethodCategory.ManualExternal && reference?.Length is not (>= 4 and <= 100))
            throw new DomainRuleException("VALIDATION_FAILED", "人工登记外部收款必须填写4到100字的交易参考号");
        if (reference?.Length > 100) throw new DomainRuleException("VALIDATION_FAILED", "交易参考号最多100字");
        if (category is PaymentMethodCategory.Cash or PaymentMethodCategory.ManualExternal && shiftId is null)
            throw new DomainRuleException("SHIFT_NOT_OPEN", "现金或人工外部收款必须归入当前班次");
        PaymentId = paymentId;
        MethodId = methodId;
        MethodCodeSnapshot = methodCode.Trim();
        MethodNameSnapshot = methodName.Trim();
        Category = category;
        AmountMinor = amountMinor;
        ExternalReference = reference;
        ShiftId = shiftId;
        ConfirmationStatus = category switch
        {
            PaymentMethodCategory.Cash => PaymentConfirmationStatus.CashRecorded,
            PaymentMethodCategory.ManualExternal => PaymentConfirmationStatus.ManualPendingReconciliation,
            _ => PaymentConfirmationStatus.InternalConfirmed,
        };
        ReconciliationStatus = category == PaymentMethodCategory.ManualExternal
            ? ReconciliationStatus.Pending : ReconciliationStatus.NotRequired;
        ConfirmedAtUtc = now;
    }

    public Guid PaymentId { get; private set; }
    public Guid MethodId { get; private set; }
    public string MethodCodeSnapshot { get; private set; } = string.Empty;
    public string MethodNameSnapshot { get; private set; } = string.Empty;
    public PaymentMethodCategory Category { get; private set; }
    public long AmountMinor { get; private set; }
    public string? ExternalReference { get; private set; }
    public Guid? ShiftId { get; private set; }
    public PaymentConfirmationStatus ConfirmationStatus { get; private set; }
    public ReconciliationStatus ReconciliationStatus { get; private set; }
    public DateTimeOffset ConfirmedAtUtc { get; private set; }
}
