namespace Erp.Application.Auditing;

public sealed record AuditEventDto(Guid Id, string Action, string EntityType, Guid? EntityId,
    string? PreviousState, string? CurrentState, string? Reason, Guid? OperatorId, string OperatorDisplayName,
    Guid? RequestId, string TraceId, DateTimeOffset OccurredAtUtc);
public sealed record AuditEventPageDto(IReadOnlyList<AuditEventDto> Items, int Total, int Page, int PageSize);

public interface IAuditQueryService
{
    Task<AuditEventPageDto> QueryAsync(Guid tenantId, Guid storeId, string? action, string? entityType,
        DateOnly? fromDate, DateOnly? toDate, int page, int pageSize, CancellationToken cancellationToken);
}
