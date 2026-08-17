using Erp.Domain.Common;

namespace Erp.Domain.Authorization;

public sealed class RoleActionGrant : Entity
{
    private RoleActionGrant()
    {
    }

    public RoleActionGrant(Guid tenantId, Guid roleId, string action)
        : base(tenantId)
    {
        RoleId = roleId;
        Action = action.Trim();
        if (Action.Length is 0 or > 128)
        {
            throw new DomainRuleException("VALIDATION_FAILED", "权限动作长度不正确");
        }
    }

    public Guid RoleId { get; private set; }

    public string Action { get; private set; } = string.Empty;
}
