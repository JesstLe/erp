using Erp.Domain.Common;

namespace Erp.Domain.Facilities;

public enum FacilitySessionStatus { Active, Paused, Ended, Cancelled }
public enum FacilitySessionEndReason { Completed, Switched, Mistaken }

public sealed class FacilitySession : Entity
{
    private readonly List<FacilitySessionPause> _pauses = [];
    private FacilitySession() { }

    public FacilitySession(Guid tenantId, Guid storeId, Guid facilityId, Guid visitId, DateTimeOffset startedAtUtc,
        Guid startedByUserId, Guid startCommandId) : base(tenantId)
    {
        StoreId = storeId;
        FacilityId = facilityId;
        VisitId = visitId;
        StartedAtUtc = startedAtUtc;
        StartedByUserId = startedByUserId;
        StartCommandId = startCommandId;
        Status = FacilitySessionStatus.Active;
    }

    public Guid StoreId { get; private set; }
    public Guid FacilityId { get; private set; }
    public Guid VisitId { get; private set; }
    public FacilitySessionStatus Status { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? EndedAtUtc { get; private set; }
    public Guid StartedByUserId { get; private set; }
    public Guid StartCommandId { get; private set; }
    public FacilitySessionEndReason? EndReason { get; private set; }
    public Guid? SwitchGroupId { get; private set; }
    public IReadOnlyCollection<FacilitySessionPause> Pauses => _pauses.AsReadOnly();

    public void Pause(DateTimeOffset now, Guid operatorId, Guid commandId)
    {
        if (Status != FacilitySessionStatus.Active) throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "只有使用中的设施可以暂停");
        _pauses.Add(new FacilitySessionPause(TenantId, Id, now, operatorId, commandId));
        Status = FacilitySessionStatus.Paused;
        Touch();
    }

    public void Resume(DateTimeOffset now)
    {
        if (Status != FacilitySessionStatus.Paused) throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "只有暂停中的设施可以继续");
        var pause = _pauses.SingleOrDefault(x => x.EndedAtUtc is null)
            ?? throw new DomainRuleException("INVARIANT_VIOLATION", "暂停区间记录缺失");
        pause.Resume(now);
        Status = FacilitySessionStatus.Active;
        Touch();
    }

    public void End(DateTimeOffset now, FacilitySessionEndReason reason, Guid? switchGroupId = null)
    {
        if (Status is not (FacilitySessionStatus.Active or FacilitySessionStatus.Paused))
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "当前设施使用记录不能结束");
        if (Status == FacilitySessionStatus.Paused)
            _pauses.Single(x => x.EndedAtUtc is null).Resume(now);
        Status = reason == FacilitySessionEndReason.Mistaken ? FacilitySessionStatus.Cancelled : FacilitySessionStatus.Ended;
        EndedAtUtc = now;
        EndReason = reason;
        SwitchGroupId = switchGroupId;
        Touch();
    }

    public long GetPausedSeconds(DateTimeOffset now) => _pauses.Sum(x => x.GetDurationSeconds(now));

    public long GetActiveSeconds(DateTimeOffset now)
    {
        var end = EndedAtUtc ?? now;
        var elapsed = Math.Max(0, (long)(end - StartedAtUtc).TotalSeconds);
        return Math.Max(0, elapsed - GetPausedSeconds(now));
    }

    public long GetActiveSecondsInRange(DateTimeOffset rangeStart, DateTimeOffset rangeEnd, DateTimeOffset now)
    {
        if (rangeEnd <= rangeStart) throw new DomainRuleException("VALIDATION_FAILED", "统计结束时间必须晚于开始时间");
        var start = StartedAtUtc > rangeStart ? StartedAtUtc : rangeStart;
        var rawEnd = EndedAtUtc ?? now;
        var end = rawEnd < rangeEnd ? rawEnd : rangeEnd;
        if (end <= start) return 0;
        var seconds = (long)(end - start).TotalSeconds;
        foreach (var pause in _pauses)
        {
            var pauseStart = pause.StartedAtUtc > start ? pause.StartedAtUtc : start;
            var pauseRawEnd = pause.EndedAtUtc ?? now;
            var pauseEnd = pauseRawEnd < end ? pauseRawEnd : end;
            if (pauseEnd > pauseStart) seconds -= (long)(pauseEnd - pauseStart).TotalSeconds;
        }
        return Math.Max(0, seconds);
    }
}

public sealed class FacilitySessionPause : Entity
{
    private FacilitySessionPause() { }

    internal FacilitySessionPause(Guid tenantId, Guid sessionId, DateTimeOffset startedAtUtc, Guid startedByUserId, Guid commandId)
        : base(tenantId)
    {
        SessionId = sessionId;
        StartedAtUtc = startedAtUtc;
        StartedByUserId = startedByUserId;
        CommandId = commandId;
    }

    public Guid SessionId { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? EndedAtUtc { get; private set; }
    public Guid StartedByUserId { get; private set; }
    public Guid CommandId { get; private set; }

    internal void Resume(DateTimeOffset now)
    {
        if (EndedAtUtc is not null) throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "暂停区间已经结束");
        EndedAtUtc = now;
        Touch();
    }

    internal long GetDurationSeconds(DateTimeOffset now) => Math.Max(0, (long)((EndedAtUtc ?? now) - StartedAtUtc).TotalSeconds);
}
