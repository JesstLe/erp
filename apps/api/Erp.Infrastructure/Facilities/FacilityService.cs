using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Erp.Application.Common;
using Erp.Application.Facilities;
using Erp.Domain.Common;
using Erp.Domain.Facilities;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Erp.Infrastructure.Facilities;

public sealed class FacilityService(ErpDbContext db, TimeProvider clock, IHttpContextAccessor httpContextAccessor) : IFacilityService
{
    private static readonly FacilitySessionStatus[] OpenSessionStatuses = [FacilitySessionStatus.Active, FacilitySessionStatus.Paused];

    public async Task<Result<FacilityBoardDto>> GetBoardAsync(Guid tenantId, Guid storeId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var groups = await db.FacilityGroups.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.StoreId == storeId)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.DisplayName).ToListAsync(cancellationToken);
        var facilities = await db.Facilities.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.StoreId == storeId)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.DisplayName).ToListAsync(cancellationToken);
        var typeNames = await db.FacilityTypes.AsNoTracking().Where(x => x.TenantId == tenantId)
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken);
        var sessions = await db.FacilitySessions.AsNoTracking().Include(x => x.Pauses)
            .Where(x => x.TenantId == tenantId && x.StoreId == storeId && OpenSessionStatuses.Contains(x.Status))
            .ToListAsync(cancellationToken);
        var sessionByFacility = sessions.ToDictionary(x => x.FacilityId);
        var visitIds = sessions.Select(x => x.VisitId).Distinct().ToList();
        var visits = await db.Visits.AsNoTracking().Where(x => visitIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var cleaningIds = await db.FacilityCleaningTasks.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.StoreId == storeId && x.Status == CleaningTaskStatus.Pending)
            .Select(x => x.FacilityId).ToHashSetAsync(cancellationToken);

        var projected = groups.Select(group => new FacilityBoardGroupDto(group.Id, group.DisplayName,
            facilities.Where(x => x.GroupId == group.Id).Select(facility =>
            {
                sessionByFacility.TryGetValue(facility.Id, out var session);
                Visit? visit = null;
                if (session is not null) visits.TryGetValue(session.VisitId, out visit);
                return ToItem(facility, typeNames.GetValueOrDefault(facility.FacilityTypeId, "未分类"), session, visit,
                    cleaningIds.Contains(facility.Id), now);
            }).ToList())).ToList();

        return ResultFactory.Success(new FacilityBoardDto(now, projected));
    }

    public async Task<IReadOnlyList<FacilityGroupDto>> ListGroupsAsync(Guid tenantId, Guid storeId, CancellationToken cancellationToken) =>
        await db.FacilityGroups.AsNoTracking().Where(x => x.TenantId == tenantId && x.StoreId == storeId)
            .OrderBy(x => x.SortOrder).Select(x => new FacilityGroupDto(x.Id, x.DisplayName, x.SortOrder)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FacilityTypeDto>> ListTypesAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await db.FacilityTypes.AsNoTracking().Where(x => x.TenantId == tenantId).OrderBy(x => x.DisplayName)
            .Select(x => new FacilityTypeDto(x.Id, x.DisplayName)).ToListAsync(cancellationToken);

    public async Task<Result<FacilityGroupDto>> CreateGroupAsync(Guid tenantId, CreateFacilityGroupCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var group = new FacilityGroup(tenantId, command.StoreId, command.DisplayName, command.SortOrder);
            db.FacilityGroups.Add(group);
            await db.SaveChangesAsync(cancellationToken);
            return ResultFactory.Success(new FacilityGroupDto(group.Id, group.DisplayName, group.SortOrder));
        }
        catch (DomainRuleException exception) { return ResultFactory.Failure<FacilityGroupDto>("VALIDATION_FAILED", exception.Message); }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception)) { return ResultFactory.Failure<FacilityGroupDto>("DUPLICATE_FACILITY_GROUP", "同一门店已存在该设施分组"); }
    }

    public async Task<Result<FacilityTypeDto>> CreateTypeAsync(Guid tenantId, CreateFacilityTypeCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var type = new FacilityType(tenantId, command.DisplayName);
            db.FacilityTypes.Add(type);
            await db.SaveChangesAsync(cancellationToken);
            return ResultFactory.Success(new FacilityTypeDto(type.Id, type.DisplayName));
        }
        catch (DomainRuleException exception) { return ResultFactory.Failure<FacilityTypeDto>("VALIDATION_FAILED", exception.Message); }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception)) { return ResultFactory.Failure<FacilityTypeDto>("DUPLICATE_FACILITY_TYPE", "该设施类型已经存在"); }
    }

    public async Task<Result<FacilityBoardItemDto>> CreateFacilityAsync(Guid tenantId, CreateFacilityCommand command, CancellationToken cancellationToken)
    {
        var validGroup = await db.FacilityGroups.AnyAsync(x => x.Id == command.GroupId && x.TenantId == tenantId && x.StoreId == command.StoreId, cancellationToken);
        var validType = await db.FacilityTypes.AnyAsync(x => x.Id == command.FacilityTypeId && x.TenantId == tenantId, cancellationToken);
        if (!validGroup || !validType) return ResultFactory.Failure<FacilityBoardItemDto>("VALIDATION_FAILED", "设施分组或类型无效");
        try
        {
            var facility = new Facility(tenantId, command.StoreId, command.GroupId, command.FacilityTypeId, command.Code,
                command.DisplayName, command.SortOrder, command.DefaultCleaningMinutes, command.AllowReservation);
            db.Facilities.Add(facility);
            await db.SaveChangesAsync(cancellationToken);
            return await GetItemAsync(tenantId, command.StoreId, facility.Id, cancellationToken);
        }
        catch (DomainRuleException exception) { return ResultFactory.Failure<FacilityBoardItemDto>("VALIDATION_FAILED", exception.Message); }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception)) { return ResultFactory.Failure<FacilityBoardItemDto>("DUPLICATE_FACILITY_CODE", "同一门店的设施编号不能重复"); }
    }

    public Task<Result<FacilityBoardItemDto>> StartAsync(Guid tenantId, StartFacilitySessionCommand command, CancellationToken cancellationToken) =>
        ExecuteAsync(tenantId, command.StoreId, command.CommandId, command.OperatorId,
            $"START|{command.StoreId}|{command.FacilityId}|{command.ExpectedDurationMinutes}|{command.Note}", command.FacilityId,
            async now =>
            {
                var facility = await db.Facilities.SingleOrDefaultAsync(x => x.Id == command.FacilityId && x.TenantId == tenantId && x.StoreId == command.StoreId, cancellationToken);
                if (facility is null) return ResultFactory.Failure<Guid>("FACILITY_NOT_FOUND", "设施不存在");
                if (facility.LifecycleStatus != FacilityLifecycleStatus.Enabled ||
                    await db.FacilitySessions.AnyAsync(x => x.FacilityId == facility.Id && OpenSessionStatuses.Contains(x.Status), cancellationToken) ||
                    await db.FacilityCleaningTasks.AnyAsync(x => x.FacilityId == facility.Id && x.Status == CleaningTaskStatus.Pending, cancellationToken))
                    return ResultFactory.Failure<Guid>("FACILITY_NOT_AVAILABLE", "设施当前不可用，请刷新看板");

                var visit = new Visit(tenantId, command.StoreId, CreateVisitNo(now), command.ExpectedDurationMinutes, command.Note, now);
                var session = new FacilitySession(tenantId, command.StoreId, facility.Id, visit.Id, now, command.OperatorId, command.CommandId);
                db.Visits.Add(visit);
                // The session stores only the aggregate identifier, so persist the visit first inside the same transaction
                // to make the database foreign-key ordering explicit.
                await db.SaveChangesAsync(cancellationToken);
                db.FacilitySessions.Add(session);
                AddAudit(tenantId, command.StoreId, command.OperatorId, "facility.session.start", "FacilitySession", session.Id,
                    null, session.Status.ToString(), command.CommandId, null, now);
                return ResultFactory.Success(facility.Id);
            }, cancellationToken);

    public Task<Result<FacilityBoardItemDto>> PauseAsync(Guid tenantId, OperateFacilitySessionCommand command, CancellationToken cancellationToken) =>
        OperateSessionAsync(tenantId, command, "PAUSE", "facility.session.pause", (session, now) => session.Pause(now, command.OperatorId, command.CommandId), cancellationToken);

    public Task<Result<FacilityBoardItemDto>> ResumeAsync(Guid tenantId, OperateFacilitySessionCommand command, CancellationToken cancellationToken) =>
        OperateSessionAsync(tenantId, command, "RESUME", "facility.session.resume", (session, now) => session.Resume(now), cancellationToken);

    public Task<Result<FacilityBoardItemDto>> EndAsync(Guid tenantId, OperateFacilitySessionCommand command, CancellationToken cancellationToken) =>
        ExecuteAsync(tenantId, command.StoreId, command.CommandId, command.OperatorId, $"END|{command.StoreId}|{command.SessionId}", Guid.Empty,
            async now =>
            {
                var session = await LoadSessionAsync(tenantId, command.StoreId, command.SessionId, cancellationToken);
                if (session is null) return ResultFactory.Failure<Guid>("FACILITY_SESSION_NOT_FOUND", "设施使用记录不存在");
                var previous = session.Status.ToString();
                try { session.End(now, FacilitySessionEndReason.Completed); }
                catch (DomainRuleException exception) { return ResultFactory.Failure<Guid>("INVALID_STATE_TRANSITION", exception.Message); }
                var facility = await db.Facilities.SingleAsync(x => x.Id == session.FacilityId, cancellationToken);
                if (facility.DefaultCleaningMinutes > 0)
                    db.FacilityCleaningTasks.Add(new FacilityCleaningTask(tenantId, command.StoreId, facility.Id, session.Id, now.AddMinutes(facility.DefaultCleaningMinutes)));
                var hasOtherOpenSession = await db.FacilitySessions.AnyAsync(x => x.VisitId == session.VisitId && x.Id != session.Id && OpenSessionStatuses.Contains(x.Status), cancellationToken);
                if (!hasOtherOpenSession)
                {
                    var visit = await db.Visits.SingleAsync(x => x.Id == session.VisitId, cancellationToken);
                    visit.EndService(now);
                }
                AddAudit(tenantId, command.StoreId, command.OperatorId, "facility.session.end", "FacilitySession", session.Id,
                    previous, session.Status.ToString(), command.CommandId, null, now);
                return ResultFactory.Success(facility.Id);
            }, cancellationToken);

    public Task<Result<FacilityBoardItemDto>> SwitchAsync(Guid tenantId, SwitchFacilityCommand command, CancellationToken cancellationToken) =>
        ExecuteAsync(tenantId, command.StoreId, command.CommandId, command.OperatorId,
            $"SWITCH|{command.StoreId}|{command.SessionId}|{command.TargetFacilityId}|{command.Reason}", command.TargetFacilityId,
            async now =>
            {
                var session = await LoadSessionAsync(tenantId, command.StoreId, command.SessionId, cancellationToken);
                if (session is null) return ResultFactory.Failure<Guid>("FACILITY_SESSION_NOT_FOUND", "设施使用记录不存在");
                var target = await db.Facilities.SingleOrDefaultAsync(x => x.Id == command.TargetFacilityId && x.TenantId == tenantId && x.StoreId == command.StoreId, cancellationToken);
                if (target is null || target.LifecycleStatus != FacilityLifecycleStatus.Enabled ||
                    await db.FacilitySessions.AnyAsync(x => x.FacilityId == command.TargetFacilityId && OpenSessionStatuses.Contains(x.Status), cancellationToken) ||
                    await db.FacilityCleaningTasks.AnyAsync(x => x.FacilityId == command.TargetFacilityId && x.Status == CleaningTaskStatus.Pending, cancellationToken))
                    return ResultFactory.Failure<Guid>("FACILITY_NOT_AVAILABLE", "目标设施当前不可用，原设施继续计时");
                var oldFacility = await db.Facilities.SingleAsync(x => x.Id == session.FacilityId, cancellationToken);
                var switchGroupId = Guid.CreateVersion7();
                session.End(now, FacilitySessionEndReason.Switched, switchGroupId);
                var next = new FacilitySession(tenantId, command.StoreId, target.Id, session.VisitId, now, command.OperatorId, command.CommandId);
                db.FacilitySessions.Add(next);
                if (oldFacility.DefaultCleaningMinutes > 0)
                    db.FacilityCleaningTasks.Add(new FacilityCleaningTask(tenantId, command.StoreId, oldFacility.Id, session.Id, now.AddMinutes(oldFacility.DefaultCleaningMinutes)));
                AddAudit(tenantId, command.StoreId, command.OperatorId, "facility.session.switch", "FacilitySession", next.Id,
                    session.FacilityId.ToString(), target.Id.ToString(), command.CommandId, command.Reason, now);
                return ResultFactory.Success(target.Id);
            }, cancellationToken);

    public Task<Result<FacilityBoardItemDto>> CompleteCleaningAsync(Guid tenantId, Guid storeId, Guid facilityId, Guid commandId,
        Guid operatorId, CancellationToken cancellationToken) =>
        ExecuteAsync(tenantId, storeId, commandId, operatorId, $"CLEAN|{storeId}|{facilityId}", facilityId,
            async now =>
            {
                var task = await db.FacilityCleaningTasks.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.StoreId == storeId &&
                    x.FacilityId == facilityId && x.Status == CleaningTaskStatus.Pending, cancellationToken);
                if (task is null) return ResultFactory.Failure<Guid>("CLEANING_TASK_NOT_FOUND", "没有待完成的清洁任务");
                task.Complete(now, operatorId);
                AddAudit(tenantId, storeId, operatorId, "facility.cleaning.complete", "FacilityCleaningTask", task.Id,
                    CleaningTaskStatus.Pending.ToString(), CleaningTaskStatus.Completed.ToString(), commandId, null, now);
                return ResultFactory.Success(facilityId);
            }, cancellationToken);

    private Task<Result<FacilityBoardItemDto>> OperateSessionAsync(Guid tenantId, OperateFacilitySessionCommand command, string operation,
        string auditAction, Action<FacilitySession, DateTimeOffset> mutate, CancellationToken cancellationToken) =>
        ExecuteAsync(tenantId, command.StoreId, command.CommandId, command.OperatorId,
            $"{operation}|{command.StoreId}|{command.SessionId}", Guid.Empty,
            async now =>
            {
                var session = await LoadSessionAsync(tenantId, command.StoreId, command.SessionId, cancellationToken);
                if (session is null) return ResultFactory.Failure<Guid>("FACILITY_SESSION_NOT_FOUND", "设施使用记录不存在");
                var previous = session.Status.ToString();
                try { mutate(session, now); }
                catch (DomainRuleException exception) { return ResultFactory.Failure<Guid>("INVALID_STATE_TRANSITION", exception.Message); }
                AddAudit(tenantId, command.StoreId, command.OperatorId, auditAction, "FacilitySession", session.Id,
                    previous, session.Status.ToString(), command.CommandId, null, now);
                return ResultFactory.Success(session.FacilityId);
            }, cancellationToken);

    private async Task<Result<FacilityBoardItemDto>> ExecuteAsync(Guid tenantId, Guid storeId, Guid commandId, Guid operatorId,
        string requestIdentity, Guid knownFacilityId, Func<DateTimeOffset, Task<Result<Guid>>> action, CancellationToken cancellationToken)
    {
        if (commandId == Guid.Empty) return ResultFactory.Failure<FacilityBoardItemDto>("VALIDATION_FAILED", "缺少幂等请求号");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(requestIdentity));
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var existing = await db.IdempotencyCommands.AsNoTracking().SingleOrDefaultAsync(x => x.CommandId == commandId, cancellationToken);
        if (existing is not null)
        {
            if (!CryptographicOperations.FixedTimeEquals(existing.RequestHash, hash))
                return ResultFactory.Failure<FacilityBoardItemDto>("IDEMPOTENCY_CONFLICT", "幂等请求号已被其他请求使用");
            var receipt = existing.ResponseBody is null ? null : JsonSerializer.Deserialize<CommandReceipt>(existing.ResponseBody);
            return receipt is null ? ResultFactory.Failure<FacilityBoardItemDto>("COMMAND_IN_PROGRESS", "请求正在处理，请稍后刷新")
                : await GetItemAsync(tenantId, storeId, receipt.FacilityId, cancellationToken);
        }

        var now = clock.GetUtcNow();
        db.IdempotencyCommands.Add(new IdempotencyCommandRecord
        {
            CommandId = commandId, TenantId = tenantId, OperatorId = operatorId, RequestHash = hash,
            CreatedAtUtc = now,
        });
        try
        {
            var actionResult = await action(now);
            if (!actionResult.IsSuccess)
            {
                await transaction.RollbackAsync(cancellationToken);
                return ResultFactory.Failure<FacilityBoardItemDto>(actionResult.Error!.Code, actionResult.Error.Message);
            }
            var facilityId = actionResult.Value == Guid.Empty ? knownFacilityId : actionResult.Value;
            var record = db.IdempotencyCommands.Local.Single(x => x.CommandId == commandId);
            record.ResponseStatus = 200;
            record.ResponseBody = JsonSerializer.Serialize(new CommandReceipt(facilityId));
            record.CompletedAtUtc = now;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return await GetItemAsync(tenantId, storeId, facilityId, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ResultFactory.Failure<FacilityBoardItemDto>("VERSION_CONFLICT", "状态已变化，请刷新看板后重试");
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            return ResultFactory.Failure<FacilityBoardItemDto>("FACILITY_NOT_AVAILABLE", "设施状态已变化，请刷新看板后重试");
        }
        catch (DbUpdateException exception) when (IsDatabaseConcurrencyConflict(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            return ResultFactory.Failure<FacilityBoardItemDto>("VERSION_CONFLICT", "状态已变化，请刷新看板后重试");
        }
        catch (Exception exception) when (IsDatabaseConcurrencyConflict(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            return ResultFactory.Failure<FacilityBoardItemDto>("VERSION_CONFLICT", "状态已变化，请刷新看板后重试");
        }
    }

    private async Task<FacilitySession?> LoadSessionAsync(Guid tenantId, Guid storeId, Guid sessionId, CancellationToken cancellationToken) =>
        await db.FacilitySessions.Include(x => x.Pauses).SingleOrDefaultAsync(x => x.Id == sessionId && x.TenantId == tenantId && x.StoreId == storeId, cancellationToken);

    private async Task<Result<FacilityBoardItemDto>> GetItemAsync(Guid tenantId, Guid storeId, Guid facilityId, CancellationToken cancellationToken)
    {
        var board = await GetBoardAsync(tenantId, storeId, cancellationToken);
        var item = board.Value!.Groups.SelectMany(x => x.Facilities).SingleOrDefault(x => x.Id == facilityId);
        return item is null ? ResultFactory.Failure<FacilityBoardItemDto>("FACILITY_NOT_FOUND", "设施不存在") : ResultFactory.Success(item);
    }

    private static FacilityBoardItemDto ToItem(Facility facility, string typeName, FacilitySession? session, Visit? visit,
        bool cleaningRequired, DateTimeOffset now)
    {
        var status = facility.LifecycleStatus switch
        {
            FacilityLifecycleStatus.Maintenance => "MAINTENANCE",
            FacilityLifecycleStatus.Disabled => "DISABLED",
            _ when session?.Status == FacilitySessionStatus.Active => "IN_USE",
            _ when session?.Status == FacilitySessionStatus.Paused => "PAUSED",
            _ when cleaningRequired => "CLEANING_REQUIRED",
            _ => "AVAILABLE",
        };
        return new FacilityBoardItemDto(facility.Id, facility.Code, facility.DisplayName, typeName, status, facility.Version,
            session?.Id, session?.VisitId, visit?.VisitNo, session?.Status.ToString().ToUpperInvariant(), session?.StartedAtUtc,
            session?.GetActiveSeconds(now) ?? 0, session?.GetPausedSeconds(now) ?? 0, visit?.ExpectedDurationMinutes, visit?.Note);
    }

    private void AddAudit(Guid tenantId, Guid storeId, Guid operatorId, string action, string entityType, Guid entityId,
        string? previous, string? current, Guid commandId, string? reason, DateTimeOffset now) => db.AuditEvents.Add(new AuditEventRecord
        {
            TenantId = tenantId, StoreId = storeId, OperatorId = operatorId, Action = action, EntityType = entityType,
            EntityId = entityId, PreviousState = previous, CurrentState = current, Reason = reason, RequestId = commandId,
            TraceId = httpContextAccessor.HttpContext?.TraceIdentifier ?? "background", OccurredAtUtc = now,
        });

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static bool IsDatabaseConcurrencyConflict(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException
                { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected })
                return true;
        }

        return false;
    }

    private static string CreateVisitNo(DateTimeOffset now) => $"V{now:yyyyMMddHHmmss}{Guid.CreateVersion7():N}"[..30].ToUpperInvariant();
    private sealed record CommandReceipt(Guid FacilityId);
}
