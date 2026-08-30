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
        Guid priceBookId, string? note, IEnumerable<ServiceOrderLineDraft> lines,
        Guid? consultantEmployeeId = null, string? consultantEmployeeNo = null,
        string? consultantEmployeeName = null, ServiceOrderReceptionDraft? reception = null)
        : base(tenantId)
    {
        StoreId = storeId;
        VisitId = visitId;
        CustomerId = customerId;
        OrderNo = Required(orderNo, 40, "消费单号");
        PriceBookId = priceBookId;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (Note?.Length > 1000) throw new DomainRuleException("VALIDATION_FAILED", "消费单备注不能超过1000字");
        ReplaceLines(lines);
        SetConsultant(consultantEmployeeId, consultantEmployeeNo, consultantEmployeeName);
        SetReception(reception);
        Status = ServiceOrderStatus.Draft;
        PriceAuthorizationStatus = PriceAuthorizationState.NotRequired;
    }

    public Guid StoreId { get; private set; }
    public Guid VisitId { get; private set; }
    public Guid? CustomerId { get; private set; }
    public Guid? ConsultantEmployeeId { get; private set; }
    public string? ConsultantEmployeeNoSnapshot { get; private set; }
    public string? ConsultantEmployeeNameSnapshot { get; private set; }
    public string? SourceChannel { get; private set; }
    public string? ManualTicketNo { get; private set; }
    public int MaleGuestCount { get; private set; }
    public string? MaleAgeBand { get; private set; }
    public int FemaleGuestCount { get; private set; }
    public string? FemaleAgeBand { get; private set; }
    public string OrderNo { get; private set; } = string.Empty;
    public Guid? PriceBookId { get; private set; }
    public string? Note { get; private set; }
    public ServiceOrderStatus Status { get; private set; }
    public long ReferenceAmountMinor { get; private set; }
    public long ReceivableMinor { get; private set; }
    public DateTimeOffset? ConfirmedAtUtc { get; private set; }
    public DateTimeOffset? SettledAtUtc { get; private set; }
    public long RefundedMinor { get; private set; }
    public PriceAuthorizationState PriceAuthorizationStatus { get; private set; }
    public Guid? PricePolicyId { get; private set; }
    public int? PricePolicyVersion { get; private set; }
    public Guid? PriceAuthorizedBy { get; private set; }
    public DateTimeOffset? PriceAuthorizedAtUtc { get; private set; }
    public IReadOnlyCollection<ServiceOrderLine> Lines => _lines;
    public bool HasPriceOverride => _lines.Any(x => x.EnteredPriceMinor != x.ReferencePriceMinor);
    public long TotalDiscountMinor => _lines.Sum(x => Math.Max(0, x.ReferenceAmountMinor - x.LineAmountMinor));
    public int MaximumLineDiscountBasisPoints => _lines
        .Where(x => x.EnteredPriceMinor < x.ReferencePriceMinor && x.ReferencePriceMinor > 0)
        .Select(x => (int)Math.Ceiling((x.ReferencePriceMinor - x.EnteredPriceMinor) * 10_000m /
            x.ReferencePriceMinor)).DefaultIfEmpty(0).Max();

    public void AuthorizePriceDirectly(Guid policyId, int policyVersion, Guid authorizedBy, DateTimeOffset now)
    {
        EnsureDraftOverride();
        if (policyId == Guid.Empty || policyVersion < 1 || authorizedBy == Guid.Empty)
            throw new DomainRuleException("VALIDATION_FAILED", "改价授权快照无效");
        PricePolicyId = policyId;
        PricePolicyVersion = policyVersion;
        PriceAuthorizationStatus = PriceAuthorizationState.DirectAuthorized;
        PriceAuthorizedBy = authorizedBy;
        PriceAuthorizedAtUtc = now;
        Touch();
    }

    public void RequestPriceApproval(Guid policyId, int policyVersion)
    {
        EnsureDraftOverride();
        if (policyId == Guid.Empty || policyVersion < 1)
            throw new DomainRuleException("VALIDATION_FAILED", "改价审批策略快照无效");
        PricePolicyId = policyId;
        PricePolicyVersion = policyVersion;
        PriceAuthorizationStatus = PriceAuthorizationState.PendingApproval;
        PriceAuthorizedBy = null;
        PriceAuthorizedAtUtc = null;
        Touch();
    }

    public void ApprovePriceOverride(Guid authorizedBy, DateTimeOffset now)
    {
        if (Status != ServiceOrderStatus.Draft || PriceAuthorizationStatus != PriceAuthorizationState.PendingApproval)
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "当前消费单没有待审批改价");
        if (authorizedBy == Guid.Empty)
            throw new DomainRuleException("VALIDATION_FAILED", "改价审批人无效");
        PriceAuthorizationStatus = PriceAuthorizationState.Approved;
        PriceAuthorizedBy = authorizedBy;
        PriceAuthorizedAtUtc = now;
        Touch();
    }

    public void RejectPriceOverride()
    {
        if (Status != ServiceOrderStatus.Draft || PriceAuthorizationStatus != PriceAuthorizationState.PendingApproval)
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "当前消费单没有待审批改价");
        PriceAuthorizationStatus = PriceAuthorizationState.Rejected;
        Touch();
    }

    public void Confirm(DateTimeOffset now)
    {
        if (Status != ServiceOrderStatus.Draft) throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "只有草稿消费单可以确认");
        if (_lines.Count == 0) throw new DomainRuleException("VALIDATION_FAILED", "消费单至少需要一个项目或产品");
        if (HasPriceOverride && PriceAuthorizationStatus is not (PriceAuthorizationState.DirectAuthorized or
            PriceAuthorizationState.Approved))
            throw new DomainRuleException("PRICE_APPROVAL_REQUIRED", "成交价尚未获得有效授权，不能确认收款金额");
        Status = ServiceOrderStatus.PendingPayment;
        ConfirmedAtUtc = now;
        Touch();
    }

    public void Void()
    {
        if (Status is not (ServiceOrderStatus.Draft or ServiceOrderStatus.PendingPayment))
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "只有草稿或待支付消费单可以作废");
        Status = ServiceOrderStatus.Voided;
        if (PriceAuthorizationStatus == PriceAuthorizationState.PendingApproval)
            PriceAuthorizationStatus = PriceAuthorizationState.Cancelled;
        Touch();
    }

    private void EnsureDraftOverride()
    {
        if (Status != ServiceOrderStatus.Draft || !HasPriceOverride ||
            PriceAuthorizationStatus != PriceAuthorizationState.NotRequired)
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "当前消费单不能设置改价授权");
    }

    public void ReplaceDraft(Guid? customerId, string? note, IEnumerable<ServiceOrderLineDraft> lines,
        Guid? consultantEmployeeId = null, string? consultantEmployeeNo = null,
        string? consultantEmployeeName = null, ServiceOrderReceptionDraft? reception = null)
    {
        if (Status != ServiceOrderStatus.Draft)
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "只有草稿消费单可以编辑");
        CustomerId = customerId;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (Note?.Length > 1000)
            throw new DomainRuleException("VALIDATION_FAILED", "消费单备注不能超过1000字");
        _lines.Clear();
        ReplaceLines(lines);
        SetConsultant(consultantEmployeeId, consultantEmployeeNo, consultantEmployeeName);
        SetReception(reception);
        PriceAuthorizationStatus = PriceAuthorizationState.NotRequired;
        PricePolicyId = null;
        PricePolicyVersion = null;
        PriceAuthorizedBy = null;
        PriceAuthorizedAtUtc = null;
        Touch();
    }

    private void ReplaceLines(IEnumerable<ServiceOrderLineDraft> lines)
    {
        foreach (var line in lines)
            _lines.Add(new ServiceOrderLine(TenantId, Id, line));
        if (_lines.Count > 100)
            throw new DomainRuleException("VALIDATION_FAILED", "一张消费单最多100行");
        ReferenceAmountMinor = _lines.Sum(x => x.ReferenceAmountMinor);
        ReceivableMinor = _lines.Sum(x => x.LineAmountMinor);
    }

    private void SetConsultant(Guid? consultantEmployeeId, string? employeeNo, string? employeeName)
    {
        var normalizedNo = string.IsNullOrWhiteSpace(employeeNo) ? null : employeeNo.Trim();
        var normalizedName = string.IsNullOrWhiteSpace(employeeName) ? null : employeeName.Trim();
        if (consultantEmployeeId.HasValue != (normalizedNo is not null && normalizedName is not null) ||
            normalizedNo?.Length > 32 || normalizedName?.Length > 100)
            throw new DomainRuleException("VALIDATION_FAILED", "整单顾问快照无效");
        ConsultantEmployeeId = consultantEmployeeId;
        ConsultantEmployeeNoSnapshot = normalizedNo;
        ConsultantEmployeeNameSnapshot = normalizedName;
    }

    private void SetReception(ServiceOrderReceptionDraft? reception)
    {
        reception ??= new ServiceOrderReceptionDraft(null, null, 0, null, 0, null);
        if (reception.MaleGuestCount is < 0 or > 99 || reception.FemaleGuestCount is < 0 or > 99)
            throw new DomainRuleException("VALIDATION_FAILED", "顾客人数必须在0到99之间");
        SourceChannel = Optional(reception.SourceChannel, 80, "来店渠道");
        ManualTicketNo = Optional(reception.ManualTicketNo, 80, "手工票号");
        MaleGuestCount = reception.MaleGuestCount;
        FemaleGuestCount = reception.FemaleGuestCount;
        MaleAgeBand = reception.MaleGuestCount == 0 ? null : Optional(reception.MaleAgeBand, 32, "男客年龄段");
        FemaleAgeBand = reception.FemaleGuestCount == 0 ? null : Optional(reception.FemaleAgeBand, 32, "女客年龄段");
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

    private static string? Optional(string? value, int max, string field)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)) return null;
        if (normalized.Length > max) throw new DomainRuleException("VALIDATION_FAILED", $"{field}不能超过{max}字");
        return normalized;
    }
}

public sealed record ServiceOrderReceptionDraft(string? SourceChannel, string? ManualTicketNo,
    int MaleGuestCount, string? MaleAgeBand, int FemaleGuestCount, string? FemaleAgeBand);

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
        int quantity, long referencePriceMinor, long enteredPriceMinor, string? priceOverrideReason,
        Guid? addedByEmployeeId, string? employeeNo, string? employeeName)
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
        ServiceEmployeeId = addedByEmployeeId;
        EmployeeNo = employeeNo;
        EmployeeName = employeeName;
    }

    public static ServiceOrderLineDraft Product(Guid productItemId, string itemCode, string itemName,
        string unitName, int quantity, long referencePriceMinor, long enteredPriceMinor,
        string? priceOverrideReason, Guid? addedByEmployeeId = null, string? employeeNo = null,
        string? employeeName = null) => new(productItemId, itemCode, itemName, unitName, quantity,
        referencePriceMinor, enteredPriceMinor, priceOverrideReason, addedByEmployeeId, employeeNo,
        employeeName);

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
        var employeeNo = string.IsNullOrWhiteSpace(draft.EmployeeNo) ? null : draft.EmployeeNo.Trim();
        var employeeName = string.IsNullOrWhiteSpace(draft.EmployeeName) ? null : draft.EmployeeName.Trim();
        if (draft.ServiceEmployeeId.HasValue != (employeeNo is not null && employeeName is not null) ||
            employeeNo?.Length > 32 || employeeName?.Length > 100)
            throw new DomainRuleException("VALIDATION_FAILED", "员工归属快照无效");

        if (LineType == ServiceOrderLineType.Product)
        {
            if (draft.CommissionMode != CommissionMode.None || draft.CommissionRateBasisPoints is not null ||
                draft.CommissionFixedMinor is not null)
                throw new DomainRuleException("VALIDATION_FAILED", "商品明细不能填写服务提成");
            ServiceEmployeeId = draft.ServiceEmployeeId;
            EmployeeNoSnapshot = employeeNo;
            EmployeeNameSnapshot = employeeName;
            CommissionModeSnapshot = CommissionMode.None;
            return;
        }

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
