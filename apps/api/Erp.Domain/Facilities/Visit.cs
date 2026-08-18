using Erp.Domain.Common;

namespace Erp.Domain.Facilities;

public enum VisitStatus { Arrived, InService, ServiceEnded, LeftNoConsumption, Completed, Cancelled }

public sealed class Visit : Entity
{
    private Visit() { }

    public Visit(Guid tenantId, Guid storeId, string visitNo, int? expectedDurationMinutes, string? note,
        DateTimeOffset arrivedAtUtc, Guid? plannedServiceItemId = null)
        : base(tenantId)
    {
        StoreId = storeId;
        VisitNo = visitNo;
        if (expectedDurationMinutes is <= 0 or > 1440) throw new DomainRuleException("VALIDATION_FAILED", "预计时长必须为1至1440分钟");
        if (note?.Trim().Length > 500) throw new DomainRuleException("VALIDATION_FAILED", "备注最多500个字符");
        ExpectedDurationMinutes = expectedDurationMinutes;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (plannedServiceItemId == Guid.Empty)
            throw new DomainRuleException("VALIDATION_FAILED", "预计服务项目无效");
        PlannedServiceItemId = plannedServiceItemId;
        ArrivedAtUtc = arrivedAtUtc;
        Status = VisitStatus.InService;
    }

    public Guid StoreId { get; private set; }
    public string VisitNo { get; private set; } = string.Empty;
    public Guid? CustomerId { get; private set; }
    public Guid? PlannedServiceItemId { get; private set; }
    public int? ExpectedDurationMinutes { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset ArrivedAtUtc { get; private set; }
    public DateTimeOffset? ServiceEndedAtUtc { get; private set; }
    public VisitStatus Status { get; private set; }

    public void EndService(DateTimeOffset now)
    {
        if (Status != VisitStatus.InService) throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "当前接待不能结束服务");
        Status = VisitStatus.ServiceEnded;
        ServiceEndedAtUtc = now;
        Touch();
    }

    public void LinkCustomer(Guid customerId)
    {
        if (Status is VisitStatus.Completed or VisitStatus.Cancelled)
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "当前接待不能关联顾客");
        CustomerId = customerId;
        Touch();
    }

    public void Complete()
    {
        if (Status != VisitStatus.ServiceEnded) throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "服务尚未结束，不能完成接待");
        Status = VisitStatus.Completed;
        Touch();
    }
}
