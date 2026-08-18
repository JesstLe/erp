using Erp.Domain.Common;
using Erp.Domain.Catalog;

namespace Erp.Domain.Cashier;

public enum ServiceOrderStatus { Draft, PendingPayment, PaymentProcessing, Settled, PartiallyRefunded, Refunded, Voided }
public enum ServiceOrderLineType { Service, Product }

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
            _lines.Add(new ServiceOrderLine(tenantId, Id, line));
        if (_lines.Count == 0) throw new DomainRuleException("VALIDATION_FAILED", "消费单至少需要一个项目或产品");
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

    public void Void()
    {
        if (Status is not (ServiceOrderStatus.Draft or ServiceOrderStatus.PendingPayment))
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "只有草稿或待支付消费单可以作废");
        Status = ServiceOrderStatus.Voided;
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

    public void CancelCheckout()
    {
        if (Status != ServiceOrderStatus.PaymentProcessing)
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "消费单当前不能取消支付处理");
        Status = ServiceOrderStatus.PendingPayment;
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

public sealed record ServiceOrderLineDraft
{
    public ServiceOrderLineDraft(Guid serviceItemId, string itemCode, string itemName, int quantity,
        int? actualSeconds, long referencePriceMinor, long enteredPriceMinor, string? priceOverrideReason,
        Guid? serviceEmployeeId = null, string? employeeNo = null, string? employeeName = null,
        CommissionMode commissionMode = CommissionMode.None, int? commissionRateBasisPoints = null,
        long? commissionFixedMinor = null)
    {
        LineType = ServiceOrderLineType.Service;
        ServiceItemId = serviceItemId;
        ItemCode = itemCode;
        ItemName = itemName;
        Quantity = quantity;
        ActualSeconds = actualSeconds;
        ReferencePriceMinor = referencePriceMinor;
        EnteredPriceMinor = enteredPriceMinor;
        PriceOverrideReason = priceOverrideReason;
        ServiceEmployeeId = serviceEmployeeId;
        EmployeeNo = employeeNo;
        EmployeeName = employeeName;
        CommissionMode = commissionMode;
        CommissionRateBasisPoints = commissionRateBasisPoints;
        CommissionFixedMinor = commissionFixedMinor;
    }

    private ServiceOrderLineDraft(Guid productItemId, string itemCode, string itemName, string unitName,
        int quantity, long referencePriceMinor, long enteredPriceMinor, string? priceOverrideReason)
    {
        LineType = ServiceOrderLineType.Product;
        ProductItemId = productItemId;
        ItemCode = itemCode;
        ItemName = itemName;
        UnitName = unitName;
        Quantity = quantity;
        ReferencePriceMinor = referencePriceMinor;
        EnteredPriceMinor = enteredPriceMinor;
        PriceOverrideReason = priceOverrideReason;
    }

    public static ServiceOrderLineDraft Product(Guid productItemId, string itemCode, string itemName,
        string unitName, int quantity, long referencePriceMinor, long enteredPriceMinor,
        string? priceOverrideReason) => new(productItemId, itemCode, itemName, unitName, quantity,
        referencePriceMinor, enteredPriceMinor, priceOverrideReason);

    public ServiceOrderLineType LineType { get; }
    public Guid? ServiceItemId { get; }
    public Guid? ProductItemId { get; }
    public string ItemCode { get; }
    public string ItemName { get; }
    public string? UnitName { get; }
    public int Quantity { get; }
    public int? ActualSeconds { get; }
    public long ReferencePriceMinor { get; }
    public long EnteredPriceMinor { get; }
    public string? PriceOverrideReason { get; }
    public Guid? ServiceEmployeeId { get; }
    public string? EmployeeNo { get; }
    public string? EmployeeName { get; }
    public CommissionMode CommissionMode { get; }
    public int? CommissionRateBasisPoints { get; }
    public long? CommissionFixedMinor { get; }
}

public sealed class ServiceOrderLine : Entity
{
    private ServiceOrderLine() { }

    internal ServiceOrderLine(Guid tenantId, Guid orderId, ServiceOrderLineDraft draft)
        : base(tenantId)
    {
        var quantity = draft.Quantity;
        var actualSeconds = draft.ActualSeconds;
        var referencePriceMinor = draft.ReferencePriceMinor;
        var enteredPriceMinor = draft.EnteredPriceMinor;
        var overrideReason = draft.PriceOverrideReason;
        if (quantity is < 1 or > 999) throw new DomainRuleException("VALIDATION_FAILED", "消费项目数量必须为1到999");
        if (actualSeconds is < 0 or > 86400) throw new DomainRuleException("VALIDATION_FAILED", "项目实际时长必须为0到86400秒");
        if (draft.LineType == ServiceOrderLineType.Product && actualSeconds is not null)
            throw new DomainRuleException("VALIDATION_FAILED", "产品明细不能填写服务时长");
        if ((draft.LineType == ServiceOrderLineType.Service) != draft.ServiceItemId.HasValue ||
            (draft.LineType == ServiceOrderLineType.Product) != draft.ProductItemId.HasValue)
            throw new DomainRuleException("VALIDATION_FAILED", "消费明细类型与目录项目不一致");
        if (referencePriceMinor is < 0 or > 10_000_000_000 || enteredPriceMinor is < 0 or > 10_000_000_000)
            throw new DomainRuleException("VALIDATION_FAILED", "项目价格超出允许范围");
        var reason = string.IsNullOrWhiteSpace(overrideReason) ? null : overrideReason.Trim();
        if (enteredPriceMinor != referencePriceMinor && reason?.Length is not (>= 2 and <= 500))
            throw new DomainRuleException("VALIDATION_FAILED", "成交价与标准价不同时必须填写2到500字的改价原因");
        OrderId = orderId;
        LineType = draft.LineType;
        ServiceItemId = draft.ServiceItemId;
        ProductItemId = draft.ProductItemId;
        ItemCodeSnapshot = draft.ItemCode.Trim();
        ItemNameSnapshot = draft.ItemName.Trim();
        UnitNameSnapshot = string.IsNullOrWhiteSpace(draft.UnitName) ? null : draft.UnitName.Trim();
        if (ItemCodeSnapshot.Length is 0 or > 40 || ItemNameSnapshot.Length is 0 or > 120)
            throw new DomainRuleException("VALIDATION_FAILED", "项目快照无效");
        if (LineType == ServiceOrderLineType.Product && UnitNameSnapshot?.Length is not (>= 1 and <= 20))
            throw new DomainRuleException("VALIDATION_FAILED", "产品计量单位快照无效");
        Quantity = quantity;
        ActualSeconds = actualSeconds;
        ReferencePriceMinor = referencePriceMinor;
        EnteredPriceMinor = enteredPriceMinor;
        PriceOverrideReason = reason;
        ReferenceAmountMinor = checked(referencePriceMinor * quantity);
        LineAmountMinor = checked(enteredPriceMinor * quantity);
        SetCommissionSnapshot(draft);
    }

    public Guid OrderId { get; private set; }
    public ServiceOrderLineType LineType { get; private set; }
    public Guid? ServiceItemId { get; private set; }
    public Guid? ProductItemId { get; private set; }
    public string ItemCodeSnapshot { get; private set; } = string.Empty;
    public string ItemNameSnapshot { get; private set; } = string.Empty;
    public string? UnitNameSnapshot { get; private set; }
    public int Quantity { get; private set; }
    public int? ActualSeconds { get; private set; }
    public long ReferencePriceMinor { get; private set; }
    public long EnteredPriceMinor { get; private set; }
    public long ReferenceAmountMinor { get; private set; }
    public long LineAmountMinor { get; private set; }
    public string? PriceOverrideReason { get; private set; }
    public int ReturnedQuantity { get; private set; }
    public Guid? ServiceEmployeeId { get; private set; }
    public string? EmployeeNoSnapshot { get; private set; }
    public string? EmployeeNameSnapshot { get; private set; }
    public CommissionMode CommissionModeSnapshot { get; private set; }
    public int? CommissionRateBasisPoints { get; private set; }
    public long? CommissionFixedMinor { get; private set; }
    public long CommissionBasisMinor { get; private set; }
    public long CommissionAmountMinor { get; private set; }

    public void ApplyProductReturn(int quantity)
    {
        if (LineType != ServiceOrderLineType.Product || quantity <= 0 || ReturnedQuantity + quantity > Quantity)
            throw new DomainRuleException("PRODUCT_RETURN_QUANTITY_EXCEEDED", "退货数量必须大于0且累计不超过原销售数量");
        ReturnedQuantity += quantity;
        Touch();
    }

    private void SetCommissionSnapshot(ServiceOrderLineDraft draft)
    {
        if (LineType == ServiceOrderLineType.Product)
        {
            if (draft.ServiceEmployeeId.HasValue || draft.CommissionMode != CommissionMode.None)
                throw new DomainRuleException("VALIDATION_FAILED", "商品明细不能填写服务员工或服务提成");
            CommissionModeSnapshot = CommissionMode.None;
            return;
        }

        var employeeNo = string.IsNullOrWhiteSpace(draft.EmployeeNo) ? null : draft.EmployeeNo.Trim();
        var employeeName = string.IsNullOrWhiteSpace(draft.EmployeeName) ? null : draft.EmployeeName.Trim();
        if (draft.ServiceEmployeeId.HasValue != (employeeNo is not null && employeeName is not null) ||
            employeeNo?.Length > 32 || employeeName?.Length > 100)
            throw new DomainRuleException("VALIDATION_FAILED", "服务员工快照无效");
        if (draft.CommissionMode != CommissionMode.None && !draft.ServiceEmployeeId.HasValue)
            throw new DomainRuleException("SERVICE_EMPLOYEE_REQUIRED", "该服务项目已设置提成，必须选择服务员工");

        ServiceEmployeeId = draft.ServiceEmployeeId;
        EmployeeNoSnapshot = employeeNo;
        EmployeeNameSnapshot = employeeName;
        CommissionModeSnapshot = draft.CommissionMode;
        CommissionRateBasisPoints = draft.CommissionRateBasisPoints;
        CommissionFixedMinor = draft.CommissionFixedMinor;
        CommissionBasisMinor = LineAmountMinor;
        CommissionAmountMinor = draft.CommissionMode switch
        {
            CommissionMode.None when draft.CommissionRateBasisPoints is null && draft.CommissionFixedMinor is null => 0,
            CommissionMode.Percentage when draft.CommissionRateBasisPoints is >= 1 and <= 10_000 &&
                draft.CommissionFixedMinor is null => checked((LineAmountMinor * draft.CommissionRateBasisPoints.Value + 5_000) / 10_000),
            CommissionMode.FixedAmount when draft.CommissionFixedMinor is >= 1 and <= 10_000_000_000 &&
                draft.CommissionRateBasisPoints is null => checked(draft.CommissionFixedMinor.Value * Quantity),
            _ => throw new DomainRuleException("VALIDATION_FAILED", "服务提成快照无效"),
        };
        if (CommissionAmountMinor > CommissionBasisMinor)
            throw new DomainRuleException("COMMISSION_EXCEEDS_LINE_AMOUNT", "提成金额不能超过该服务明细的成交金额");
    }
}
