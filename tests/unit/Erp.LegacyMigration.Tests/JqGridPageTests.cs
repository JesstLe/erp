using Erp.LegacyMigration;

namespace Erp.LegacyMigration.Tests;

public sealed class JqGridPageTests
{
    [Fact]
    public void ParsesNumericAndStringPaginationFields()
    {
        const string json = """
            {"page":"2","total":3,"records":"5","rows":[{"id":"a"},{"id":"b"}]}
            """;

        var page = JqGridPage.Parse(json, requestedPage: 2);

        Assert.Equal(2, page.Page);
        Assert.Equal(3, page.TotalPages);
        Assert.Equal(5, page.Records);
        Assert.Equal(2, page.RowCount);
        Assert.Equal(2, JqGridPage.EnumerateRows(json).Count());
    }

    [Fact]
    public void RejectsPayloadWithoutRowsArray()
    {
        const string json = "{\"page\":1,\"total\":1,\"records\":0}";

        Assert.Throws<LegacyMigrationException>(() => JqGridPage.Parse(json, requestedPage: 1));
    }

    [Theory]
    [InlineData("[{\"id\":1},{\"id\":2}]")]
    [InlineData("{\"data\":[{\"id\":1},{\"id\":2}]}")]
    [InlineData("{\"items\":[{\"id\":1},{\"id\":2}]}")]
    [InlineData("{\"list\":[{\"id\":1},{\"id\":2}]}")]
    public void AcceptsReviewedReadOnlyListShapes(string json)
    {
        var page = JqGridPage.Parse(json, requestedPage: 1);

        Assert.Equal(2, page.RowCount);
        Assert.Equal(2, page.Records);
        Assert.Equal(2, JqGridPage.EnumerateRows(json).Count());
    }

    [Fact]
    public void AcceptsLegacyEmptyStringRowsOnlyWhenRecordCountIsZero()
    {
        const string json = "{\"records\":0,\"page\":\"1\",\"total\":0,\"rows\":\"\"}";

        var page = JqGridPage.Parse(json, requestedPage: 1);

        Assert.Equal(0, page.RowCount);
        Assert.Empty(JqGridPage.EnumerateRows(json));
    }

    [Fact]
    public void AcceptsJsonEncodedRowsArray()
    {
        const string json = "{\"records\":1,\"page\":\"1\",\"total\":1,\"rows\":\"[{\\\"id\\\":1}]\"}";

        var page = JqGridPage.Parse(json, requestedPage: 1);

        Assert.Equal(1, page.RowCount);
        Assert.Single(JqGridPage.EnumerateRows(json));
    }

    [Fact]
    public void RejectsNonJsonStringRowsWhenRecordsExist()
    {
        const string json = "{\"records\":1,\"page\":\"1\",\"total\":1,\"rows\":\"unexpected\"}";

        Assert.Throws<LegacyMigrationException>(() => JqGridPage.Parse(json, requestedPage: 1));
    }
}
