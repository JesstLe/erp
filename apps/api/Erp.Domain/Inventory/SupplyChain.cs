using Erp.Domain.Common;

namespace Erp.Domain.Inventory;

public enum SupplierStatus { Active, Disabled }
public enum StocktakeStatus { PendingApproval, Posted, Cancelled }
public enum InventoryTransferStatus { Requested, InTransit, Received, Cancelled }

public sealed class Supplier : Entity
{
    private Supplier() { }

    public Supplier(Guid tenantId, string code, string name, string? contactName, string? mobile,
        string? settlementTerms) : base(tenantId)
    {
        Code = Required(code, 40, "供应商编码").ToUpperInvariant();
        Update(name, contactName, mobile, settlementTerms);
        Status = SupplierStatus.Active;
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? ContactName { get; private set; }
    public string? Mobile { get; private set; }
    public string? SettlementTerms { get; private set; }
    public SupplierStatus Status { get; private set; }

    public void Update(string name, string? contactName, string? mobile, string? settlementTerms)
    {
        Name = Required(name, 120, "供应商名称");
        ContactName = Optional(contactName, 80, "联系人");
        Mobile = Optional(mobile, 32, "联系电话");
        SettlementTerms = Optional(settlementTerms, 500, "结算条款");
        Touch();
    }

    public void ChangeStatus(bool enable)
    {
        Status = enable ? SupplierStatus.Active : SupplierStatus.Disabled;
        Touch();
    }

    private static string Required(string value, int maximum, string field)
    {
        var normalized = value.Trim();
        if (normalized.Length is 0 || normalized.Length > maximum)
            throw new DomainRuleException("VALIDATION_FAILED", $"{field}长度不正确");
        return normalized;
    }

    private static string? Optional(string? value, int maximum, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > maximum)
            throw new DomainRuleException("VALIDATION_FAILED", $"{field}不能超过{maximum}字");
        return normalized;
    }
}

public sealed class InventoryLot : Entity
{
    private InventoryLot() { }

    public InventoryLot(Guid tenantId, Guid storeId, Guid productItemId, string batchNo,
        DateOnly? expiresOn, long unitCostMinor, int quantity, string sourceType, Guid sourceLineId)
        : base(tenantId)
    {
        if (quantity is < 1 or > 1_000_000_000)
            throw new DomainRuleException("VALIDATION_FAILED", "批次数量必须为正整数");
        if (unitCostMinor is < 0 or > 10_000_000_000)
            throw new DomainRuleException("VALIDATION_FAILED", "单位成本超出允许范围");
        StoreId = storeId;
        ProductItemId = productItemId;
        BatchNo = Required(batchNo, 80, "批次号").ToUpperInvariant();
        ExpiresOn = expiresOn;
        UnitCostMinor = unitCostMinor;
        OriginalQuantity = quantity;
        RemainingQuantity = quantity;
        SourceType = Required(sourceType, 40, "批次来源");
        SourceLineId = sourceLineId;
    }

    public Guid StoreId { get; private set; }
    public Guid ProductItemId { get; private set; }
    public string BatchNo { get; private set; } = string.Empty;
    public DateOnly? ExpiresOn { get; private set; }
    public long UnitCostMinor { get; private set; }
    public int OriginalQuantity { get; private set; }
    public int RemainingQuantity { get; private set; }
    public string SourceType { get; private set; } = string.Empty;
    public Guid SourceLineId { get; private set; }

    public void Issue(int quantity)
    {
        if (quantity < 1 || quantity > RemainingQuantity)
            throw new DomainRuleException("INVENTORY_LOT_INSUFFICIENT", "批次剩余数量不足");
        RemainingQuantity -= quantity;
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

public sealed class InventoryLotAllocation : Entity
{
    private InventoryLotAllocation() { }

    public InventoryLotAllocation(Guid tenantId, Guid movementId, Guid lotId, int quantity)
        : base(tenantId)
    {
        if (quantity <= 0) throw new DomainRuleException("VALIDATION_FAILED", "批次分摊数量必须大于0");
        MovementId = movementId;
        LotId = lotId;
        Quantity = quantity;
    }

    public Guid MovementId { get; private set; }
    public Guid LotId { get; private set; }
    public int Quantity { get; private set; }
}

public sealed class PurchaseReceipt : Entity
{
    private readonly List<PurchaseReceiptLine> _lines = [];
    private PurchaseReceipt() { }

    public PurchaseReceipt(Guid tenantId, Guid storeId, Guid supplierId, string receiptNo,
        string? externalNo, string note, Guid postedBy, DateTimeOffset postedAtUtc,
        IEnumerable<(Guid ProductItemId, int Quantity, long UnitCostMinor, string BatchNo,
            DateOnly? ExpiresOn)> lines) : base(tenantId)
    {
        StoreId = storeId;
        SupplierId = supplierId;
        ReceiptNo = Required(receiptNo, 40, "采购入库单号");
        ExternalNo = Optional(externalNo, 80, "供应商单号");
        Note = Required(note, 500, "采购入库说明");
        PostedBy = postedBy;
        PostedAtUtc = postedAtUtc;
        foreach (var line in lines)
            _lines.Add(new PurchaseReceiptLine(tenantId, Id, line.ProductItemId, line.Quantity,
                line.UnitCostMinor, line.BatchNo, line.ExpiresOn));
        if (_lines.Count is 0 or > 100)
            throw new DomainRuleException("VALIDATION_FAILED", "采购入库需要1到100行产品");
        if (_lines.Select(x => new { x.ProductItemId, x.BatchNo }).Distinct().Count() != _lines.Count)
            throw new DomainRuleException("VALIDATION_FAILED", "同一采购入库单不能重复产品批次");
    }

    public Guid StoreId { get; private set; }
    public Guid SupplierId { get; private set; }
    public string ReceiptNo { get; private set; } = string.Empty;
    public string? ExternalNo { get; private set; }
    public string Note { get; private set; } = string.Empty;
    public Guid PostedBy { get; private set; }
    public DateTimeOffset PostedAtUtc { get; private set; }
    public IReadOnlyCollection<PurchaseReceiptLine> Lines => _lines;
    public long TotalCostMinor => _lines.Sum(x => x.LineCostMinor);

    private static string Required(string value, int maximum, string field)
    {
        var normalized = value.Trim();
        if (normalized.Length is 0 || normalized.Length > maximum)
            throw new DomainRuleException("VALIDATION_FAILED", $"{field}长度不正确");
        return normalized;
    }

    private static string? Optional(string? value, int maximum, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > maximum)
            throw new DomainRuleException("VALIDATION_FAILED", $"{field}不能超过{maximum}字");
        return normalized;
    }
}

public sealed class PurchaseReceiptLine : Entity
{
    private PurchaseReceiptLine() { }

    internal PurchaseReceiptLine(Guid tenantId, Guid receiptId, Guid productItemId, int quantity,
        long unitCostMinor, string batchNo, DateOnly? expiresOn) : base(tenantId)
    {
        if (quantity is < 1 or > 1_000_000_000 || unitCostMinor is < 0 or > 10_000_000_000)
            throw new DomainRuleException("VALIDATION_FAILED", "采购数量或单位成本无效");
        ReceiptId = receiptId;
        ProductItemId = productItemId;
        Quantity = quantity;
        UnitCostMinor = unitCostMinor;
        BatchNo = batchNo.Trim().ToUpperInvariant();
        if (BatchNo.Length is 0 or > 80)
            throw new DomainRuleException("VALIDATION_FAILED", "批次号长度不正确");
        ExpiresOn = expiresOn;
        _ = checked((long)quantity * unitCostMinor);
    }

    public Guid ReceiptId { get; private set; }
    public Guid ProductItemId { get; private set; }
    public int Quantity { get; private set; }
    public long UnitCostMinor { get; private set; }
    public long LineCostMinor => checked((long)Quantity * UnitCostMinor);
    public string BatchNo { get; private set; } = string.Empty;
    public DateOnly? ExpiresOn { get; private set; }
}

public sealed class Stocktake : Entity
{
    private readonly List<StocktakeLine> _lines = [];
    private Stocktake() { }

    public Stocktake(Guid tenantId, Guid storeId, string stocktakeNo, string reason, Guid requestedBy,
        DateTimeOffset frozenAtUtc, IEnumerable<(Guid ProductItemId, int BookQuantity,
            int CountedQuantity)> lines) : base(tenantId)
    {
        StoreId = storeId;
        StocktakeNo = Required(stocktakeNo, 40, "盘点单号");
        Reason = Required(reason, 500, "盘点原因");
        RequestedBy = requestedBy;
        FrozenAtUtc = frozenAtUtc;
        foreach (var line in lines)
            _lines.Add(new StocktakeLine(tenantId, Id, line.ProductItemId, line.BookQuantity,
                line.CountedQuantity));
        if (_lines.Count is 0 or > 500 || _lines.Select(x => x.ProductItemId).Distinct().Count() != _lines.Count)
            throw new DomainRuleException("VALIDATION_FAILED", "盘点单需要1到500行且不能重复产品");
        Status = StocktakeStatus.PendingApproval;
    }

    public Guid StoreId { get; private set; }
    public string StocktakeNo { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public Guid RequestedBy { get; private set; }
    public DateTimeOffset FrozenAtUtc { get; private set; }
    public StocktakeStatus Status { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    public DateTimeOffset? PostedAtUtc { get; private set; }
    public string? DecisionReason { get; private set; }
    public IReadOnlyCollection<StocktakeLine> Lines => _lines;

    public void Approve(Guid approverId, string reason, DateTimeOffset now)
    {
        if (Status != StocktakeStatus.PendingApproval)
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "盘点单当前不能审批");
        if (RequestedBy == approverId)
            throw new DomainRuleException("FORBIDDEN_ACTION", "盘点申请人与审批人必须分离");
        ApprovedBy = approverId;
        DecisionReason = Required(reason, 500, "审批说明");
        PostedAtUtc = now;
        Status = StocktakeStatus.Posted;
        Touch();
    }

    public void Cancel(Guid operatorId, string reason)
    {
        if (Status != StocktakeStatus.PendingApproval)
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "盘点单当前不能取消");
        ApprovedBy = operatorId;
        DecisionReason = Required(reason, 500, "取消原因");
        Status = StocktakeStatus.Cancelled;
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

public sealed class StocktakeLine : Entity
{
    private StocktakeLine() { }

    internal StocktakeLine(Guid tenantId, Guid stocktakeId, Guid productItemId, int bookQuantity,
        int countedQuantity) : base(tenantId)
    {
        if (bookQuantity < 0 || countedQuantity < 0)
            throw new DomainRuleException("VALIDATION_FAILED", "账面数量和盘点数量不能为负");
        StocktakeId = stocktakeId;
        ProductItemId = productItemId;
        BookQuantity = bookQuantity;
        CountedQuantity = countedQuantity;
        DifferenceQuantity = checked(countedQuantity - bookQuantity);
    }

    public Guid StocktakeId { get; private set; }
    public Guid ProductItemId { get; private set; }
    public int BookQuantity { get; private set; }
    public int CountedQuantity { get; private set; }
    public int DifferenceQuantity { get; private set; }
}

public sealed class InventoryTransfer : Entity
{
    private readonly List<InventoryTransferLine> _lines = [];
    private InventoryTransfer() { }

    public InventoryTransfer(Guid tenantId, Guid sourceStoreId, Guid destinationStoreId,
        string transferNo, string reason, Guid requestedBy, DateTimeOffset requestedAtUtc,
        IEnumerable<(Guid ProductItemId, int Quantity)> lines) : base(tenantId)
    {
        if (sourceStoreId == destinationStoreId)
            throw new DomainRuleException("VALIDATION_FAILED", "调出和调入门店不能相同");
        SourceStoreId = sourceStoreId;
        DestinationStoreId = destinationStoreId;
        TransferNo = Required(transferNo, 40, "调拨单号");
        Reason = Required(reason, 500, "调拨原因");
        RequestedBy = requestedBy;
        RequestedAtUtc = requestedAtUtc;
        foreach (var line in lines)
            _lines.Add(new InventoryTransferLine(tenantId, Id, line.ProductItemId, line.Quantity));
        if (_lines.Count is 0 or > 100 || _lines.Select(x => x.ProductItemId).Distinct().Count() != _lines.Count)
            throw new DomainRuleException("VALIDATION_FAILED", "调拨单需要1到100行且不能重复产品");
        Status = InventoryTransferStatus.Requested;
    }

    public Guid SourceStoreId { get; private set; }
    public Guid DestinationStoreId { get; private set; }
    public string TransferNo { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public Guid RequestedBy { get; private set; }
    public DateTimeOffset RequestedAtUtc { get; private set; }
    public InventoryTransferStatus Status { get; private set; }
    public Guid? ShippedBy { get; private set; }
    public DateTimeOffset? ShippedAtUtc { get; private set; }
    public Guid? ReceivedBy { get; private set; }
    public DateTimeOffset? ReceivedAtUtc { get; private set; }
    public string? DecisionReason { get; private set; }
    public IReadOnlyCollection<InventoryTransferLine> Lines => _lines;

    public void Ship(Guid operatorId, string reason, DateTimeOffset now)
    {
        if (Status != InventoryTransferStatus.Requested)
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "调拨单当前不能出库");
        ShippedBy = operatorId;
        ShippedAtUtc = now;
        DecisionReason = Required(reason, 500, "出库说明");
        Status = InventoryTransferStatus.InTransit;
        Touch();
    }

    public void Receive(Guid operatorId, string reason, DateTimeOffset now)
    {
        if (Status != InventoryTransferStatus.InTransit)
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "调拨单当前不能收货");
        ReceivedBy = operatorId;
        ReceivedAtUtc = now;
        DecisionReason = Required(reason, 500, "收货说明");
        Status = InventoryTransferStatus.Received;
        Touch();
    }

    public void Cancel(string reason)
    {
        if (Status != InventoryTransferStatus.Requested)
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "只有待出库调拨单可以取消");
        DecisionReason = Required(reason, 500, "取消原因");
        Status = InventoryTransferStatus.Cancelled;
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

public sealed class InventoryTransferLine : Entity
{
    private InventoryTransferLine() { }

    internal InventoryTransferLine(Guid tenantId, Guid transferId, Guid productItemId, int quantity)
        : base(tenantId)
    {
        if (quantity is < 1 or > 1_000_000_000)
            throw new DomainRuleException("VALIDATION_FAILED", "调拨数量必须为正整数");
        TransferId = transferId;
        ProductItemId = productItemId;
        Quantity = quantity;
    }

    public Guid TransferId { get; private set; }
    public Guid ProductItemId { get; private set; }
    public int Quantity { get; private set; }
}

public sealed class InventoryTransferLot : Entity
{
    private InventoryTransferLot() { }

    public InventoryTransferLot(Guid tenantId, Guid transferLineId, Guid sourceLotId, string batchNo,
        DateOnly? expiresOn, long unitCostMinor, int quantity) : base(tenantId)
    {
        if (quantity <= 0) throw new DomainRuleException("VALIDATION_FAILED", "调拨批次数量必须大于0");
        TransferLineId = transferLineId;
        SourceLotId = sourceLotId;
        BatchNo = batchNo;
        ExpiresOn = expiresOn;
        UnitCostMinor = unitCostMinor;
        Quantity = quantity;
    }

    public Guid TransferLineId { get; private set; }
    public Guid SourceLotId { get; private set; }
    public string BatchNo { get; private set; } = string.Empty;
    public DateOnly? ExpiresOn { get; private set; }
    public long UnitCostMinor { get; private set; }
    public int Quantity { get; private set; }
}
