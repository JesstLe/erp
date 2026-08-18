using Erp.Domain.Common;
using Erp.Domain.Scheduling;

namespace Erp.Domain.Tests.Scheduling;

public sealed class AppointmentTests
{
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid StoreId = Guid.CreateVersion7();
    private static readonly DateTimeOffset Start = new(2026, 8, 19, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OptionalEmployeeAndFacilityDoNotPreventScheduling()
    {
        var appointment = Create();

        Assert.Equal(AppointmentStatus.Scheduled, appointment.Status);
        Assert.Null(appointment.EmployeeId);
        Assert.Null(appointment.FacilityId);
    }

    [Fact]
    public void ArrivalLinksImmutableVisitContext()
    {
        var appointment = Create();
        var visitId = Guid.CreateVersion7();

        appointment.MarkArrived(Start.AddMinutes(-10), Guid.CreateVersion7(), visitId);

        Assert.Equal(AppointmentStatus.Arrived, appointment.Status);
        Assert.Equal(visitId, appointment.VisitId);
        Assert.Throws<DomainRuleException>(() => appointment.Cancel(Start, Guid.CreateVersion7(), "重复处理"));
    }

    [Fact]
    public void NoShowCannotBeRecordedBeforeStart()
    {
        var appointment = Create();

        var exception = Assert.Throws<DomainRuleException>(() => appointment.MarkNoShow(
            Start.AddSeconds(-1), Guid.CreateVersion7(), null));

        Assert.Equal("APPOINTMENT_NOT_STARTED", exception.Code);
    }

    [Fact]
    public void CancellationRequiresReasonAndPreservesRecord()
    {
        var appointment = Create();

        Assert.Throws<DomainRuleException>(() => appointment.Cancel(Start, Guid.CreateVersion7(), " "));

        appointment.Cancel(Start, Guid.CreateVersion7(), "顾客主动取消");
        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
        Assert.Equal("顾客主动取消", appointment.CancellationReason);
    }

    [Fact]
    public void AppointmentDurationIsBounded()
    {
        var exception = Assert.Throws<DomainRuleException>(() => Create(Start, Start.AddMinutes(4)));

        Assert.Equal("VALIDATION_FAILED", exception.Code);
    }

    private static Appointment Create(DateTimeOffset? startsAt = null, DateTimeOffset? endsAt = null) =>
        new(TenantId, StoreId, "A202608190001", Guid.CreateVersion7(), Guid.CreateVersion7(), null, null,
            startsAt ?? Start, endsAt ?? Start.AddHours(1), null, Guid.CreateVersion7(), Guid.CreateVersion7());
}
