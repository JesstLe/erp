using Erp.Domain.Catalog;
using Erp.Domain.Common;

namespace Erp.Domain.Organization;

public sealed class PositionServiceCommission : Entity
{
    private PositionServiceCommission() { }

    public PositionServiceCommission(Guid tenantId, Guid positionId, Guid serviceItemId, int rateBasisPoints)
        : base(tenantId)
    {
        if (positionId == Guid.Empty || serviceItemId == Guid.Empty || rateBasisPoints is < 0 or > 10_000)
            throw new DomainRuleException("VALIDATION_FAILED", "岗位、服务项目或提成比例无效");
        PositionId = positionId;
        ServiceItemId = serviceItemId;
        RateBasisPoints = rateBasisPoints;
    }

    public Guid PositionId { get; private set; }
    public Guid ServiceItemId { get; private set; }
    public int RateBasisPoints { get; private set; }
}

public sealed record ResolvedServiceCommission(CommissionMode Mode, int? RateBasisPoints, long? FixedMinor,
    string? PositionCode = null, string? PositionName = null, string Source = "ServiceItem")
{
    public static ResolvedServiceCommission Resolve(ServiceItem item, EmployeePosition? position,
        PositionServiceCommission? rule)
    {
        if (position is not null && position.TenantId != item.TenantId || rule is not null &&
            (position is null || rule.TenantId != item.TenantId || rule.PositionId != position.Id ||
                rule.ServiceItemId != item.Id))
            throw new DomainRuleException("VALIDATION_FAILED", "提成规则不属于当前品牌、岗位或项目");
        var rate = rule?.RateBasisPoints ?? position?.DefaultCommissionRateBasisPoints;
        var source = rule is not null ? "PositionService" : rate.HasValue ? "PositionDefault" : "ServiceItem";
        return rate.HasValue
            ? new(rate.Value == 0 ? CommissionMode.None : CommissionMode.Percentage,
                rate.Value == 0 ? null : rate, null, position?.Code, position?.Name, source)
            : new(item.CommissionMode, item.CommissionRateBasisPoints, item.CommissionFixedMinor,
                position?.Code, position?.Name, source);
    }
}
