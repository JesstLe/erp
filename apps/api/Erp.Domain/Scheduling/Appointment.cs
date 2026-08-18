using Erp.Domain.Common;

namespace Erp.Domain.Scheduling;

public enum AppointmentStatus
{
    Scheduled,
    Arrived,
    Cancelled,
    NoShow,
}

public sealed class Appointment : Entity
{
    private Appointment()
    {
    }

    public Appointment(Guid tenantId, Guid storeId, string appointmentNo, Guid customerId,
        Guid serviceItemId, Guid? employeeId, Guid? facilityId, DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc, string? note, Guid createdBy, Guid createCommandId)
        : base(tenantId)
    {
        if (storeId == Guid.Empty || customerId == Guid.Empty || serviceItemId == Guid.Empty ||
            createdBy == Guid.Empty || createCommandId == Guid.Empty)
            throw new DomainRuleException("VALIDATION_FAILED", "预约的门店、顾客、项目和操作信息不能为空");
        StoreId = storeId;
        AppointmentNo = Required(appointmentNo, 40, "预约编号").ToUpperInvariant();
        CustomerId = customerId;
        ServiceItemId = serviceItemId;
        SetResources(employeeId, facilityId, startsAtUtc, endsAtUtc, note);
        CreatedBy = createdBy;
        CreateCommandId = createCommandId;
        Status = AppointmentStatus.Scheduled;
    }

    public Guid StoreId { get; private set; }
    public string AppointmentNo { get; private set; } = string.Empty;
    public Guid CustomerId { get; private set; }
    public Guid ServiceItemId { get; private set; }
    public Guid? EmployeeId { get; private set; }
    public Guid? FacilityId { get; private set; }
    public DateTimeOffset StartsAtUtc { get; private set; }
    public DateTimeOffset EndsAtUtc { get; private set; }
    public string? Note { get; private set; }
    public AppointmentStatus Status { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid CreateCommandId { get; private set; }
    public Guid? VisitId { get; private set; }
    public Guid? ArrivedBy { get; private set; }
    public DateTimeOffset? ArrivedAtUtc { get; private set; }
    public DateTimeOffset? CancelledAtUtc { get; private set; }
    public Guid? CancelledBy { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTimeOffset? NoShowAtUtc { get; private set; }
    public Guid? NoShowBy { get; private set; }
    public string? NoShowReason { get; private set; }

    public void Update(Guid serviceItemId, Guid? employeeId, Guid? facilityId,
        DateTimeOffset startsAtUtc, DateTimeOffset endsAtUtc, string? note)
    {
        EnsureScheduled("只有待到店预约可以修改");
        if (serviceItemId == Guid.Empty)
            throw new DomainRuleException("VALIDATION_FAILED", "服务项目不能为空");
        ServiceItemId = serviceItemId;
        SetResources(employeeId, facilityId, startsAtUtc, endsAtUtc, note);
        Touch();
    }

    public void Cancel(DateTimeOffset now, Guid operatorId, string reason)
    {
        EnsureScheduled("只有待到店预约可以取消");
        var normalizedReason = Required(reason, 500, "取消原因");
        Status = AppointmentStatus.Cancelled;
        CancelledAtUtc = now;
        CancelledBy = operatorId;
        CancellationReason = normalizedReason;
        Touch();
    }

    public void MarkNoShow(DateTimeOffset now, Guid operatorId, string? reason)
    {
        EnsureScheduled("只有待到店预约可以标记爽约");
        if (now < StartsAtUtc)
            throw new DomainRuleException("APPOINTMENT_NOT_STARTED", "预约开始前不能标记爽约");
        var normalizedReason = Optional(reason, 500, "爽约说明");
        Status = AppointmentStatus.NoShow;
        NoShowAtUtc = now;
        NoShowBy = operatorId;
        NoShowReason = normalizedReason;
        Touch();
    }

    public void MarkArrived(DateTimeOffset now, Guid operatorId, Guid visitId)
    {
        EnsureScheduled("只有待到店预约可以办理到店");
        if (visitId == Guid.Empty)
            throw new DomainRuleException("VALIDATION_FAILED", "接待记录不能为空");
        Status = AppointmentStatus.Arrived;
        VisitId = visitId;
        ArrivedAtUtc = now;
        ArrivedBy = operatorId;
        Touch();
    }

    private void SetResources(Guid? employeeId, Guid? facilityId, DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc, string? note)
    {
        if (employeeId == Guid.Empty || facilityId == Guid.Empty)
            throw new DomainRuleException("VALIDATION_FAILED", "预约员工或设施无效");
        var duration = endsAtUtc - startsAtUtc;
        if (duration < TimeSpan.FromMinutes(5) || duration > TimeSpan.FromHours(24))
            throw new DomainRuleException("VALIDATION_FAILED", "预约时长必须为5分钟至24小时");
        EmployeeId = employeeId;
        FacilityId = facilityId;
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
        Note = Optional(note, 500, "预约备注");
    }

    private void EnsureScheduled(string message)
    {
        if (Status != AppointmentStatus.Scheduled)
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", message);
    }

    private static string Required(string value, int maxLength, string label)
    {
        var normalized = value.Trim();
        if (normalized.Length is 0 || normalized.Length > maxLength)
            throw new DomainRuleException("VALIDATION_FAILED", $"{label}长度必须为1至{maxLength}个字符");
        return normalized;
    }

    private static string? Optional(string? value, int maxLength, string label)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)) return null;
        if (normalized.Length > maxLength)
            throw new DomainRuleException("VALIDATION_FAILED", $"{label}最多{maxLength}个字符");
        return normalized;
    }
}
