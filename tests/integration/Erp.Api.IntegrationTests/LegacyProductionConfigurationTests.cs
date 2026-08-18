using Erp.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Erp.Api.IntegrationTests;

public sealed class LegacyProductionConfigurationTests
{
    [Fact]
    public void LegacyProductionHostDerivesStablePurposeSeparatedPlatformPeppers()
    {
        const string legacyRoot = "legacy-production-customer-pepper-1234567890";
        var first = BuildLegacyConfiguration(legacyRoot);
        var second = BuildLegacyConfiguration(legacyRoot);

        new ServiceCollection().AddErpInfrastructure(first, new ProductionHostEnvironment());
        new ServiceCollection().AddErpInfrastructure(second, new ProductionHostEnvironment());

        var securityEvents = first["SecurityEvents:AccountHashPepper"];
        var registration = first["PlatformRegistration:ContactHashPepper"];
        Assert.NotNull(securityEvents);
        Assert.NotNull(registration);
        Assert.Equal(64, securityEvents.Length);
        Assert.Equal(64, registration.Length);
        Assert.NotEqual(securityEvents, registration);
        Assert.Equal(securityEvents, second["SecurityEvents:AccountHashPepper"]);
        Assert.Equal(registration, second["PlatformRegistration:ContactHashPepper"]);
        Assert.DoesNotContain(legacyRoot, securityEvents, StringComparison.Ordinal);
        Assert.DoesNotContain(legacyRoot, registration, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitInvalidPurposePepperStillFailsClosed()
    {
        var configuration = BuildLegacyConfiguration("legacy-production-customer-pepper-1234567890",
            new Dictionary<string, string?>
            {
                ["SecurityEvents:AccountHashPepper"] = "CHANGE_ME",
            });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddErpInfrastructure(configuration, new ProductionHostEnvironment()));

        Assert.Contains("SecurityEvents:AccountHashPepper", exception.Message, StringComparison.Ordinal);
    }

    private static IConfigurationRoot BuildLegacyConfiguration(string rootPepper,
        IReadOnlyDictionary<string, string?>? additional = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:ErpDatabase"] = "Host=127.0.0.1;Database=erp;Username=erp;Password=test",
            ["CustomerPrivacy:LookupPepper"] = rootPepper,
            ["MemberVerification:CodePepper"] = "legacy-production-member-pepper-1234567890",
            ["FileStorage:RootPath"] = Path.Combine(Path.GetTempPath(), "erp-legacy-files"),
            ["DataProtection:KeyRingPath"] = Path.Combine(Path.GetTempPath(), "erp-legacy-keys"),
        };
        if (additional is not null)
            foreach (var (key, value) in additional) values[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private sealed class ProductionHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Erp.Api.IntegrationTests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
