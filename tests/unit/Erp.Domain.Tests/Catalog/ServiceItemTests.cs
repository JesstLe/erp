using Erp.Domain.Catalog;
using Erp.Domain.Common;

namespace Erp.Domain.Tests.Catalog;

public sealed class ServiceItemTests
{
    [Fact]
    public void CreateWithValidValuesUsesEnabledStatus()
    {
        var item = new ServiceItem(Guid.CreateVersion7(), "SV001", "基础服务", 60);

        Assert.Equal("SV001", item.Code);
        Assert.Equal("基础服务", item.Name);
        Assert.Equal(60, item.StandardDurationMinutes);
        Assert.Equal(CatalogItemStatus.Enabled, item.Status);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1441)]
    public void CreateWithInvalidDurationIsRejected(int minutes)
    {
        var exception = Assert.Throws<DomainRuleException>(
            () => new ServiceItem(Guid.CreateVersion7(), "SV001", "基础服务", minutes));

        Assert.Equal("VALIDATION_FAILED", exception.Code);
    }

    [Fact]
    public void ItemCanBeUpdatedDisabledAndRestoredWithoutChangingCode()
    {
        var item = new ServiceItem(Guid.CreateVersion7(), "SV001", "基础服务", 60);

        item.Update("进阶服务", 90);
        item.Disable();

        Assert.Equal("SV001", item.Code);
        Assert.Equal("进阶服务", item.Name);
        Assert.Equal(90, item.StandardDurationMinutes);
        Assert.Equal(CatalogItemStatus.Disabled, item.Status);

        item.Enable();
        Assert.Equal(CatalogItemStatus.Enabled, item.Status);
    }

    [Fact]
    public void CommissionRuleAcceptsPercentageOrFixedAmountExclusively()
    {
        var item = new ServiceItem(Guid.CreateVersion7(), "SV001", "基础服务", 60);

        item.ConfigureCommission(CommissionMode.Percentage, 1_250, null);
        Assert.Equal(CommissionMode.Percentage, item.CommissionMode);
        Assert.Equal(1_250, item.CommissionRateBasisPoints);

        item.ConfigureCommission(CommissionMode.FixedAmount, null, 2_000);
        Assert.Equal(CommissionMode.FixedAmount, item.CommissionMode);
        Assert.Equal(2_000, item.CommissionFixedMinor);

        Assert.Throws<DomainRuleException>(() =>
            item.ConfigureCommission(CommissionMode.Percentage, 1_250, 2_000));
    }
}
