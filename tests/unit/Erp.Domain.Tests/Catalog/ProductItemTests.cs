using Erp.Domain.Catalog;
using Erp.Domain.Common;

namespace Erp.Domain.Tests.Catalog;

public sealed class ProductItemTests
{
    private static readonly Guid TenantId = Guid.CreateVersion7();

    [Fact]
    public void ProductNormalizesCodeAndRetainsInventoryAttribute()
    {
        var product = new ProductItem(TenantId, " pd001 ", "护理套装", "套", true);

        Assert.Equal("PD001", product.Code);
        Assert.Equal("护理套装", product.Name);
        Assert.Equal("套", product.UnitName);
        Assert.True(product.TrackInventory);
        Assert.Equal(CatalogItemStatus.Enabled, product.Status);
    }

    [Fact]
    public void ProductRejectsMissingRequiredFields()
    {
        Assert.Throws<DomainRuleException>(() => new ProductItem(TenantId, "", "护理套装", "套", false));
        Assert.Throws<DomainRuleException>(() => new ProductItem(TenantId, "PD001", "护理套装", "", false));
    }

    [Fact]
    public void PriceBookCanSnapshotProductPriceAlongsideServicePrice()
    {
        var book = new PriceBook(TenantId, "综合标准价", new DateOnly(2026, 8, 18));
        book.SetPrice(Guid.CreateVersion7(), 10_000);
        book.SetProductPrice(Guid.CreateVersion7(), 6_800);
        book.Publish(DateTimeOffset.UtcNow);

        Assert.Single(book.Lines);
        Assert.Single(book.ProductLines);
        Assert.Equal(6_800, book.ProductLines.Single().UnitPriceMinor);
        Assert.Equal(PriceBookStatus.Published, book.Status);
    }
}
