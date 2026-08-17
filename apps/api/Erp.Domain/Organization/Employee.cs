using Erp.Domain.Common;

namespace Erp.Domain.Organization;

public enum EmployeeStatus
{
    Active,
    Inactive,
}

public sealed class Employee : Entity
{
    private Employee()
    {
    }

    public Employee(Guid tenantId, string employeeNo, string displayName, string positionCode, Guid? userId)
        : base(tenantId)
    {
        EmployeeNo = NormalizeRequired(employeeNo, 2, 32, "INVALID_EMPLOYEE_NO", "员工工号长度必须为2到32个字符");
        DisplayName = NormalizeRequired(displayName, 2, 100, "INVALID_EMPLOYEE_NAME", "员工姓名长度必须为2到100个字符");
        PositionCode = NormalizeRequired(positionCode, 2, 40, "INVALID_POSITION", "岗位长度必须为2到40个字符");
        UserId = userId;
        Status = EmployeeStatus.Active;
    }

    public string EmployeeNo { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string PositionCode { get; private set; } = string.Empty;

    public Guid? UserId { get; private set; }

    public EmployeeStatus Status { get; private set; }

    public void Deactivate()
    {
        if (Status == EmployeeStatus.Inactive) return;
        Status = EmployeeStatus.Inactive;
        Touch();
    }

    private static string NormalizeRequired(string value, int minLength, int maxLength, string code, string message)
    {
        var normalized = value.Trim();
        if (normalized.Length < minLength || normalized.Length > maxLength)
            throw new DomainRuleException(code, message);
        return normalized;
    }
}

public sealed class EmployeeStore : Entity
{
    private EmployeeStore()
    {
    }

    public EmployeeStore(Guid tenantId, Guid employeeId, Guid storeId, bool isPrimary)
        : base(tenantId)
    {
        if (employeeId == Guid.Empty || storeId == Guid.Empty)
            throw new DomainRuleException("INVALID_EMPLOYEE_STORE", "员工和门店不能为空");
        EmployeeId = employeeId;
        StoreId = storeId;
        IsPrimary = isPrimary;
    }

    public Guid EmployeeId { get; private set; }

    public Guid StoreId { get; private set; }

    public bool IsPrimary { get; private set; }
}
