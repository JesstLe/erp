using System.Data;
using Erp.Application.Common;
using Erp.Application.Customers;
using Erp.Domain.Cashier;
using Erp.Domain.Common;
using Erp.Domain.Customers;
using Erp.Infrastructure.Files;
using Erp.Infrastructure.Identity;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Erp.Infrastructure.Customers;

public sealed class ServiceRecordService(ErpDbContext dbContext, SecureFileStorage fileStorage,
    IHttpContextAccessor httpContextAccessor) : IServiceRecordService
{
    public async Task<IReadOnlyList<ServiceRecordDto>> ListAsync(Guid tenantId, Guid storeId, Guid customerId,
        CancellationToken cancellationToken)
    {
        var records = await dbContext.ServiceRecords.AsNoTracking().AsSplitQuery()
            .Include(x => x.Attachments)
            .Where(x => x.TenantId == tenantId && x.StoreId == storeId && x.CustomerId == customerId)
            .OrderByDescending(x => x.ServiceOccurredAtUtc).ThenByDescending(x => x.CreatedAtUtc)
            .Take(100).ToListAsync(cancellationToken);
        return await MapAsync(records, cancellationToken);
    }

    public async Task<IReadOnlyList<ServiceRecordOrderOptionDto>> ListOrderOptionsAsync(Guid tenantId, Guid storeId,
        Guid customerId, CancellationToken cancellationToken)
        => await dbContext.ServiceOrders.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.StoreId == storeId && x.CustomerId == customerId &&
                x.Status != ServiceOrderStatus.Voided)
            .OrderByDescending(x => x.CreatedAtUtc).Take(50)
            .Select(x => new ServiceRecordOrderOptionDto(x.Id, x.OrderNo, x.Status.ToString(), x.CreatedAtUtc))
            .ToListAsync(cancellationToken);

    public async Task<Result<ServiceRecordDto>> CreateAsync(Guid tenantId, CreateServiceRecordCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Images.Count > 6)
            return ResultFactory.Failure<ServiceRecordDto>("VALIDATION_FAILED", "每条服务记录最多上传6张图片");
        var existing = await dbContext.ServiceRecords.AsNoTracking().AsSplitQuery().Include(x => x.Attachments)
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.CommandId == command.CommandId,
                cancellationToken);
        if (existing is not null)
            return ResultFactory.Success((await MapAsync([existing], cancellationToken)).Single());

        var customerExists = await dbContext.Customers.AsNoTracking().AnyAsync(x => x.TenantId == tenantId &&
            x.Id == command.CustomerId && x.HomeStoreId == command.StoreId, cancellationToken);
        if (!customerExists)
            return ResultFactory.Failure<ServiceRecordDto>("CUSTOMER_NOT_FOUND", "顾客档案不存在或不属于当前门店");

        if (command.ServiceOrderId.HasValue)
        {
            var orderExists = await dbContext.ServiceOrders.AsNoTracking().AnyAsync(x => x.TenantId == tenantId &&
                x.StoreId == command.StoreId && x.Id == command.ServiceOrderId.Value &&
                x.CustomerId == command.CustomerId, cancellationToken);
            if (!orderExists)
                return ResultFactory.Failure<ServiceRecordDto>("SERVICE_ORDER_NOT_FOUND", "关联消费单不存在或不属于该顾客");
        }

        var storedFiles = new List<StoredFileRecord>();
        try
        {
            var record = new ServiceRecord(tenantId, command.StoreId, command.CustomerId, command.ServiceOrderId,
                command.ServiceOccurredAtUtc, command.ConditionNotes, command.ServiceContent, command.FollowUpNotes,
                command.CommandId, command.OperatorId, DateTimeOffset.UtcNow);
            foreach (var image in command.Images)
            {
                var stored = await fileStorage.StoreImageAsync(tenantId, command.StoreId,
                    StoredFilePurposes.ServiceRecordImage, command.OperatorId, image, cancellationToken);
                storedFiles.Add(stored);
                record.AttachImage(stored.Id);
            }

            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable,
                cancellationToken);
            dbContext.StoredFiles.AddRange(storedFiles);
            dbContext.ServiceRecords.Add(record);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "customer.service_record.create",
                "ServiceRecord", record.Id);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success((await MapAsync([record], cancellationToken)).Single());
        }
        catch (DomainRuleException exception)
        {
            await CleanupAsync(storedFiles);
            return ResultFactory.Failure<ServiceRecordDto>(exception.Code, exception.Message);
        }
        catch (SecureFileStorageException exception)
        {
            await CleanupAsync(storedFiles);
            return ResultFactory.Failure<ServiceRecordDto>(exception.Code, exception.Message);
        }
        catch
        {
            await CleanupAsync(storedFiles);
            throw;
        }
    }

    public async Task<Result<StoredFileContent>> ReadImageAsync(Guid tenantId, Guid storeId, Guid customerId,
        Guid fileId, CancellationToken cancellationToken)
    {
        var isAttached = await dbContext.ServiceRecordAttachments.AsNoTracking().AnyAsync(attachment =>
            attachment.FileId == fileId && dbContext.ServiceRecords.Any(record => record.Id == attachment.ServiceRecordId &&
                record.TenantId == tenantId && record.StoreId == storeId && record.CustomerId == customerId),
            cancellationToken);
        if (!isAttached)
            return ResultFactory.Failure<StoredFileContent>("FILE_NOT_FOUND", "服务记录图片不存在");
        var file = await dbContext.StoredFiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == fileId &&
            x.TenantId == tenantId && x.StoreId == storeId && x.Purpose == StoredFilePurposes.ServiceRecordImage,
            cancellationToken);
        if (file is null)
            return ResultFactory.Failure<StoredFileContent>("FILE_NOT_FOUND", "服务记录图片不存在");
        try { return ResultFactory.Success(await fileStorage.ReadAsync(file, cancellationToken)); }
        catch (SecureFileStorageException exception)
        { return ResultFactory.Failure<StoredFileContent>(exception.Code, exception.Message); }
    }

    private async Task<IReadOnlyList<ServiceRecordDto>> MapAsync(IReadOnlyList<ServiceRecord> records,
        CancellationToken cancellationToken)
    {
        var fileIds = records.SelectMany(x => x.Attachments).Select(x => x.FileId).Distinct().ToArray();
        var files = await dbContext.StoredFiles.AsNoTracking().Where(x => fileIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var userIds = records.Select(x => x.CreatedBy).Distinct().ToArray();
        var userNames = await dbContext.Set<ApplicationUser>().AsNoTracking().Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken);
        var orderIds = records.Where(x => x.ServiceOrderId.HasValue).Select(x => x.ServiceOrderId!.Value)
            .Distinct().ToArray();
        var orderNos = await dbContext.ServiceOrders.AsNoTracking().Where(x => orderIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.OrderNo, cancellationToken);

        return records.Select(record => new ServiceRecordDto(record.Id, record.StoreId, record.CustomerId,
            record.ServiceOrderId, record.ServiceOrderId.HasValue ? orderNos.GetValueOrDefault(record.ServiceOrderId.Value) : null,
            record.ServiceOccurredAtUtc, record.ConditionNotes, record.ServiceContent, record.FollowUpNotes,
            record.CreatedBy, userNames.GetValueOrDefault(record.CreatedBy, "未知人员"), record.CreatedAtUtc,
            record.Attachments.OrderBy(x => x.SortOrder).Where(x => files.ContainsKey(x.FileId)).Select(x =>
            {
                var file = files[x.FileId];
                return new ServiceRecordAttachmentDto(file.Id, file.OriginalFileName, file.ContentType, file.SizeBytes);
            }).ToList())).ToList();
    }

    private async Task CleanupAsync(IEnumerable<StoredFileRecord> files)
    {
        foreach (var file in files) await fileStorage.TryDeleteUncommittedAsync(file);
    }

    private void AddAudit(Guid tenantId, Guid storeId, Guid operatorId, string action, string entityType,
        Guid entityId) => dbContext.AuditEvents.Add(new AuditEventRecord
    {
        TenantId = tenantId,
        StoreId = storeId,
        OperatorId = operatorId,
        Action = action,
        EntityType = entityType,
        EntityId = entityId,
        CurrentState = "Created",
        TraceId = httpContextAccessor.HttpContext?.TraceIdentifier ?? "background",
        OccurredAtUtc = DateTimeOffset.UtcNow,
    });
}
