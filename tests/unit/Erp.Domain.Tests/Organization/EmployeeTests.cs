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

    [Fact]
    public void EmployeeCanReactivateAndUpdateProfile()
    {
        var employee = new Employee(TenantId, "E0004", "服务员工", "TECHNICIAN", null);
        employee.Deactivate();

        Assert.Throws<DomainRuleException>(() => employee.UpdateProfile("新姓名", "FRONT_DESK"));

        employee.Reactivate();
        employee.UpdateProfile(" 新姓名 ", " FRONT_DESK ");

        Assert.Equal(EmployeeStatus.Active, employee.Status);
        Assert.Equal("新姓名", employee.DisplayName);
        Assert.Equal("FRONT_DESK", employee.PositionCode);
    }

    [Fact]
    public void StoreAndTenantProfilesSupportSafeLifecycleChanges()
    {
        var tenant = new Tenant(" brand01 ", " 原品牌 ");
        tenant.UpdateProfile("new_brand", " 新品牌 ");
        var store = new Store(tenant.Id, "s01", "原门店");
        store.UpdateProfile(" new_store ", " 新门店 ", "Asia/Shanghai");
        store.Disable();
        store.Enable();

        Assert.Equal("NEW_BRAND", tenant.Code);
        Assert.Equal("新品牌", tenant.Name);
        Assert.Equal("NEW_STORE", store.Code);
        Assert.Equal("新门店", store.Name);
        Assert.Equal(StoreStatus.Enabled, store.Status);
    }
}
