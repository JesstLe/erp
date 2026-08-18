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
    public async Task<PageResult<ServiceRecordDto>> ListAsync(Guid tenantId, Guid storeId, Guid customerId,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        var customerIds = await CustomerGroupIdsAsync(tenantId, customerId, cancellationToken);
        var query = dbContext.ServiceRecords.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.StoreId == storeId && customerIds.Contains(x.CustomerId));
        var total = await query.CountAsync(cancellationToken);
        var records = await query.AsSplitQuery().Include(x => x.Attachments)
            .OrderByDescending(x => x.ServiceOccurredAtUtc).ThenByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PageResult<ServiceRecordDto>(await MapAsync(records, cancellationToken), total, page, pageSize);
    }

    public async Task<IReadOnlyList<ServiceRecordOrderOptionDto>> ListOrderOptionsAsync(Guid tenantId, Guid storeId,
        Guid customerId, CancellationToken cancellationToken)
    {
        var customerIds = await CustomerGroupIdsAsync(tenantId, customerId, cancellationToken);
        return await dbContext.ServiceOrders.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.StoreId == storeId && x.CustomerId.HasValue &&
                customerIds.Contains(x.CustomerId.Value) &&
                x.Status != ServiceOrderStatus.Voided)
            .OrderByDescending(x => x.CreatedAtUtc).Take(50)
            .Select(x => new ServiceRecordOrderOptionDto(x.Id, x.OrderNo, x.Status.ToString(), x.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

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
        var customerIds = await CustomerGroupIdsAsync(tenantId, customerId, cancellationToken);
        var isAttached = await dbContext.ServiceRecordAttachments.AsNoTracking().AnyAsync(attachment =>
            attachment.FileId == fileId && dbContext.ServiceRecords.Any(record => record.Id == attachment.ServiceRecordId &&
                record.TenantId == tenantId && record.StoreId == storeId && customerIds.Contains(record.CustomerId)),
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

    public async Task<Result<ServiceRecordDto>> CorrectAsync(Guid tenantId, CorrectServiceRecordCommand command,
        CancellationToken cancellationToken)
    {
        var existingCorrection = await dbContext.ServiceRecordCorrections.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.CommandId == command.CommandId,
                cancellationToken);
        var customerIds = await CustomerGroupIdsAsync(tenantId, command.CustomerId, cancellationToken);
        var record = await dbContext.ServiceRecords.AsNoTracking().AsSplitQuery().Include(x => x.Attachments)
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.StoreId == command.StoreId &&
                x.Id == command.ServiceRecordId && customerIds.Contains(x.CustomerId), cancellationToken);
        if (record is null)
            return ResultFactory.Failure<ServiceRecordDto>("SERVICE_RECORD_NOT_FOUND", "服务档案不存在");
        if (existingCorrection is not null)
            return ResultFactory.Success((await MapAsync([record], cancellationToken)).Single());
        try
        {
            var correction = new ServiceRecordCorrection(tenantId, record.Id, command.Reason,
                command.ConditionNotes, command.ServiceContent, command.FollowUpNotes, command.CommandId,
                command.OperatorId);
            dbContext.ServiceRecordCorrections.Add(correction);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "customer.service_record.correct",
                "ServiceRecord", record.Id);
            await dbContext.SaveChangesAsync(cancellationToken);
            return ResultFactory.Success((await MapAsync([record], cancellationToken)).Single());
        }
        catch (DomainRuleException exception)
        {
            return ResultFactory.Failure<ServiceRecordDto>(exception.Code, exception.Message);
        }
        catch (DbUpdateException)
        {
            foreach (var entry in dbContext.ChangeTracker.Entries().Where(x => x.State == EntityState.Added))
                entry.State = EntityState.Detached;
            var repeated = await dbContext.ServiceRecordCorrections.AsNoTracking()
                .AnyAsync(x => x.TenantId == tenantId && x.CommandId == command.CommandId, cancellationToken);
            if (repeated) return ResultFactory.Success((await MapAsync([record], cancellationToken)).Single());
            throw;
        }
    }

    private async Task<IReadOnlyList<ServiceRecordDto>> MapAsync(IReadOnlyList<ServiceRecord> records,
        CancellationToken cancellationToken)
    {
        var fileIds = records.SelectMany(x => x.Attachments).Select(x => x.FileId).Distinct().ToArray();
        var files = await dbContext.StoredFiles.AsNoTracking().Where(x => fileIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var recordIds = records.Select(x => x.Id).ToArray();
        var corrections = await dbContext.ServiceRecordCorrections.AsNoTracking()
            .Where(x => recordIds.Contains(x.ServiceRecordId)).OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        var userIds = records.Select(x => x.CreatedBy).Concat(corrections.Select(x => x.CorrectedBy))
            .Distinct().ToArray();
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
            }).ToList(), corrections.Where(x => x.ServiceRecordId == record.Id).Select(correction =>
                new ServiceRecordCorrectionDto(correction.Id, correction.Reason, correction.ConditionNotes,
                    correction.ServiceContent, correction.FollowUpNotes, correction.CorrectedBy,
                    userNames.GetValueOrDefault(correction.CorrectedBy, "未知人员"), correction.CreatedAtUtc))
                .ToList())).ToList();
    }

    private async Task<List<Guid>> CustomerGroupIdsAsync(Guid tenantId, Guid customerId,
        CancellationToken cancellationToken) => await dbContext.Customers.AsNoTracking()
        .Where(x => x.TenantId == tenantId && (x.Id == customerId || x.MergedIntoCustomerId == customerId))
        .Select(x => x.Id).ToListAsync(cancellationToken);

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
