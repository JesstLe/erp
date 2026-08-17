using Microsoft.AspNetCore.Identity;

namespace Erp.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ApplicationRole : IdentityRole<Guid>
{
    public Guid TenantId { get; set; }
}

