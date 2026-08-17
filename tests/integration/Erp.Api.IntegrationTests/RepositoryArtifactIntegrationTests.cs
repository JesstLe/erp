using System.Text.RegularExpressions;

namespace Erp.Api.IntegrationTests;

public sealed partial class RepositoryArtifactIntegrationTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void DatabaseMigrationsAreUniquelyVersionedAndNonDestructive()
    {
        var migrationDirectory = Path.Combine(RepositoryRoot, "db", "migrations");
        var migrations = Directory.GetFiles(migrationDirectory, "V*.sql").Order().ToList();

        Assert.NotEmpty(migrations);
        var versions = migrations.Select(path => MigrationNamePattern().Match(Path.GetFileName(path)))
            .Select(match => match.Success ? match.Groups[1].Value : string.Empty).ToList();
        Assert.DoesNotContain(string.Empty, versions);
        Assert.Equal(versions.Count, versions.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(versions.Order(StringComparer.Ordinal), versions);
        Assert.All(migrations, path =>
        {
            var sql = File.ReadAllText(path);
            Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void MergedUserManualReferencesExistingScreenshots()
    {
        var manualPath = Path.Combine(RepositoryRoot, "docs", "user-manual", "ERP-V1-user-manual.md");
        var manual = File.ReadAllText(manualPath);
        var imagePaths = MarkdownImagePattern().Matches(manual).Select(match => match.Groups[1].Value).ToList();

        Assert.NotEmpty(imagePaths);
        Assert.All(imagePaths, relativePath =>
            Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(manualPath)!, relativePath)),
                $"用户手册截图不存在：{relativePath}"));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ERP.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("无法定位 ERP 仓库根目录");
    }

    [GeneratedRegex("^V([0-9]+)__[a-z0-9_]+\\.sql$", RegexOptions.CultureInvariant)]
    private static partial Regex MigrationNamePattern();

    [GeneratedRegex("!\\[[^\\]]*\\]\\((assets/[^)]+)\\)", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownImagePattern();
}
