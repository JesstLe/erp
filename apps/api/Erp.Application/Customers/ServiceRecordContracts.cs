using Erp.Application.Common;

namespace Erp.Application.Customers;

public sealed record ServiceRecordAttachmentDto(Guid FileId, string FileName, string ContentType, long SizeBytes);
public sealed record ServiceRecordCorrectionDto(Guid Id, string Reason, string? ConditionNotes,
    string? ServiceContent, string? FollowUpNotes, Guid CorrectedBy, string CorrectedByName,
    DateTimeOffset CreatedAtUtc);

public sealed record ServiceRecordDto(Guid Id, Guid StoreId, Guid CustomerId, Guid? ServiceOrderId,
    string? ServiceOrderNo, DateTimeOffset ServiceOccurredAtUtc, string? ConditionNotes, string? ServiceContent,
    string? FollowUpNotes, Guid CreatedBy, string CreatedByName, DateTimeOffset CreatedAtUtc,
    IReadOnlyList<ServiceRecordAttachmentDto> Attachments,
    IReadOnlyList<ServiceRecordCorrectionDto> Corrections);

public sealed record ServiceRecordOrderOptionDto(Guid Id, string OrderNo, string Status, DateTimeOffset CreatedAtUtc);

public sealed record CreateServiceRecordCommand(Guid StoreId, Guid CustomerId, Guid? ServiceOrderId,
    DateTimeOffset ServiceOccurredAtUtc, string? ConditionNotes, string? ServiceContent, string? FollowUpNotes,
    Guid CommandId, Guid OperatorId, IReadOnlyList<FileUploadInput> Images);
public sealed record CorrectServiceRecordCommand(Guid StoreId, Guid CustomerId, Guid ServiceRecordId,
    string Reason, string? ConditionNotes, string? ServiceContent, string? FollowUpNotes, Guid CommandId,
    Guid OperatorId);

public interface IServiceRecordService
{
    Task<PageResult<ServiceRecordDto>> ListAsync(Guid tenantId, Guid storeId, Guid customerId,
        int page, int pageSize, CancellationToken cancellationToken);
    Task<IReadOnlyList<ServiceRecordOrderOptionDto>> ListOrderOptionsAsync(Guid tenantId, Guid storeId,
        Guid customerId, CancellationToken cancellationToken);
    Task<Result<ServiceRecordDto>> CreateAsync(Guid tenantId, CreateServiceRecordCommand command,
        CancellationToken cancellationToken);
    Task<Result<ServiceRecordDto>> CorrectAsync(Guid tenantId, CorrectServiceRecordCommand command,
        CancellationToken cancellationToken);
    Task<Result<StoredFileContent>> ReadImageAsync(Guid tenantId, Guid storeId, Guid customerId, Guid fileId,
        CancellationToken cancellationToken);
}
