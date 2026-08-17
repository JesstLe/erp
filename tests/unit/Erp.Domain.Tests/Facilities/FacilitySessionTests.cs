using Erp.Domain.Common;
using Erp.Domain.Facilities;

namespace Erp.Domain.Tests.Facilities;

public sealed class FacilitySessionTests
{
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid StoreId = Guid.CreateVersion7();
    private static readonly Guid FacilityId = Guid.CreateVersion7();
    private static readonly Guid VisitId = Guid.CreateVersion7();
    private static readonly Guid OperatorId = Guid.CreateVersion7();
    private static readonly DateTimeOffset Start = new(2026, 8, 18, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PauseAndResumeExcludePauseIntervalFromActiveDuration()
    {
        var session = CreateSession();

        session.Pause(Start.AddMinutes(20), OperatorId, Guid.CreateVersion7());
        session.Resume(Start.AddMinutes(30));

        Assert.Equal(FacilitySessionStatus.Active, session.Status);
        Assert.Equal(600, session.GetPausedSeconds(Start.AddMinutes(35)));
        Assert.Equal(1500, session.GetActiveSeconds(Start.AddMinutes(35)));
    }

    [Fact]
    public void EndWhilePausedClosesPauseAndProducesStableDurations()
    {
        var session = CreateSession();
        session.Pause(Start.AddMinutes(10), OperatorId, Guid.CreateVersion7());

        session.End(Start.AddMinutes(25), FacilitySessionEndReason.Completed);

        Assert.Equal(FacilitySessionStatus.Ended, session.Status);
        Assert.Equal(900, session.GetPausedSeconds(Start.AddHours(2)));
        Assert.Equal(600, session.GetActiveSeconds(Start.AddHours(2)));
    }

    [Fact]
    public void EndedSessionCannotResumeOrEndAgain()
    {
        var session = CreateSession();
        session.End(Start.AddMinutes(5), FacilitySessionEndReason.Completed);

        Assert.Throws<DomainRuleException>(() => session.End(Start.AddMinutes(6), FacilitySessionEndReason.Completed));
        Assert.Throws<DomainRuleException>(() => session.Resume(Start.AddMinutes(6)));
    }

    [Fact]
    public void SwitchEndPreservesSwitchGroupAndHistory()
    {
        var session = CreateSession();
        var switchGroupId = Guid.CreateVersion7();

        session.End(Start.AddMinutes(12), FacilitySessionEndReason.Switched, switchGroupId);

        Assert.Equal(FacilitySessionStatus.Ended, session.Status);
        Assert.Equal(FacilitySessionEndReason.Switched, session.EndReason);
        Assert.Equal(switchGroupId, session.SwitchGroupId);
        Assert.Equal(FacilityId, session.FacilityId);
    }

    [Fact]
    public void ReportRangeClipsSessionAndPauseIntervals()
    {
        var session = CreateSession();
        session.Pause(Start.AddMinutes(20), OperatorId, Guid.CreateVersion7());
        session.Resume(Start.AddMinutes(40));
        session.End(Start.AddHours(2), FacilitySessionEndReason.Completed);

        var seconds = session.GetActiveSecondsInRange(Start.AddMinutes(10), Start.AddMinutes(70), Start.AddHours(3));

        Assert.Equal(40 * 60, seconds);
        Assert.Equal(0, session.GetActiveSecondsInRange(Start.AddHours(3), Start.AddHours(4), Start.AddHours(4)));
        Assert.Throws<DomainRuleException>(() => session.GetActiveSecondsInRange(Start, Start, Start));
    }

    private static FacilitySession CreateSession() => new(TenantId, StoreId, FacilityId, VisitId, Start, OperatorId, Guid.CreateVersion7());
}
