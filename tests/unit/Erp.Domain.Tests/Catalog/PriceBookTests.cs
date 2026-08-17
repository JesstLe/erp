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
}
