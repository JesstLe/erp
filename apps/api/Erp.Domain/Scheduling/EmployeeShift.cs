using Erp.Domain.Common;

namespace Erp.Domain.Scheduling;

public enum EmployeeShiftStatus
{
    Scheduled,
    Cancelled,
}

public sealed class EmployeeShift : Entity
{
    private EmployeeShift()
    {
    }

    public EmployeeShift(Guid tenantId, Guid storeId, Guid employeeId, DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc, string? note, Guid createdBy, Guid createCommandId)
        : base(tenantId)
    {
        if (storeId == Guid.Empty || employeeId == Guid.Empty || createdBy == Guid.Empty ||
            createCommandId == Guid.Empty)
            throw new DomainRuleException("VALIDATION_FAILED", "班次的门店、员工和操作信息不能为空");
        StoreId = storeId;
        EmployeeId = employeeId;
        SetPeriod(startsAtUtc, endsAtUtc, note);
        CreatedBy = createdBy;
        CreateCommandId = createCommandId;
        Status = EmployeeShiftStatus.Scheduled;
    }

    public Guid StoreId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public DateTimeOffset StartsAtUtc { get; private set; }
    public DateTimeOffset EndsAtUtc { get; private set; }
    public string? Note { get; private set; }
    public EmployeeShiftStatus Status { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid CreateCommandId { get; private set; }
    public DateTimeOffset? CancelledAtUtc { get; private set; }
    public Guid? CancelledBy { get; private set; }
    public string? CancellationReason { get; private set; }

    public void Update(DateTimeOffset startsAtUtc, DateTimeOffset endsAtUtc, string? note)
    {
        EnsureScheduled();
        SetPeriod(startsAtUtc, endsAtUtc, note);
        Touch();
    }

    public void Cancel(DateTimeOffset now, Guid operatorId, string reason)
    {
        EnsureScheduled();
        var normalized = reason.Trim();
        if (normalized.Length is 0 or > 500)
            throw new DomainRuleException("VALIDATION_FAILED", "取消原因长度必须为1至500个字符");
        Status = EmployeeShiftStatus.Cancelled;
        CancelledAtUtc = now;
        CancelledBy = operatorId;
        CancellationReason = normalized;
        Touch();
    }

    private void SetPeriod(DateTimeOffset startsAtUtc, DateTimeOffset endsAtUtc, string? note)
    {
        var duration = endsAtUtc - startsAtUtc;
        if (duration < TimeSpan.FromMinutes(30) || duration > TimeSpan.FromHours(24))
            throw new DomainRuleException("VALIDATION_FAILED", "员工班次必须为30分钟至24小时");
        var normalized = note?.Trim();
        if (normalized?.Length > 500)
            throw new DomainRuleException("VALIDATION_FAILED", "班次备注最多500个字符");
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
        Note = string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private void EnsureScheduled()
    {
        if (Status != EmployeeShiftStatus.Scheduled)
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "已取消班次不能修改");
    }
}
