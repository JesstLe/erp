using Erp.Domain.Common;

namespace Erp.Domain.Facilities;

public enum FacilityLifecycleStatus { Enabled, Maintenance, Disabled }

public sealed class FacilityGroup : Entity
{
    private FacilityGroup() { }

    public FacilityGroup(Guid tenantId, Guid storeId, string displayName, int sortOrder) : base(tenantId)
    {
        StoreId = storeId;
        DisplayName = Required(displayName, 50, "设施分组名称");
        SortOrder = sortOrder;
    }

    public Guid StoreId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }

    private static string Required(string value, int max, string label)
    {
        var normalized = value.Trim();
        if (normalized.Length is 0 || normalized.Length > max) throw new DomainRuleException("VALIDATION_FAILED", $"{label}长度必须为1至{max}个字符");
        return normalized;
    }
}

public sealed class FacilityType : Entity
{
    private FacilityType() { }

    public FacilityType(Guid tenantId, string displayName) : base(tenantId)
    {
        DisplayName = displayName.Trim();
        if (DisplayName.Length is 0 or > 50) throw new DomainRuleException("VALIDATION_FAILED", "设施类型名称长度必须为1至50个字符");
    }

    public string DisplayName { get; private set; } = string.Empty;
}

public sealed class Facility : Entity
{
    private Facility() { }

    public Facility(Guid tenantId, Guid storeId, Guid groupId, Guid facilityTypeId, string code, string displayName,
        int sortOrder, int defaultCleaningMinutes, bool allowReservation) : base(tenantId)
    {
        StoreId = storeId;
        GroupId = groupId;
        FacilityTypeId = facilityTypeId;
        Code = Normalize(code, 40, "设施编号").ToUpperInvariant();
        DisplayName = Normalize(displayName, 50, "设施名称");
        if (defaultCleaningMinutes is < 0 or > 1440) throw new DomainRuleException("VALIDATION_FAILED", "默认清洁时长必须为0至1440分钟");
        SortOrder = sortOrder;
        DefaultCleaningMinutes = defaultCleaningMinutes;
        AllowReservation = allowReservation;
        LifecycleStatus = FacilityLifecycleStatus.Enabled;
    }

    public Guid StoreId { get; private set; }
    public Guid GroupId { get; private set; }
    public Guid FacilityTypeId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }
    public int DefaultCleaningMinutes { get; private set; }
    public bool AllowReservation { get; private set; }
    public FacilityLifecycleStatus LifecycleStatus { get; private set; }

    private static string Normalize(string value, int max, string label)
    {
        var normalized = value.Trim();
        if (normalized.Length is 0 || normalized.Length > max) throw new DomainRuleException("VALIDATION_FAILED", $"{label}长度必须为1至{max}个字符");
        return normalized;
    }
}
