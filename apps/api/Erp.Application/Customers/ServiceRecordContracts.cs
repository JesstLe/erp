using Erp.Application.Common;

namespace Erp.Application.Customers;

public sealed record ServiceRecordAttachmentDto(Guid FileId, string FileName, string ContentType, long SizeBytes);

public sealed record ServiceRecordDto(Guid Id, Guid StoreId, Guid CustomerId, Guid? ServiceOrderId,
    string? ServiceOrderNo, DateTimeOffset ServiceOccurredAtUtc, string? ConditionNotes, string? ServiceContent,
    string? FollowUpNotes, Guid CreatedBy, string CreatedByName, DateTimeOffset CreatedAtUtc,
    IReadOnlyList<ServiceRecordAttachmentDto> Attachments);

public sealed record ServiceRecordOrderOptionDto(Guid Id, string OrderNo, string Status, DateTimeOffset CreatedAtUtc);

public sealed record CreateServiceRecordCommand(Guid StoreId, Guid CustomerId, Guid? ServiceOrderId,
    DateTimeOffset ServiceOccurredAtUtc, string? ConditionNotes, string? ServiceContent, string? FollowUpNotes,
    Guid CommandId, Guid OperatorId, IReadOnlyList<FileUploadInput> Images);

public interface IServiceRecordService
{
    Task<IReadOnlyList<ServiceRecordDto>> ListAsync(Guid tenantId, Guid storeId, Guid customerId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ServiceRecordOrderOptionDto>> ListOrderOptionsAsync(Guid tenantId, Guid storeId,
        Guid customerId, CancellationToken cancellationToken);
    Task<Result<ServiceRecordDto>> CreateAsync(Guid tenantId, CreateServiceRecordCommand command,
        CancellationToken cancellationToken);
    Task<Result<StoredFileContent>> ReadImageAsync(Guid tenantId, Guid storeId, Guid customerId, Guid fileId,
        CancellationToken cancellationToken);
}
