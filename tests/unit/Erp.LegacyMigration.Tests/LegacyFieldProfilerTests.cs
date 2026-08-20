using System.Security.Cryptography;
using System.Text.Json;
using Erp.LegacyMigration;

namespace Erp.LegacyMigration.Tests;

public sealed class LegacyFieldProfilerTests
{
    [Fact]
    public async Task ProfilesStructureWithoutEmittingSourceValues()
    {
        await using var fixture = await ProfileFixture.CreateAsync(
            """
            {"uid":"secret-id-1","member_name":"Alice Secret","mobile":"13800138000","balance":"123.45","optional":""}
            {"uid":"secret-id-2","member_name":"Bob Secret","mobile":"13900139000","balance":"0"}
            """,
            rowCount: 2);

        var report = await fixture.ProfileAsync();
        var json = JsonSerializer.Serialize(report, LegacyFieldProfileReport.JsonOptions);

        Assert.Equal(1, report.EntityCount);
        Assert.Equal(2, report.TotalRows);
        Assert.DoesNotContain("Alice Secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Bob Secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("13800138000", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-id-1", json, StringComparison.Ordinal);

        var entity = Assert.Single(report.Entities);
        Assert.True(entity.IntegrityVerified);
        Assert.True(entity.Fields.Single(field => field.Field == "uid").CandidateKey);
        Assert.Equal("personal", entity.Fields.Single(field => field.Field == "member_name").Sensitivity);
        Assert.Equal("personal", entity.Fields.Single(field => field.Field == "mobile").Sensitivity);
        Assert.Equal("financial", entity.Fields.Single(field => field.Field == "balance").Sensitivity);

        var optional = entity.Fields.Single(field => field.Field == "optional");
        Assert.Equal(1, optional.PresenceCount);
        Assert.Equal(1, optional.MissingCount);
        Assert.Equal(1, optional.NullOrBlankCount);
    }

    [Fact]
    public async Task RejectsEncryptedRowsWhenManifestHashDoesNotMatch()
    {
        await using var fixture = await ProfileFixture.CreateAsync("{\"id\":\"1\"}\n", rowCount: 1);
        await File.AppendAllTextAsync(fixture.RowsPath, "tamper");

        await Assert.ThrowsAsync<LegacyMigrationException>(() => fixture.ProfileAsync());
    }

    [Fact]
    public async Task RejectsRowsFilePathTraversalFromManifest()
    {
        await using var fixture = await ProfileFixture.CreateAsync(
            "{\"id\":\"1\"}\n",
            rowCount: 1,
            rowsFile: "../outside.enc");

        await Assert.ThrowsAsync<LegacyMigrationException>(() => fixture.ProfileAsync());
    }

    [Fact]
    public async Task RejectsDuplicateJsonPropertyNames()
    {
        await using var fixture = await ProfileFixture.CreateAsync(
            "{\"id\":\"1\",\"id\":\"2\"}\n",
            rowCount: 1);

        await Assert.ThrowsAsync<LegacyMigrationException>(() => fixture.ProfileAsync());
    }

    private sealed class ProfileFixture : IAsyncDisposable
    {
        private readonly byte[] _key;

        private ProfileFixture(string root, string rowsPath, byte[] key)
        {
            Root = root;
            RowsPath = rowsPath;
            _key = key;
        }

        public string Root { get; }

        public string RowsPath { get; }

        public static async Task<ProfileFixture> CreateAsync(
            string rows,
            int rowCount,
            string rowsFile = "rows.jsonl.enc")
        {
            var root = Path.Combine(Path.GetTempPath(), $"erp-legacy-profile-{Guid.NewGuid():N}");
            var entityDirectory = Path.Combine(root, "customers");
            Directory.CreateDirectory(entityDirectory);
            var key = RandomNumberGenerator.GetBytes(32);
            var actualRowsPath = Path.Combine(entityDirectory, "rows.jsonl.enc");
            using (var payloadStore = new EncryptedPayloadStore(key))
            {
                await payloadStore.WriteEncryptedTextAsync(actualRowsPath, rows, CancellationToken.None);
            }

            var hash = await SecureFile.Sha256Async(actualRowsPath, CancellationToken.None);
            var manifest = new LegacyExportManifest(
                SchemaVersion: 1,
                RunId: Guid.NewGuid(),
                Entity: "customers",
                SourceHost: LegacyEndpointPolicy.Origin.Host,
                Endpoint: "/swshop/base/member.php?act=grid",
                StartedAtUtc: DateTimeOffset.UtcNow,
                CompletedAtUtc: DateTimeOffset.UtcNow,
                PageSize: 100,
                PageCount: 1,
                RowCount: rowCount,
                SourceRecords: rowCount,
                Encryption: "AES-256-GCM/ERPLEG1",
                RowsFile: rowsFile,
                RowsSha256: hash,
                Pages: []);
            await SecureFile.WriteTextAtomicAsync(
                Path.Combine(entityDirectory, "manifest.json"),
                JsonSerializer.Serialize(manifest, LegacyFieldProfileReport.JsonOptions),
                CancellationToken.None);
            return new ProfileFixture(root, actualRowsPath, key);
        }

        public async Task<LegacyFieldProfileReport> ProfileAsync()
        {
            using var payloadStore = new EncryptedPayloadStore(_key);
            var profiler = new LegacyFieldProfiler(payloadStore, TextWriter.Null);
            return await profiler.ProfileAsync([Root], CancellationToken.None);
        }

        public ValueTask DisposeAsync()
        {
            CryptographicOperations.ZeroMemory(_key);
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
