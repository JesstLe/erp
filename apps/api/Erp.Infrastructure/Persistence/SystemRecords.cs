namespace Erp.Infrastructure.Persistence;

public sealed class AuditEventRecord
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid TenantId { get; init; }
    public Guid? StoreId { get; init; }
    public Guid? OperatorId { get; init; }
    public string Action { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public Guid? EntityId { get; init; }
    public string? PreviousState { get; init; }
    public string? CurrentState { get; init; }
    public string? Reason { get; init; }
    public string TraceId { get; init; } = string.Empty;
    public Guid? RequestId { get; init; }
    public string Metadata { get; init; } = "{}";
    public DateTimeOffset OccurredAtUtc { get; init; }
}

public sealed class IdempotencyCommandRecord
{
    public Guid CommandId { get; init; }
    public Guid TenantId { get; init; }
    public Guid OperatorId { get; init; }
    public byte[] RequestHash { get; init; } = [];
    public int? ResponseStatus { get; set; }
    public string? ResponseBody { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}
