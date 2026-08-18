namespace Erp.Application.Common;

public sealed record FileUploadInput(string FileName, string? DeclaredContentType, long DeclaredLength,
    Stream Content);

public sealed record StoredFileContent(string ContentType, byte[] Content);
