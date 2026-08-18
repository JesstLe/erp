using System.Security.Cryptography;
using System.Globalization;
using Erp.Application.Common;
using Erp.Domain.Common;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Erp.Infrastructure.Files;

public sealed class SecureFileStorage
{
    public const long MaximumImageBytes = 5 * 1024 * 1024;
    private readonly string rootPath;
    private readonly IDataProtector protector;

    public SecureFileStorage(IConfiguration configuration, IHostEnvironment environment,
        IDataProtectionProvider dataProtectionProvider)
    {
        var configuredRoot = configuration["FileStorage:RootPath"];
        if (string.IsNullOrWhiteSpace(configuredRoot) && !environment.IsDevelopment())
            throw new InvalidOperationException("生产环境必须配置独立持久化目录 FileStorage:RootPath");
        rootPath = Path.GetFullPath(string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(environment.ContentRootPath, "App_Data", "uploads")
            : configuredRoot);
        Directory.CreateDirectory(rootPath);
        protector = dataProtectionProvider.CreateProtector("Erp.StoredFiles.v1");
    }

    public async Task<StoredFileRecord> StoreImageAsync(Guid tenantId, Guid? storeId, string purpose,
        Guid operatorId, FileUploadInput input, CancellationToken cancellationToken)
    {
        if (input.DeclaredLength <= 0 || input.DeclaredLength > MaximumImageBytes)
            throw new DomainRuleException("FILE_TOO_LARGE", "图片不能为空且单张不能超过5MB");

        var content = await ReadLimitedAsync(input.Content, cancellationToken);
        var contentType = DetectContentType(content);
        var id = Guid.CreateVersion7();
        var storageKey = Path.Combine(tenantId.ToString("N"), DateTimeOffset.UtcNow.ToString("yyyyMM", CultureInfo.InvariantCulture),
            $"{id:N}.blob");
        var fullPath = ResolveStoragePath(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var protectedContent = protector.Protect(content);
        try
        {
            await using var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous | FileOptions.WriteThrough);
            await stream.WriteAsync(protectedContent, cancellationToken);
        }
        catch (IOException exception)
        {
            throw new SecureFileStorageException("FILE_STORAGE_UNAVAILABLE", "图片暂时无法保存", exception);
        }

        return new StoredFileRecord
        {
            Id = id,
            TenantId = tenantId,
            StoreId = storeId,
            Purpose = purpose,
            StorageKey = storageKey.Replace(Path.DirectorySeparatorChar, '/'),
            OriginalFileName = NormalizeFileName(input.FileName),
            ContentType = contentType,
            SizeBytes = content.LongLength,
            Sha256 = SHA256.HashData(content),
            CreatedBy = operatorId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    public async Task<StoredFileContent> ReadAsync(StoredFileRecord record, CancellationToken cancellationToken)
    {
        try
        {
            var protectedContent = await File.ReadAllBytesAsync(ResolveStoragePath(record.StorageKey), cancellationToken);
            var content = protector.Unprotect(protectedContent);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(content), record.Sha256))
                throw new SecureFileStorageException("FILE_INTEGRITY_FAILED", "图片完整性校验失败");
            return new StoredFileContent(record.ContentType, content);
        }
        catch (SecureFileStorageException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or CryptographicException)
        {
            throw new SecureFileStorageException("FILE_STORAGE_UNAVAILABLE", "图片暂时无法读取", exception);
        }
    }

    public Task TryDeleteUncommittedAsync(StoredFileRecord record)
    {
        try { File.Delete(ResolveStoragePath(record.StorageKey)); }
        catch (IOException) { }
        return Task.CompletedTask;
    }

    private static async Task<byte[]> ReadLimitedAsync(Stream source, CancellationToken cancellationToken)
    {
        await using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (output.Length + read > MaximumImageBytes)
                throw new DomainRuleException("FILE_TOO_LARGE", "单张图片不能超过5MB");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (output.Length == 0)
            throw new DomainRuleException("VALIDATION_FAILED", "图片不能为空");
        return output.ToArray();
    }

    private string ResolveStoragePath(string storageKey)
    {
        var normalizedKey = storageKey.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(rootPath, normalizedKey));
        var rootPrefix = rootPath.EndsWith(Path.DirectorySeparatorChar) ? rootPath : rootPath + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, StringComparison.Ordinal))
            throw new SecureFileStorageException("FILE_STORAGE_KEY_INVALID", "图片存储标识无效");
        return fullPath;
    }

    private static string DetectContentType(byte[] content)
    {
        if (content.Length >= 3 && content[0] == 0xff && content[1] == 0xd8 && content[2] == 0xff)
            return "image/jpeg";
        if (content.Length >= 8 && content.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }))
            return "image/png";
        if (content.Length >= 12 && content.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
            content.AsSpan(8, 4).SequenceEqual("WEBP"u8))
            return "image/webp";
        throw new DomainRuleException("FILE_TYPE_NOT_ALLOWED", "只允许上传JPEG、PNG或WebP图片");
    }

    private static string NormalizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName).Trim();
        name = string.Concat(name.Where(character => !char.IsControl(character)));
        if (string.IsNullOrWhiteSpace(name)) name = "image";
        return name.Length <= 180 ? name : name[..180];
    }
}

public sealed class SecureFileStorageException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}
