using Erp.Domain.Common;

namespace Erp.Domain.Cashier;

public sealed class ServiceOrderVisitLink : Entity
{
    private ServiceOrderVisitLink() { }

    public ServiceOrderVisitLink(Guid tenantId, Guid orderId, Guid visitId) : base(tenantId)
    {
        if (orderId == Guid.Empty || visitId == Guid.Empty)
            throw new DomainRuleException("VALIDATION_FAILED", "消费单接待关联无效");
        OrderId = orderId;
        VisitId = visitId;
    }

    public Guid OrderId { get; private set; }
    public Guid VisitId { get; private set; }
}

public sealed class ServiceOrderPrebillSnapshot : Entity
{
    private ServiceOrderPrebillSnapshot() { }

    public ServiceOrderPrebillSnapshot(Guid tenantId, Guid storeId, Guid orderId, string prebillNo,
        string payloadJson, Guid generatedBy, DateTimeOffset generatedAtUtc) : base(tenantId)
    {
        if (storeId == Guid.Empty || orderId == Guid.Empty || generatedBy == Guid.Empty)
            throw new DomainRuleException("VALIDATION_FAILED", "预结单关联信息无效");
        var number = prebillNo.Trim();
        if (number.Length is 0 or > 40)
            throw new DomainRuleException("VALIDATION_FAILED", "预结单号无效");
        if (payloadJson.Length is 0 or > 200_000)
            throw new DomainRuleException("VALIDATION_FAILED", "预结快照内容无效");
        StoreId = storeId;
        OrderId = orderId;
        PrebillNo = number;
        PayloadJson = payloadJson;
        GeneratedBy = generatedBy;
        GeneratedAtUtc = generatedAtUtc;
    }

    public Guid StoreId { get; private set; }
    public Guid OrderId { get; private set; }
    public string PrebillNo { get; private set; } = string.Empty;
    public string PayloadJson { get; private set; } = string.Empty;
    public Guid GeneratedBy { get; private set; }
    public DateTimeOffset GeneratedAtUtc { get; private set; }
}
