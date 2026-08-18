using Erp.Application.Security;

namespace Erp.Domain.Tests.Security;

public sealed class SystemPermissionsTests
{
    [Fact]
    public void ResolveCombinesMultipleRolesWithoutLosingPermissions()
    {
        var permissions = SystemPermissions.Resolve([SystemRoles.FrontDesk, SystemRoles.Cashier]);

        Assert.Contains(SystemPermissions.FacilityOperate, permissions);
        Assert.Contains(SystemPermissions.CashierCheckout, permissions);
        Assert.DoesNotContain(SystemPermissions.InventoryRead, permissions);
    }

    [Fact]
    public void StoreManagerDoesNotReceiveOwnerOnlyConfiguration()
    {
        var permissions = SystemPermissions.Resolve([SystemRoles.StoreManager]);

        Assert.Contains(SystemPermissions.FacilityConfigure, permissions);
        Assert.Contains(SystemPermissions.ReportRead, permissions);
        Assert.DoesNotContain(SystemPermissions.EmployeeManage, permissions);
        Assert.DoesNotContain(SystemPermissions.PaymentChannelManage, permissions);
    }

    [Fact]
    public void UnknownRolesFailClosed()
    {
        Assert.Empty(SystemPermissions.Resolve(["UNKNOWN_ROLE"]));
    }

    [Fact]
    public void RestrictedRolesDoNotReceiveUnrelatedPages()
    {
        var frontDesk = SystemPermissions.Resolve([SystemRoles.FrontDesk]);
        var cashier = SystemPermissions.Resolve([SystemRoles.Cashier]);
        var technician = SystemPermissions.Resolve([SystemRoles.Technician]);

        Assert.DoesNotContain(SystemPermissions.CashierCheckout, frontDesk);
        Assert.DoesNotContain(SystemPermissions.InventoryRead, cashier);
        Assert.DoesNotContain(SystemPermissions.CustomerRead, technician);
        Assert.All(SystemPermissions.Resolve([
            SystemRoles.StoreManager, SystemRoles.FrontDesk, SystemRoles.Cashier, SystemRoles.Technician,
        ]), permission => Assert.Contains(permission, SystemPermissions.All));
    }
}
