using Erp.Domain.Common;

namespace Erp.Domain.Cashier;

public enum ServiceOrderStatus { Draft, PendingPayment, PaymentProcessing, Settled, PartiallyRefunded, Refunded, Voided }

public sealed class ServiceOrder : Entity
{
    private readonly List<ServiceOrderLine> _lines = [];
    private ServiceOrder() { }

    public ServiceOrder(Guid tenantId, Guid storeId, Guid visitId, Guid? customerId, string orderNo,
        Guid priceBookId, string? note, IEnumerable<ServiceOrderLineDraft> lines)
        : base(tenantId)
    {
        StoreId = storeId;
        VisitId = visitId;
        CustomerId = customerId;
        OrderNo = Required(orderNo, 40, "消费单号");
        PriceBookId = priceBookId;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (Note?.Length > 1000) throw new DomainRuleException("VALIDATION_FAILED", "消费单备注不能超过1000字");
        foreach (var line in lines)
            _lines.Add(new ServiceOrderLine(tenantId, Id, line.ServiceItemId, line.ItemCode, line.ItemName,
                line.Quantity, line.ActualSeconds, line.ReferencePriceMinor, line.EnteredPriceMinor,
                line.PriceOverrideReason));
        if (_lines.Count == 0) throw new DomainRuleException("VALIDATION_FAILED", "消费单至少需要一个服务项目");
        if (_lines.Count > 100) throw new DomainRuleException("VALIDATION_FAILED", "一张消费单最多100行");
        ReferenceAmountMinor = _lines.Sum(x => x.ReferenceAmountMinor);
        ReceivableMinor = _lines.Sum(x => x.LineAmountMinor);
        Status = ServiceOrderStatus.Draft;
    }

    public Guid StoreId { get; private set; }
    public Guid VisitId { get; private set; }
    public Guid? CustomerId { get; private set; }
    public string OrderNo { get; private set; } = string.Empty;
    public Guid PriceBookId { get; private set; }
    public string? Note { get; private set; }
    public ServiceOrderStatus Status { get; private set; }
    public long ReferenceAmountMinor { get; private set; }
    public long ReceivableMinor { get; private set; }
    public DateTimeOffset? ConfirmedAtUtc { get; private set; }
    public DateTimeOffset? SettledAtUtc { get; private set; }
    public long RefundedMinor { get; private set; }
    public IReadOnlyCollection<ServiceOrderLine> Lines => _lines;

    public void Confirm(DateTimeOffset now)
    {
        if (Status != ServiceOrderStatus.Draft) throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "只有草稿消费单可以确认");
        Status = ServiceOrderStatus.PendingPayment;
        ConfirmedAtUtc = now;
        Touch();
    }

    public void BeginCheckout()
    {
        if (Status != ServiceOrderStatus.PendingPayment) throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "消费单当前不能开始结算");
        Status = ServiceOrderStatus.PaymentProcessing;
        Touch();
    }

    public void Settle(DateTimeOffset now)
    {
        if (Status != ServiceOrderStatus.PaymentProcessing) throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "消费单当前不能完成结算");
        Status = ServiceOrderStatus.Settled;
        SettledAtUtc = now;
        Touch();
    }

    public void ApplyRefund(long amountMinor)
    {
        if (Status is not (ServiceOrderStatus.Settled or ServiceOrderStatus.PartiallyRefunded))
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "当前消费单不可退款");
        if (amountMinor <= 0 || RefundedMinor + amountMinor > ReceivableMinor)
            throw new DomainRuleException("REFUND_AMOUNT_EXCEEDED", "退款累计金额不能超过消费单应收金额");
        RefundedMinor += amountMinor;
        Status = RefundedMinor == ReceivableMinor ? ServiceOrderStatus.Refunded : ServiceOrderStatus.PartiallyRefunded;
        Touch();
    }

    private static string Required(string value, int max, string field)
    {
        var normalized = value.Trim();
        if (normalized.Length is 0 || normalized.Length > max) throw new DomainRuleException("VALIDATION_FAILED", $"{field}长度不正确");
        return normalized;
    }
}

public sealed record ServiceOrderLineDraft(Guid ServiceItemId, string ItemCode, string ItemName, int Quantity,
    int? ActualSeconds, long ReferencePriceMinor, long EnteredPriceMinor, string? PriceOverrideReason);

public sealed class ServiceOrderLine : Entity
{
    private ServiceOrderLine() { }

    internal ServiceOrderLine(Guid tenantId, Guid orderId, Guid serviceItemId, string itemCode, string itemName,
        int quantity, int? actualSeconds, long referencePriceMinor, long enteredPriceMinor, string? overrideReason)
        : base(tenantId)
    {
        if (quantity is < 1 or > 999) throw new DomainRuleException("VALIDATION_FAILED", "服务项目数量必须为1到999");
        if (actualSeconds is < 0 or > 86400) throw new DomainRuleException("VALIDATION_FAILED", "项目实际时长必须为0到86400秒");
        if (referencePriceMinor is < 0 or > 10_000_000_000 || enteredPriceMinor is < 0 or > 10_000_000_000)
            throw new DomainRuleException("VALIDATION_FAILED", "服务价格超出允许范围");
        var reason = string.IsNullOrWhiteSpace(overrideReason) ? null : overrideReason.Trim();
        if (enteredPriceMinor != referencePriceMinor && reason?.Length is not (>= 2 and <= 500))
            throw new DomainRuleException("VALIDATION_FAILED", "成交价与标准价不同时必须填写2到500字的改价原因");
        OrderId = orderId;
        ServiceItemId = serviceItemId;
        ItemCodeSnapshot = itemCode.Trim();
        ItemNameSnapshot = itemName.Trim();
        if (ItemCodeSnapshot.Length is 0 or > 40 || ItemNameSnapshot.Length is 0 or > 120)
            throw new DomainRuleException("VALIDATION_FAILED", "项目快照无效");
        Quantity = quantity;
        ActualSeconds = actualSeconds;
        ReferencePriceMinor = referencePriceMinor;
        EnteredPriceMinor = enteredPriceMinor;
        PriceOverrideReason = reason;
        ReferenceAmountMinor = checked(referencePriceMinor * quantity);
        LineAmountMinor = checked(enteredPriceMinor * quantity);
    }

    public Guid OrderId { get; private set; }
    public Guid ServiceItemId { get; private set; }
    public string ItemCodeSnapshot { get; private set; } = string.Empty;
    public string ItemNameSnapshot { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public int? ActualSeconds { get; private set; }
    public long ReferencePriceMinor { get; private set; }
    public long EnteredPriceMinor { get; private set; }
    public long ReferenceAmountMinor { get; private set; }
    public long LineAmountMinor { get; private set; }
    public string? PriceOverrideReason { get; private set; }
}
