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

public sealed class PlatformAdminUserRecord
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public string Account { get; set; } = string.Empty;
    public string NormalizedAccount { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public bool MustChangePassword { get; set; } = true;
    public int AccessFailedCount { get; set; }
    public DateTimeOffset? LockoutEndUtc { get; set; }
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public uint Version { get; set; }
}

public sealed class LoginSecurityEventRecord
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public string Scope { get; init; } = string.Empty;
    public Guid? TenantId { get; init; }
    public Guid? MerchantUserId { get; init; }
    public Guid? PlatformUserId { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string ResultCode { get; init; } = string.Empty;
    public byte[] AccountHash { get; init; } = [];
    public string AccountMask { get; init; } = string.Empty;
    public string IpAddress { get; init; } = string.Empty;
    public string UserAgentSummary { get; init; } = string.Empty;
    public string TraceId { get; init; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; init; }
}

public sealed class PlatformAuditEventRecord
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid PlatformUserId { get; init; }
    public string Action { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public Guid? EntityId { get; init; }
    public string? PreviousState { get; init; }
    public string? CurrentState { get; init; }
    public string? Reason { get; init; }
    public string TraceId { get; init; } = string.Empty;
    public string Metadata { get; init; } = "{}";
    public DateTimeOffset OccurredAtUtc { get; init; }
}
