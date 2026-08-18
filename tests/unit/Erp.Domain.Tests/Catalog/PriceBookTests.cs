using Erp.Domain.Catalog;
using Erp.Domain.Common;

namespace Erp.Domain.Tests.Catalog;

public sealed class PriceBookTests
{
    [Fact]
    public void PublishWithoutLinesIsRejected()
    {
        var book = new PriceBook(Guid.CreateVersion7(), "V1标准价", new DateOnly(2026, 8, 18));

        var exception = Assert.Throws<DomainRuleException>(() => book.Publish(DateTimeOffset.UtcNow));

        Assert.Equal("VALIDATION_FAILED", exception.Code);
    }

    [Fact]
    public void PublishWithLineFreezesFurtherChanges()
    {
        var book = new PriceBook(Guid.CreateVersion7(), "V1标准价", new DateOnly(2026, 8, 18));
        var itemId = Guid.CreateVersion7();
        book.SetPrice(itemId, 10_000);

        book.Publish(DateTimeOffset.UtcNow);

        Assert.Equal(PriceBookStatus.Published, book.Status);
        var exception = Assert.Throws<DomainRuleException>(() => book.SetPrice(itemId, 9_000));
        Assert.Equal("STATE_TRANSITION_NOT_ALLOWED", exception.Code);
    }

    [Fact]
    public void SetPriceUsesMinorUnitsAndReplacesDraftLine()
    {
        var book = new PriceBook(Guid.CreateVersion7(), "V1标准价", new DateOnly(2026, 8, 18));
        var itemId = Guid.CreateVersion7();

        book.SetPrice(itemId, 10_000);
        book.SetPrice(itemId, 12_800);

        var line = Assert.Single(book.Lines);
        Assert.Equal(12_800, line.UnitPriceMinor);
    }

    [Fact]
    public void ProductOnlyPriceBookCanBePublishedWithoutServiceBinding()
    {
        var book = new PriceBook(Guid.CreateVersion7(), "单独商品价", new DateOnly(2026, 8, 18));
        book.SetProductPrice(Guid.CreateVersion7(), 2_500);

        book.Publish(DateTimeOffset.UtcNow);

        Assert.Equal(PriceBookStatus.Published, book.Status);
        Assert.Empty(book.Lines);
        Assert.Single(book.ProductLines);
    }

    [Fact]
    public void DraftCanBeEditedAndCancelledButPublishedVersionCannot()
    {
        var book = new PriceBook(Guid.CreateVersion7(), "原草稿", new DateOnly(2026, 8, 18));
        book.SetPrice(Guid.CreateVersion7(), 5_000);
        book.UpdateDraft("新草稿", new DateOnly(2026, 9, 1));
        book.CancelDraft();

        Assert.Equal("新草稿", book.Name);
        Assert.Equal(new DateOnly(2026, 9, 1), book.EffectiveFrom);
        Assert.Equal(PriceBookStatus.Retired, book.Status);
        Assert.Throws<DomainRuleException>(() => book.UpdateDraft("不能修改", new DateOnly(2026, 9, 2)));
    }
}
