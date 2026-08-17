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
}
