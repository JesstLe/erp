using System.Security.Cryptography;
using System.Text;
using Erp.LegacyMigration;

namespace Erp.LegacyMigration.Tests;

public sealed class EncryptedPayloadStoreTests
{
    [Fact]
    public async Task EncryptsBusinessPayloadAndAuthenticatesIt()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"erp-legacy-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "page.json.enc");
        var key = RandomNumberGenerator.GetBytes(32);

        try
        {
            using var store = new EncryptedPayloadStore(key);
            const string plaintext = "{\"name\":\"测试顾客\",\"mobile\":\"13800138000\"}";

            await store.WriteEncryptedTextAsync(path, plaintext, CancellationToken.None);

            var encrypted = await File.ReadAllBytesAsync(path);
            Assert.DoesNotContain("测试顾客", Encoding.UTF8.GetString(encrypted), StringComparison.Ordinal);
            Assert.Equal(plaintext, await store.ReadEncryptedTextAsync(path, CancellationToken.None));

            encrypted[^1] ^= 0x01;
            await File.WriteAllBytesAsync(path, encrypted);
            await Assert.ThrowsAsync<LegacyMigrationException>(
                () => store.ReadEncryptedTextAsync(path, CancellationToken.None));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
