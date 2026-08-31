using Erp.Application.LegacyMigration;
using Erp.LegacyMigration;

namespace Erp.LegacyMigration.Tests;

public sealed class LegacyStoreAttributionPlanTests
{
    [Fact]
    public void ImportOptionsAcceptExplicitCanonicalStoreMapping()
    {
        var options = LegacyImportOptions.Parse(
        [
            "import", "--input", Path.GetTempPath(), "--tenant", "B01", "--store-map", "1=S001"
        ]);

        Assert.Equal("S001", Assert.Single(options.StoreMappings!).Value);
    }

    [Fact]
    public void ImportOptionsRequireExactConfirmationForProductionTenant()
    {
        var input = Path.GetTempPath();

        var missingConfirmation = Assert.Throws<LegacyMigrationException>(() => LegacyImportOptions.Parse(
        [
            "import", "--input", input, "--tenant", "B2026082001",
        ]));
        Assert.Contains("--confirm-target", missingConfirmation.Message, StringComparison.Ordinal);

        var options = LegacyImportOptions.Parse(
        [
            "import", "--input", input, "--tenant", "B2026082001",
            "--confirm-target", "B2026082001", "--store-map", "1=S001",
            "--sync-mapped-stores", "--reconcile-existing-customers",
        ]);

        Assert.Equal("B2026082001", options.ConfirmedTargetTenantCode);
        Assert.True(options.SyncMappedStores);
        Assert.True(options.ReconcileExistingCustomers);
    }

    [Fact]
    public void ImportOptionsRejectDuplicateTargetStoreCodes()
    {
        var exception = Assert.Throws<LegacyMigrationException>(() => LegacyImportOptions.Parse(
        [
            "import", "--input", Path.GetTempPath(), "--tenant", "B01",
            "--store-map", "1=S001", "--store-map", "2=S001",
        ]));

        Assert.Contains("唯一", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolvesSourceIdCodeAndNameWithoutLeakingOtherFields()
    {
        var rows = new[]
        {
            Row("stores", "1", ("shop_code", "A01"), ("shop_name", "总店")),
            Row("stores", "2", ("shop_code", "A02"), ("shop_name", "分店")),
            Row("customers", "101", ("member_shop", "总店"), ("member_name", "不应输出")),
            Row("customers", "102", ("member_shop", "A02")),
            Row("employees", "201", ("emplee_shop", "1")),
            Row("care-records", "301", ("bill_shop", "2"))
        };
        var dataset = new LegacyImportDataset("B01", "test", new string('a', 64), "test", rows, []);

        var plan = LegacyStoreAttributionPlanBuilder.Build(dataset);

        Assert.Equal("1", plan.Customers.Single(x => x.SourceId == "101").SourceStoreId);
        Assert.Equal("2", plan.Customers.Single(x => x.SourceId == "102").SourceStoreId);
        Assert.Equal("1", Assert.Single(plan.Employees).SourceStoreId);
        Assert.Equal("2", Assert.Single(plan.CareRecords).SourceStoreId);
        Assert.DoesNotContain("不应输出", System.Text.Json.JsonSerializer.Serialize(plan), StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsUnknownStoreInsteadOfFallingBack()
    {
        var dataset = new LegacyImportDataset("B01", "test", new string('b', 64), "test",
        [
            Row("stores", "1", ("shop_code", "A01"), ("shop_name", "总店")),
            Row("customers", "101", ("member_shop", "不存在")),
            Row("employees", "201", ("emplee_shop", "1")),
            Row("care-records", "301", ("bill_shop", "1"))
        ], []);

        Assert.Throws<LegacyMigrationException>(() => LegacyStoreAttributionPlanBuilder.Build(dataset));
    }

    private static LegacySourceRow Row(string entity, string sourceId, params (string Key, string Value)[] fields) =>
        new(entity, sourceId, new string('c', 64), fields.ToDictionary(x => x.Key, x => (string?)x.Value));
}
