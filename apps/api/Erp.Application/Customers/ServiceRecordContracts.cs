using Erp.Application.Common;

namespace Erp.Application.Customers;

public sealed record ServiceRecordAttachmentDto(Guid FileId, string FileName, string ContentType, long SizeBytes);
public sealed record ServiceRecordCorrectionDto(Guid Id, string Reason, string? ConditionNotes,
    string? ServiceContent, string? FollowUpNotes, Guid CorrectedBy, string CorrectedByName,
    DateTimeOffset CreatedAtUtc);

public sealed record ServiceRecordCategoryDto(Guid Id, string Code, string Name, int SortOrder, string Status,
    uint Version);

public sealed record ServiceRecordOverviewDto(Guid Id, Guid StoreId, Guid CustomerId, string CustomerName,
    string MaskedMobile, string HomeStoreName, Guid? CategoryId, string? CategoryCode, string? CategoryName,
    Guid? ServiceOrderId, string? ServiceOrderNo, DateTimeOffset ServiceOccurredAtUtc, string? ConditionNotes,
    string? ServiceContent, string? FollowUpNotes, Guid CreatedBy, string CreatedByName,
    DateTimeOffset CreatedAtUtc, int AttachmentCount, int CorrectionCount);

public sealed record ServiceRecordDto(Guid Id, Guid StoreId, Guid CustomerId, Guid? CategoryId,
    string? CategoryCode, string? CategoryName, Guid? ServiceOrderId, string? ServiceOrderNo,
    DateTimeOffset ServiceOccurredAtUtc, string? ConditionNotes, string? ServiceContent,
    string? FollowUpNotes, Guid CreatedBy, string CreatedByName, DateTimeOffset CreatedAtUtc,
    IReadOnlyList<ServiceRecordAttachmentDto> Attachments,
    IReadOnlyList<ServiceRecordCorrectionDto> Corrections);

public sealed record ServiceRecordOrderOptionDto(Guid Id, string OrderNo, string Status, DateTimeOffset CreatedAtUtc);

public sealed record CreateServiceRecordCommand(Guid StoreId, Guid CustomerId, Guid? ServiceOrderId,
    Guid? CategoryId, DateTimeOffset ServiceOccurredAtUtc, string? ConditionNotes, string? ServiceContent,
    string? FollowUpNotes, Guid CommandId, Guid OperatorId, IReadOnlyList<FileUploadInput> Images);
public sealed record CorrectServiceRecordCommand(Guid StoreId, Guid CustomerId, Guid ServiceRecordId,
    string Reason, string? ConditionNotes, string? ServiceContent, string? FollowUpNotes, Guid CommandId,
    Guid OperatorId);

public interface IServiceRecordService
{
    Task<PageResult<ServiceRecordDto>> ListAsync(Guid tenantId, Guid storeId, Guid customerId,
        int page, int pageSize, CancellationToken cancellationToken);
    Task<PageResult<ServiceRecordOverviewDto>> ListOverviewAsync(Guid tenantId, Guid storeId, Guid? categoryId,
        string? query, int page, int pageSize, CancellationToken cancellationToken);
    Task<IReadOnlyList<ServiceRecordCategoryDto>> ListCategoriesAsync(Guid tenantId,
        CancellationToken cancellationToken);
    Task<Result<ServiceRecordCategoryDto>> CreateCategoryAsync(Guid tenantId, string name, int sortOrder,
        Guid operatorId, CancellationToken cancellationToken);
    Task<Result<ServiceRecordCategoryDto>> UpdateCategoryAsync(Guid tenantId, Guid categoryId, string name,
        int sortOrder, bool isEnabled, uint expectedVersion, Guid operatorId,
        CancellationToken cancellationToken);
    Task<Result<bool>> DeleteCategoryAsync(Guid tenantId, Guid categoryId, uint expectedVersion,
        Guid operatorId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ServiceRecordOrderOptionDto>> ListOrderOptionsAsync(Guid tenantId, Guid storeId,
        Guid customerId, CancellationToken cancellationToken);
    Task<Result<ServiceRecordDto>> CreateAsync(Guid tenantId, CreateServiceRecordCommand command,
        CancellationToken cancellationToken);
    Task<Result<ServiceRecordDto>> CorrectAsync(Guid tenantId, CorrectServiceRecordCommand command,
        CancellationToken cancellationToken);
    Task<Result<StoredFileContent>> ReadImageAsync(Guid tenantId, Guid storeId, Guid customerId, Guid fileId,
        CancellationToken cancellationToken);
}
