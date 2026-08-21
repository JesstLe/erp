using Erp.LegacyMigration;

namespace Erp.LegacyMigration.Tests;

public sealed class LegacyEndpointPolicyTests
{
    private readonly LegacyEndpointPolicy _policy = new();

    [Fact]
    public void AllowsOnlyReviewedCustomerGridGet()
    {
        var uri = new Uri("https://app5.siweicloud.com/swshop/base/member.php?act=grid&page=1&rows=100");

        _policy.EnsureAllowed(HttpMethod.Get, uri);
    }

    [Fact]
    public void AllowsReviewedCareGridGet()
    {
        _policy.EnsureAllowed(HttpMethod.Get,
            new Uri("https://app5.siweicloud.com/swshop/vip/nurse.php?act=grid&page=1&rows=100"));
    }

    [Fact]
    public void AllowsReviewedCarePagePreflightGet()
    {
        _policy.EnsureAllowed(HttpMethod.Get,
            new Uri("https://app5.siweicloud.com/swshop/vip/nurse.php"));
    }

    [Fact]
    public void AllowsOnlyReviewedCareGridInitializationPost()
    {
        _policy.EnsureAllowed(HttpMethod.Post,
            new Uri("https://app5.siweicloud.com/swshop/vip/nurse.php?act=custom"));

        Assert.Throws<LegacyMigrationException>(() => _policy.EnsureAllowed(
            HttpMethod.Post,
            new Uri("https://app5.siweicloud.com/swshop/vip/nurse.php?act=custom&extra=1")));
    }

    [Fact]
    public void CareGridUriIncludesReviewedFullHistoryFilters()
    {
        var uri = LegacyEntityDefinition.CareRecords.BuildPageUri(2, 100);

        Assert.Equal("https", uri.Scheme);
        Assert.Equal("app5.siweicloud.com", uri.Host);
        Assert.Equal("/swshop/vip/nurse.php", uri.AbsolutePath);
        Assert.Contains("act=grid", uri.Query, StringComparison.Ordinal);
        Assert.Contains("page=2", uri.Query, StringComparison.Ordinal);
        Assert.Contains("rows=100", uri.Query, StringComparison.Ordinal);
        Assert.Contains("search_bdate=2019-01-01", uri.Query, StringComparison.Ordinal);
        Assert.Contains("search_edate=", uri.Query, StringComparison.Ordinal);
        Assert.Contains("search_find=Y", uri.Query, StringComparison.Ordinal);
        Assert.Contains("search_shop=0", uri.Query, StringComparison.Ordinal);
        Assert.Contains("search_nusort=0", uri.Query, StringComparison.Ordinal);
        _policy.EnsureAllowed(HttpMethod.Get, uri);
    }

    [Theory]
    [InlineData("https://app5.siweicloud.com/swshop/base/member.php?act=adds&wintop=N&winpid=2&id=2259")]
    [InlineData("https://app5.siweicloud.com/swshop/picture/21091626/member/example_1.jpg")]
    [InlineData("https://app5.siweicloud.com/swshop/vip/nurse.php?act=adds&wintop=N&winpid=1&id=1672")]
    [InlineData("https://app5.siweicloud.com/swshop/picture/21091626/nurse/example_1.jpg")]
    public void AllowsOnlyReviewedCustomerPhotoGets(string value)
    {
        _policy.EnsureAllowed(HttpMethod.Get, new Uri(value));
    }

    [Theory]
    [InlineData("https://app5.siweicloud.com/swshop/base/member.php?act=adds&id=2259")]
    [InlineData("https://app5.siweicloud.com/swshop/base/member.php?act=adds&wintop=N&winpid=2&id=abc")]
    [InlineData("https://app5.siweicloud.com/swshop/picture/21091626/member/../secret.jpg")]
    [InlineData("https://app5.siweicloud.com/swshop/picture/21091626/member/example.exe")]
    [InlineData("https://app5.siweicloud.com/swshop/vip/nurse.php?act=adds&wintop=N&winpid=2&id=1672")]
    public void RejectsUnreviewedCustomerPhotoGets(string value)
    {
        Assert.Throws<LegacyMigrationException>(() => _policy.EnsureAllowed(HttpMethod.Get, new Uri(value)));
    }

    [Theory]
    [InlineData("shop")]
    [InlineData("emplee")]
    [InlineData("service")]
    [InlineData("product")]
    [InlineData("numcard")]
    [InlineData("iclevel")]
    [InlineData("icfull")]
    [InlineData("room")]
    [InlineData("brand")]
    [InlineData("unit")]
    [InlineData("ework")]
    [InlineData("source")]
    public void AllowsRegisteredBaseMasterGridGet(string controller)
    {
        var uri = new Uri($"https://app5.siweicloud.com/swshop/base/{controller}.php?act=grid&page=1&rows=100");

        _policy.EnsureAllowed(HttpMethod.Get, uri);
    }

    [Theory]
    [InlineData("http://app5.siweicloud.com/swshop/base/member.php?act=grid")]
    [InlineData("https://example.com/swshop/base/member.php?act=grid")]
    [InlineData("https://app5.siweicloud.com/swshop/base/member.php?act=drop")]
    [InlineData("https://app5.siweicloud.com/swshop/base/member.php?act=grid&act=drop")]
    [InlineData("https://app5.siweicloud.com/swshop/print/frame.php?act=vip_sell_list")]
    public void RejectsUnreviewedOrDangerousGet(string value)
    {
        var exception = Assert.Throws<LegacyMigrationException>(
            () => _policy.EnsureAllowed(HttpMethod.Get, new Uri(value)));

        Assert.Contains("拒绝", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsPostToReadEndpoint()
    {
        var uri = new Uri("https://app5.siweicloud.com/swshop/base/member.php?act=grid");

        Assert.Throws<LegacyMigrationException>(() => _policy.EnsureAllowed(HttpMethod.Post, uri));
    }

    [Fact]
    public void AllowsOnlyExactLoginPost()
    {
        var uri = new Uri("https://app5.siweicloud.com/swshop/login/login.php?act=login");

        _policy.EnsureAllowed(HttpMethod.Post, uri);
    }

    [Fact]
    public void ResolvesBaseMasterSelectionWithoutCustomers()
    {
        var entities = LegacyEntityCatalog.Resolve(LegacyEntityCatalog.BaseMasterSelection);

        Assert.Equal(12, entities.Count);
        Assert.DoesNotContain(entities, entity => entity == LegacyEntityDefinition.Customers);
        Assert.Equal(entities.Count, entities.Select(entity => entity.Path).Distinct().Count());
    }
}
