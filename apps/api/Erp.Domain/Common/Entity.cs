namespace Erp.Domain.Common;

public abstract class Entity
{
    protected Entity(Guid tenantId)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    protected Entity()
    {
    }

    public Guid Id { get; protected set; }

    public Guid TenantId { get; protected set; }

    public DateTimeOffset CreatedAtUtc { get; protected set; }

    public DateTimeOffset UpdatedAtUtc { get; protected set; }

    public uint Version { get; protected set; }

    protected void Touch() => UpdatedAtUtc = DateTimeOffset.UtcNow;
}

