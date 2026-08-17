using Erp.Domain.Common;

namespace Erp.Domain.Facilities;

public enum CleaningTaskStatus { Pending, Completed }

public sealed class FacilityCleaningTask : Entity
{
    private FacilityCleaningTask() { }

    public FacilityCleaningTask(Guid tenantId, Guid storeId, Guid facilityId, Guid sessionId, DateTimeOffset dueAtUtc)
        : base(tenantId)
    {
        StoreId = storeId;
        FacilityId = facilityId;
        SessionId = sessionId;
        Status = CleaningTaskStatus.Pending;
        DueAtUtc = dueAtUtc;
    }

    public Guid StoreId { get; private set; }
    public Guid FacilityId { get; private set; }
    public Guid SessionId { get; private set; }
    public CleaningTaskStatus Status { get; private set; }
    public DateTimeOffset DueAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public Guid? CompletedByUserId { get; private set; }

    public void Complete(DateTimeOffset now, Guid operatorId)
    {
        if (Status != CleaningTaskStatus.Pending) throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "清洁任务已完成");
        Status = CleaningTaskStatus.Completed;
        CompletedAtUtc = now;
        CompletedByUserId = operatorId;
        Touch();
    }
}
