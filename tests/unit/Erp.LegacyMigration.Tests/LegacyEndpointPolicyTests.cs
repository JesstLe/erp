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
