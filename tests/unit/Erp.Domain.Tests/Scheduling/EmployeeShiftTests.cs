using Erp.Domain.Common;
using Erp.Domain.Scheduling;

namespace Erp.Domain.Tests.Scheduling;

public sealed class EmployeeShiftTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 19, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ScheduledShiftCanBeUpdatedAndCancelled()
    {
        var shift = Create();

        shift.Update(Start.AddHours(1), Start.AddHours(9), "晚班");
        shift.Cancel(Start, Guid.CreateVersion7(), "临时调休");

        Assert.Equal(EmployeeShiftStatus.Cancelled, shift.Status);
        Assert.Equal("晚班", shift.Note);
        Assert.Equal("临时调休", shift.CancellationReason);
        Assert.Throws<DomainRuleException>(() => shift.Update(Start, Start.AddHours(8), null));
    }

    [Fact]
    public void ShiftDurationMustBeAtLeastThirtyMinutes()
    {
        var exception = Assert.Throws<DomainRuleException>(() => new EmployeeShift(Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Start, Start.AddMinutes(29), null,
            Guid.CreateVersion7(), Guid.CreateVersion7()));

        Assert.Equal("VALIDATION_FAILED", exception.Code);
    }

    private static EmployeeShift Create() => new(Guid.CreateVersion7(), Guid.CreateVersion7(),
        Guid.CreateVersion7(), Start, Start.AddHours(8), null, Guid.CreateVersion7(), Guid.CreateVersion7());
}
