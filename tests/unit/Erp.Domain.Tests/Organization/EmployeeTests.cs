using Erp.Domain.Common;
using Erp.Domain.Organization;

namespace Erp.Domain.Tests.Organization;

public sealed class EmployeeTests
{
    private static readonly Guid TenantId = Guid.CreateVersion7();

    [Fact]
    public void EmployeeNormalizesRequiredFieldsAndStartsActive()
    {
        var userId = Guid.CreateVersion7();
        var employee = new Employee(TenantId, " E0002 ", " 测试店长 ", " STORE_MANAGER ", userId);

        Assert.Equal("E0002", employee.EmployeeNo);
        Assert.Equal("测试店长", employee.DisplayName);
        Assert.Equal("STORE_MANAGER", employee.PositionCode);
        Assert.Equal(userId, employee.UserId);
        Assert.Equal(EmployeeStatus.Active, employee.Status);
    }

    [Fact]
    public void EmployeeRejectsInvalidRequiredFields()
    {
        Assert.Throws<DomainRuleException>(() => new Employee(TenantId, "1", "测试店长", "STORE_MANAGER", null));
        Assert.Throws<DomainRuleException>(() => new Employee(TenantId, "E0002", "A", "STORE_MANAGER", null));
    }

    [Fact]
    public void EmployeeDeactivationIsIdempotent()
    {
        var employee = new Employee(TenantId, "E0003", "前台员工", "FRONT_DESK", null);
        employee.Deactivate();
        employee.Deactivate();

        Assert.Equal(EmployeeStatus.Inactive, employee.Status);
    }
}
