using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Erp.LegacyMigration;

public sealed class EncryptedPayloadStore : IDisposable
{
    private static readonly byte[] Magic = "ERPLEG1"u8.ToArray();
    private const int NonceLength = 12;
    private const int TagLength = 16;

    private readonly byte[] _key;

    public EncryptedPayloadStore(ReadOnlySpan<byte> key)
    {
        if (key.Length != 32)
        {
            throw new ArgumentException("AES-256 key must contain 32 bytes.", nameof(key));
        }

        _key = key.ToArray();
    }

    public async Task WriteEncryptedTextAsync(
        string path,
        string text,
        CancellationToken cancellationToken)
    {
        var plaintext = Encoding.UTF8.GetBytes(text);
        try
        {
            var encrypted = Encrypt(plaintext);
            await SecureFile.WriteBytesAtomicAsync(path, encrypted, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public async Task WriteEncryptedBytesAsync(
        string path,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken)
    {
        var plaintext = bytes.ToArray();
        try
        {
            var encrypted = Encrypt(plaintext);
            await SecureFile.WriteBytesAtomicAsync(path, encrypted, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public async Task<byte[]> ReadEncryptedBytesAsync(string path, CancellationToken cancellationToken)
    {
        var encrypted = await File.ReadAllBytesAsync(path, cancellationToken);
        return Decrypt(encrypted);
    }

    public async Task<string> ReadEncryptedTextAsync(string path, CancellationToken cancellationToken)
    {
        var encrypted = await File.ReadAllBytesAsync(path, cancellationToken);
        var plaintext = Decrypt(encrypted);
        try
        {
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public void Dispose() => CryptographicOperations.ZeroMemory(_key);

    private byte[] Encrypt(ReadOnlySpan<byte> plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var tag = new byte[TagLength];
        var ciphertext = new byte[plaintext.Length];
        using (var aes = new AesGcm(_key, TagLength))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag, Magic);
        }

        var result = new byte[Magic.Length + nonce.Length + tag.Length + ciphertext.Length];
        Magic.CopyTo(result, 0);
        nonce.CopyTo(result, Magic.Length);
        tag.CopyTo(result, Magic.Length + nonce.Length);
        ciphertext.CopyTo(result, Magic.Length + nonce.Length + tag.Length);
        return result;
    }

    private byte[] Decrypt(ReadOnlySpan<byte> payload)
    {
        var minimumLength = Magic.Length + NonceLength + TagLength;
        if (payload.Length < minimumLength || !payload[..Magic.Length].SequenceEqual(Magic))
        {
            throw new LegacyMigrationException("加密导出文件格式无效。");
        }

        var nonce = payload.Slice(Magic.Length, NonceLength);
        var tag = payload.Slice(Magic.Length + NonceLength, TagLength);
        var ciphertext = payload[minimumLength..];
        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(_key, TagLength);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, Magic);
            return plaintext;
        }
        catch (CryptographicException exception)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new LegacyMigrationException("加密导出文件校验失败；密钥错误或文件已损坏。", exception);
        }
    }
}

public static class SecureOutputDirectory
{
    public static void Prepare(string outputDirectory)
    {
        var fullPath = Path.GetFullPath(outputDirectory);
        var current = new DirectoryInfo(Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath)!);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                throw new LegacyMigrationException("导出目录不能位于 Git 工作区内。");
            }

            current = current.Parent;
        }

        Directory.CreateDirectory(fullPath);
        Restrict(fullPath);
    }

    public static void Restrict(string directory)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}

public static class SecureFile
{
    public static async Task WriteBytesAtomicAsync(
        string path,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new LegacyMigrationException("输出文件缺少目录。");
        Directory.CreateDirectory(directory);
        SecureOutputDirectory.Restrict(directory);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes.ToArray(), cancellationToken);
            RestrictFile(temporaryPath);
            File.Move(temporaryPath, path, overwrite: true);
            RestrictFile(path);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    public static async Task WriteTextAtomicAsync(
        string path,
        string text,
        CancellationToken cancellationToken)
    {
        await WriteBytesAtomicAsync(path, Encoding.UTF8.GetBytes(text), cancellationToken);
    }

    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using var input = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(input, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    private static void RestrictFile(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}

public static partial class SensitiveText
{
    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var withoutPhones = PhonePattern().Replace(value, "$1****$2");
        return withoutPhones.Length <= 300 ? withoutPhones : withoutPhones[..300];
    }

    [GeneratedRegex(@"(?<!\d)(1\d{2})\d{4}(\d{4})(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();
}
