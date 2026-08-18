namespace Erp.Infrastructure.Files;

public sealed class StoredFileRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? StoreId { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public byte[] Sha256 { get; set; } = [];
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public static class StoredFilePurposes
{
    public const string ProductImage = "ProductImage";
    public const string ServiceRecordImage = "ServiceRecordImage";
}
