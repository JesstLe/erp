using Erp.Application.Auditing;
using Erp.Infrastructure.Identity;
using Erp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Erp.Infrastructure.Auditing;

internal sealed class AuditQueryService(ErpDbContext db) : IAuditQueryService
{
    public async Task<AuditEventPageDto> QueryAsync(Guid tenantId, Guid storeId, string? action, string? entityType,
        DateOnly? fromDate, DateOnly? toDate, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Clamp(page, 1, 10_000);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var normalizedAction = Normalize(action, 128);
        var normalizedEntity = Normalize(entityType, 80);
        var timeZoneId = await db.Stores.Where(x => x.Id == storeId && x.TenantId == tenantId)
            .Select(x => x.TimeZoneId).SingleAsync(cancellationToken);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        DateTimeOffset? fromUtc = fromDate is null ? null : ToUtc(fromDate.Value, timeZone);
        DateTimeOffset? toUtc = toDate is null ? null : ToUtc(toDate.Value.AddDays(1), timeZone);
        var query = db.AuditEvents.AsNoTracking().Where(x => x.TenantId == tenantId && x.StoreId == storeId);
        if (normalizedAction is not null) query = query.Where(x => x.Action.Contains(normalizedAction));
        if (normalizedEntity is not null) query = query.Where(x => x.EntityType == normalizedEntity);
        if (fromUtc is not null) query = query.Where(x => x.OccurredAtUtc >= fromUtc);
        if (toUtc is not null) query = query.Where(x => x.OccurredAtUtc < toUtc);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var operatorIds = rows.Where(x => x.OperatorId is not null).Select(x => x.OperatorId!.Value).Distinct().ToList();
        var operators = await db.Set<ApplicationUser>().AsNoTracking().Where(x => operatorIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken);
        var items = rows.Select(x => new AuditEventDto(x.Id, x.Action, x.EntityType, x.EntityId,
            x.PreviousState, x.CurrentState, x.Reason, x.OperatorId,
            x.OperatorId is not null && operators.TryGetValue(x.OperatorId.Value, out var name) ? name : "系统/未知账号",
            x.RequestId, x.TraceId, x.OccurredAtUtc)).ToList();
        return new AuditEventPageDto(items, total, page, pageSize);
    }

    private static string? Normalize(string? value, int max)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized?.Length > max ? normalized[..max] : normalized;
    }

    private static DateTimeOffset ToUtc(DateOnly date, TimeZoneInfo timeZone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, timeZone), TimeSpan.Zero);
    }
}
