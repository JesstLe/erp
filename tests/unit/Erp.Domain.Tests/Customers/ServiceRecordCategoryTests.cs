using Erp.Domain.Common;
using Erp.Domain.Customers;

namespace Erp.Domain.Tests.Customers;

public sealed class ServiceRecordCategoryTests
{
    [Fact]
    public void BrandCanRenameAndDisableCategoryWithoutChangingGeneratedCode()
    {
        var category = new ServiceRecordCategory(Guid.NewGuid(), "CARE000001", "售后回访", 30);

        category.Update("重点客户回访", 10);
        category.Disable();

        Assert.Equal("CARE000001", category.Code);
        Assert.Equal("重点客户回访", category.Name);
        Assert.Equal(10, category.SortOrder);
        Assert.Equal(ServiceRecordCategoryStatus.Disabled, category.Status);
    }

    [Fact]
    public void CategoryRejectsInvalidSortOrder()
    {
        var exception = Assert.Throws<DomainRuleException>(() =>
            new ServiceRecordCategory(Guid.NewGuid(), "CARE000001", "回访", 10_000));

        Assert.Equal("VALIDATION_FAILED", exception.Code);
    }

    [Fact]
    public void ServiceRecordMayReferenceCategoryOrRemainUnclassified()
    {
        var now = DateTimeOffset.UtcNow;
        var categoryId = Guid.NewGuid();
        var classified = new ServiceRecord(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, now,
            null, null, null, Guid.NewGuid(), Guid.NewGuid(), now, categoryId);
        var unclassified = new ServiceRecord(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, now,
            null, null, null, Guid.NewGuid(), Guid.NewGuid(), now);

        Assert.Equal(categoryId, classified.CategoryId);
        Assert.Null(unclassified.CategoryId);
    }
}
