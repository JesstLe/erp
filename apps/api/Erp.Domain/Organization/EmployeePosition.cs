using Erp.Domain.Common;

namespace Erp.Domain.Organization;

public sealed class EmployeePosition : Entity
{
    private EmployeePosition()
    {
    }

    public EmployeePosition(Guid tenantId, string code, string name, int sortOrder = 0)
        : base(tenantId)
    {
        Code = Normalize(code, 40, "岗位编码");
        Name = Normalize(name, 60, "岗位名称");
        SetSortOrder(sortOrder);
        Status = EmployeePositionStatus.Enabled;
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public int SortOrder { get; private set; }

    public EmployeePositionStatus Status { get; private set; }

    public void Update(string name, int sortOrder)
    {
        Name = Normalize(name, 60, "岗位名称");
        SetSortOrder(sortOrder);
        Touch();
    }

    public void Enable()
    {
        Status = EmployeePositionStatus.Enabled;
        Touch();
    }

    public void Disable()
    {
        Status = EmployeePositionStatus.Disabled;
        Touch();
    }

    private void SetSortOrder(int sortOrder)
    {
        if (sortOrder is < 0 or > 9999)
            throw new DomainRuleException("VALIDATION_FAILED", "岗位排序必须在0到9999之间");
        SortOrder = sortOrder;
    }

    private static string Normalize(string value, int maxLength, string displayName)
    {
        var normalized = value.Trim();
        if (normalized.Length is < 2 or > 60 || normalized.Length > maxLength)
            throw new DomainRuleException("VALIDATION_FAILED", $"{displayName}长度必须为2到{maxLength}个字符");
        return normalized;
    }
}

public enum EmployeePositionStatus
{
    Enabled = 1,
    Disabled = 2,
}
