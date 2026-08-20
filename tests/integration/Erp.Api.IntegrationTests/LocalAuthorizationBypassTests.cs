using Erp.Infrastructure.Security;
using Microsoft.Extensions.Hosting;

namespace Erp.Api.IntegrationTests;

public sealed class LocalAuthorizationBypassTests
{
    [Fact]
    public void ExplicitDevelopmentSettingEnablesBypass()
    {
        Assert.True(LocalAuthorizationBypass.IsEnabled(Environments.Development, "true"));
    }

    [Theory]
    [InlineData("false")]
    [InlineData(null)]
    public void DevelopmentRequiresExplicitSetting(string? configured)
    {
        Assert.False(LocalAuthorizationBypass.IsEnabled(Environments.Development, configured));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void NonDevelopmentEnvironmentsNeverEnableBypass(string environmentName)
    {
        Assert.False(LocalAuthorizationBypass.IsEnabled(environmentName, "true"));
    }
}
