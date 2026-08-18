using Erp.Domain.Common;

namespace Erp.Domain.Inventory;

public enum InventoryReservationStatus { Active, Consumed, Released }
public enum InventoryMovementDirection { In, Out }
public enum InventoryMovementType { Opening, Receipt, SaleIssue, SalesReturn, AdjustmentIn, AdjustmentOut }
public enum InventoryDocumentType { Opening, Receipt, AdjustmentIn, AdjustmentOut }

public sealed class InventoryBalance : Entity
{
    private InventoryBalance() { }

    public InventoryBalance(Guid tenantId, Guid storeId, Guid productItemId) : base(tenantId)
    {
        StoreId = storeId;
        ProductItemId = productItemId;
    }

    public Guid StoreId { get; private set; }
    public Guid ProductItemId { get; private set; }
    public int OnHandQuantity { get; private set; }
    public int ReservedQuantity { get; private set; }
    public int AvailableQuantity => checked(OnHandQuantity - ReservedQuantity);

    public void Receive(int quantity)
    {
        Positive(quantity);
        OnHandQuantity = checked(OnHandQuantity + quantity);
        Touch();
    }

    public void Reserve(int quantity)
    {
        Positive(quantity);
        if (quantity > AvailableQuantity)
            throw new DomainRuleException("INSUFFICIENT_INVENTORY", "产品可用库存不足，不能确认消费单");
        ReservedQuantity = checked(ReservedQuantity + quantity);
        Touch();
    }

    public void ConsumeReserved(int quantity)
    {
        Positive(quantity);
        if (quantity > ReservedQuantity || quantity > OnHandQuantity)
            throw new DomainRuleException("INVENTORY_RESERVATION_MISMATCH", "库存预占不足，不能完成产品出库");
        ReservedQuantity -= quantity;
        OnHandQuantity -= quantity;
        Touch();
    }

    public void Release(int quantity)
    {
        Positive(quantity);
        if (quantity > ReservedQuantity)
            throw new DomainRuleException("INVENTORY_RESERVATION_MISMATCH", "库存预占不足，不能释放");
        ReservedQuantity -= quantity;
        Touch();
    }

    public void AdjustOut(int quantity)
    {
        Positive(quantity);
        if (quantity > AvailableQuantity)
            throw new DomainRuleException("INSUFFICIENT_INVENTORY", "可用库存不足，不能出库");
        OnHandQuantity -= quantity;
        Touch();
    }

    private static void Positive(int quantity)
    {
        if (quantity is < 1 or > 1_000_000_000)
            throw new DomainRuleException("VALIDATION_FAILED", "库存数量必须为1到1000000000的整数");
    }
}

public sealed class InventoryReservation : Entity
{
    private InventoryReservation() { }

    public InventoryReservation(Guid tenantId, Guid storeId, Guid orderId, Guid orderLineId,
        Guid productItemId, Guid balanceId, int quantity, DateTimeOffset reservedAtUtc) : base(tenantId)
    {
        if (quantity is < 1 or > 999)
            throw new DomainRuleException("VALIDATION_FAILED", "销售预占数量必须为1到999");
        StoreId = storeId;
        OrderId = orderId;
        OrderLineId = orderLineId;
        ProductItemId = productItemId;
        BalanceId = balanceId;
        Quantity = quantity;
        Status = InventoryReservationStatus.Active;
        ReservedAtUtc = reservedAtUtc;
    }

    public Guid StoreId { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid OrderLineId { get; private set; }
    public Guid ProductItemId { get; private set; }
    public Guid BalanceId { get; private set; }
    public int Quantity { get; private set; }
    public InventoryReservationStatus Status { get; private set; }
    public DateTimeOffset ReservedAtUtc { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }
    public DateTimeOffset? ReleasedAtUtc { get; private set; }

    public void Consume(DateTimeOffset now)
    {
        EnsureActive();
        Status = InventoryReservationStatus.Consumed;
        ConsumedAtUtc = now;
        Touch();
    }

    public void Release(DateTimeOffset now)
    {
        EnsureActive();
        Status = InventoryReservationStatus.Released;
        ReleasedAtUtc = now;
        Touch();
    }

    private void EnsureActive()
    {
        if (Status != InventoryReservationStatus.Active)
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "库存预占已经结束");
    }
}

public sealed class InventoryMovement : Entity
{
    private InventoryMovement() { }

    public InventoryMovement(Guid tenantId, Guid storeId, Guid productItemId, Guid balanceId,
        InventoryMovementType movementType, InventoryMovementDirection direction, int quantity,
        int onHandBefore, int onHandAfter, string sourceType, Guid sourceId, Guid sourceLineId,
        Guid commandId, Guid? operatorId, DateTimeOffset occurredAtUtc) : base(tenantId)
    {
        if (quantity <= 0 || onHandBefore < 0 || onHandAfter < 0 ||
            (direction == InventoryMovementDirection.In && onHandAfter - onHandBefore != quantity) ||
            (direction == InventoryMovementDirection.Out && onHandBefore - onHandAfter != quantity))
            throw new DomainRuleException("VALIDATION_FAILED", "库存流水数量或余额快照无效");
        StoreId = storeId;
        ProductItemId = productItemId;
        BalanceId = balanceId;
        MovementType = movementType;
        Direction = direction;
        Quantity = quantity;
        OnHandBefore = onHandBefore;
        OnHandAfter = onHandAfter;
        SourceType = Required(sourceType, 40, "库存来源类型");
        SourceId = sourceId;
        SourceLineId = sourceLineId;
        CommandId = commandId;
        OperatorId = operatorId;
        OccurredAtUtc = occurredAtUtc;
    }

    public Guid StoreId { get; private set; }
    public Guid ProductItemId { get; private set; }
    public Guid BalanceId { get; private set; }
    public InventoryMovementType MovementType { get; private set; }
    public InventoryMovementDirection Direction { get; private set; }
    public int Quantity { get; private set; }
    public int OnHandBefore { get; private set; }
    public int OnHandAfter { get; private set; }
    public string SourceType { get; private set; } = string.Empty;
    public Guid SourceId { get; private set; }
    public Guid SourceLineId { get; private set; }
    public Guid CommandId { get; private set; }
    public Guid? OperatorId { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }

    private static string Required(string value, int maximum, string field)
    {
        var normalized = value.Trim();
        if (normalized.Length is 0 || normalized.Length > maximum)
            throw new DomainRuleException("VALIDATION_FAILED", $"{field}长度不正确");
        return normalized;
    }
}

public sealed class InventoryDocument : Entity
{
    private readonly List<InventoryDocumentLine> _lines = [];
    private InventoryDocument() { }

    public InventoryDocument(Guid tenantId, Guid storeId, string documentNo, InventoryDocumentType documentType,
        string reason, Guid postedBy, DateTimeOffset postedAtUtc,
        IEnumerable<(Guid ProductItemId, int Quantity)> lines) : base(tenantId)
    {
        StoreId = storeId;
        DocumentNo = Required(documentNo, 40, "库存单号");
        DocumentType = documentType;
        Reason = Required(reason, 500, "库存变动原因");
        PostedBy = postedBy;
        PostedAtUtc = postedAtUtc;
        foreach (var line in lines)
            _lines.Add(new InventoryDocumentLine(tenantId, Id, line.ProductItemId, line.Quantity));
        if (_lines.Count is 0 or > 100)
            throw new DomainRuleException("VALIDATION_FAILED", "库存单据需要1到100行产品");
        if (_lines.Select(x => x.ProductItemId).Distinct().Count() != _lines.Count)
            throw new DomainRuleException("VALIDATION_FAILED", "同一库存单据不能重复产品");
    }

    public Guid StoreId { get; private set; }
    public string DocumentNo { get; private set; } = string.Empty;
    public InventoryDocumentType DocumentType { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public Guid PostedBy { get; private set; }
    public DateTimeOffset PostedAtUtc { get; private set; }
    public IReadOnlyCollection<InventoryDocumentLine> Lines => _lines;

    private static string Required(string value, int maximum, string field)
    {
        var normalized = value.Trim();
        if (normalized.Length is 0 || normalized.Length > maximum)
            throw new DomainRuleException("VALIDATION_FAILED", $"{field}长度不正确");
        return normalized;
    }
}

public sealed class InventoryDocumentLine : Entity
{
    private InventoryDocumentLine() { }

    internal InventoryDocumentLine(Guid tenantId, Guid documentId, Guid productItemId, int quantity)
        : base(tenantId)
    {
        if (quantity is < 1 or > 1_000_000_000)
            throw new DomainRuleException("VALIDATION_FAILED", "库存单据数量必须为正整数");
        DocumentId = documentId;
        ProductItemId = productItemId;
        Quantity = quantity;
    }

    public Guid DocumentId { get; private set; }
    public Guid ProductItemId { get; private set; }
    public int Quantity { get; private set; }
}

public sealed class ProductReturn : Entity
{
    private ProductReturn() { }

    public ProductReturn(Guid tenantId, Guid storeId, Guid orderId, Guid orderLineId, Guid productItemId,
        int quantity, string reason, Guid commandId, Guid returnedBy, DateTimeOffset returnedAtUtc) : base(tenantId)
    {
        if (quantity is < 1 or > 999)
            throw new DomainRuleException("VALIDATION_FAILED", "退货数量必须为1到999");
        StoreId = storeId;
        OrderId = orderId;
        OrderLineId = orderLineId;
        ProductItemId = productItemId;
        Quantity = quantity;
        Reason = Required(reason, 500, "退货原因");
        CommandId = commandId;
        ReturnedBy = returnedBy;
        ReturnedAtUtc = returnedAtUtc;
    }

    public Guid StoreId { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid OrderLineId { get; private set; }
    public Guid ProductItemId { get; private set; }
    public int Quantity { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public Guid CommandId { get; private set; }
    public Guid ReturnedBy { get; private set; }
    public DateTimeOffset ReturnedAtUtc { get; private set; }

    private static string Required(string value, int maximum, string field)
    {
        var normalized = value.Trim();
        if (normalized.Length is 0 || normalized.Length > maximum)
            throw new DomainRuleException("VALIDATION_FAILED", $"{field}长度不正确");
        return normalized;
    }
}
