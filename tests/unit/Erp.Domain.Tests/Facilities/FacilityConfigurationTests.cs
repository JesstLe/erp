using Erp.Domain.Common;
using Erp.Domain.Facilities;

namespace Erp.Domain.Tests.Facilities;

public sealed class FacilityConfigurationTests
{
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid StoreId = Guid.CreateVersion7();
    private static readonly Guid GroupId = Guid.CreateVersion7();
    private static readonly Guid TypeId = Guid.CreateVersion7();

    [Fact]
    public void OptionalBusinessDescriptionAndReferencePriceMayBeEmpty()
    {
        var facility = new Facility(TenantId, StoreId, GroupId, TypeId, "A01", "A01服务位",
            10, 0, false);

        Assert.Null(facility.ServiceName);
        Assert.Null(facility.EquipmentName);
        Assert.Null(facility.ReferencePriceMinor);
    }

    [Fact]
    public void UpdateChangesDisplayConfigurationWithoutChangingStore()
    {
        var facility = new Facility(TenantId, StoreId, GroupId, TypeId, "A01", "A01服务位",
            10, 0, false);
        var nextGroupId = Guid.CreateVersion7();

        facility.Update(nextGroupId, TypeId, "B02", "B02服务位", 20, 15, true,
            "基础服务", "通用设备", 12_800, FacilityLifecycleStatus.Maintenance);

        Assert.Equal(StoreId, facility.StoreId);
        Assert.Equal(nextGroupId, facility.GroupId);
        Assert.Equal("基础服务", facility.ServiceName);
        Assert.Equal("通用设备", facility.EquipmentName);
        Assert.Equal(12_800, facility.ReferencePriceMinor);
        Assert.Equal(FacilityLifecycleStatus.Maintenance, facility.LifecycleStatus);
    }

    [Fact]
    public void InvalidReferencePriceIsRejected()
    {
        var exception = Assert.Throws<DomainRuleException>(() => new Facility(TenantId, StoreId, GroupId,
            TypeId, "A01", "A01服务位", 10, 0, false, referencePriceMinor: -1));

        Assert.Equal("VALIDATION_FAILED", exception.Code);
    }

    [Fact]
    public void ServiceAreaCanBeRenamedAndReordered()
    {
        var group = new FacilityGroup(TenantId, StoreId, "一楼服务区", 10);

        group.Update("二楼服务区", 20);

        Assert.Equal("二楼服务区", group.DisplayName);
        Assert.Equal(20, group.SortOrder);
    }
}
