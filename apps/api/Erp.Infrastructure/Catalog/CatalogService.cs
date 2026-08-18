using Erp.Application.Catalog;
using Erp.Application.Common;
using Erp.Domain.Catalog;
using Erp.Domain.Common;
using Erp.Infrastructure.Persistence;
using Erp.Infrastructure.Files;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Erp.Infrastructure.Catalog;

public sealed class CatalogService(ErpDbContext dbContext, IHttpContextAccessor httpContextAccessor,
    SecureFileStorage fileStorage) : ICatalogService
{
    public async Task<IReadOnlyList<ServiceItemDto>> ListServiceItemsAsync(Guid tenantId, string? query,
        CatalogItemStatus? status, CancellationToken cancellationToken)
    {
        var normalizedQuery = query?.Trim();
        var items = dbContext.ServiceItems.AsNoTracking().Where(x => x.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(normalizedQuery))
            items = items.Where(x => x.Code.Contains(normalizedQuery) || x.Name.Contains(normalizedQuery));
        if (status.HasValue) items = items.Where(x => x.Status == status.Value);
        return await items
            .OrderBy(x => x.Code)
            .Select(x => new ServiceItemDto(x.Id, x.Code, x.Name, x.StandardDurationMinutes, x.Status.ToString().ToUpperInvariant(), x.Version))
            .ToListAsync(cancellationToken);
    }

    public async Task<Result<ServiceItemDto>> CreateServiceItemAsync(Guid tenantId, CreateServiceItemCommand command, CancellationToken cancellationToken)
    {
        if (await dbContext.ServiceItems.AnyAsync(x => x.TenantId == tenantId && x.Code == command.Code.Trim(), cancellationToken))
        {
            return ResultFactory.Failure<ServiceItemDto>("DUPLICATE_CODE", "项目编码已经存在");
        }

        try
        {
            var item = new ServiceItem(tenantId, command.Code, command.Name, command.StandardDurationMinutes);
            dbContext.ServiceItems.Add(item);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "catalog.service_item.create", "ServiceItem", item.Id,
                null, item.Status.ToString());
            await dbContext.SaveChangesAsync(cancellationToken);
            return ResultFactory.Success(Map(item));
        }
        catch (DomainRuleException exception)
        {
            return ResultFactory.Failure<ServiceItemDto>(exception.Code, exception.Message);
        }
    }

    public async Task<Result<ServiceItemDto>> UpdateServiceItemAsync(Guid tenantId, UpdateServiceItemCommand command,
        CancellationToken cancellationToken)
    {
        var item = await dbContext.ServiceItems.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == command.Id,
            cancellationToken);
        if (item is null) return ResultFactory.Failure<ServiceItemDto>("SERVICE_ITEM_NOT_FOUND", "服务项目不存在");
        if (item.Version != command.ExpectedVersion)
            return ResultFactory.Failure<ServiceItemDto>("VERSION_CONFLICT", "服务项目已被其他人修改，请刷新后重试");
        try
        {
            var previous = JsonSerializer.Serialize(Map(item));
            item.Update(command.Name, command.StandardDurationMinutes);
            if (command.Status == CatalogItemStatus.Enabled) item.Enable(); else item.Disable();
            AddAudit(tenantId, command.StoreId, command.OperatorId, "catalog.service_item.update", "ServiceItem",
                item.Id, previous, JsonSerializer.Serialize(Map(item)));
            await dbContext.SaveChangesAsync(cancellationToken);
            return ResultFactory.Success(Map(item));
        }
        catch (DomainRuleException exception)
        {
            return ResultFactory.Failure<ServiceItemDto>(exception.Code, exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ResultFactory.Failure<ServiceItemDto>("VERSION_CONFLICT", "服务项目已被其他人修改，请刷新后重试");
        }
    }

    public async Task<Result<bool>> DeleteServiceItemAsync(Guid tenantId, DeleteCatalogItemCommand command,
        CancellationToken cancellationToken)
    {
        var item = await dbContext.ServiceItems.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == command.Id,
            cancellationToken);
        if (item is null) return ResultFactory.Failure<bool>("SERVICE_ITEM_NOT_FOUND", "服务项目不存在");
        if (item.Version != command.ExpectedVersion)
            return ResultFactory.Failure<bool>("VERSION_CONFLICT", "服务项目已被其他人修改，请刷新后重试");
        var isReferenced = await dbContext.PriceBookLines.AnyAsync(x => x.TenantId == tenantId && x.ServiceItemId == item.Id,
                cancellationToken) ||
            await dbContext.ServiceOrderLines.AnyAsync(x => x.TenantId == tenantId && x.ServiceItemId == item.Id,
                cancellationToken) ||
            await dbContext.Visits.AnyAsync(x => x.TenantId == tenantId && x.PlannedServiceItemId == item.Id,
                cancellationToken);
        if (isReferenced)
            return ResultFactory.Failure<bool>("RESOURCE_IN_USE", "该服务项目已有价格、接待或订单记录，请停用而不是删除");
        try
        {
            dbContext.ServiceItems.Remove(item);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "catalog.service_item.delete", "ServiceItem",
                item.Id, JsonSerializer.Serialize(Map(item)), null);
            await dbContext.SaveChangesAsync(cancellationToken);
            return ResultFactory.Success(true);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ResultFactory.Failure<bool>("VERSION_CONFLICT", "服务项目已被其他人修改，请刷新后重试");
        }
        catch (DbUpdateException)
        {
            return ResultFactory.Failure<bool>("RESOURCE_IN_USE", "该服务项目已被业务引用，请停用而不是删除");
        }
    }

    public async Task<IReadOnlyList<ProductItemDto>> ListProductItemsAsync(Guid tenantId, string? query,
        CatalogItemStatus? status, CancellationToken cancellationToken)
    {
        var normalizedQuery = query?.Trim();
        var items = dbContext.ProductItems.AsNoTracking().Where(x => x.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(normalizedQuery))
            items = items.Where(x => x.Code.Contains(normalizedQuery) || x.Name.Contains(normalizedQuery));
        if (status.HasValue) items = items.Where(x => x.Status == status.Value);
        return await items.OrderBy(x => x.Code)
            .Select(x => new ProductItemDto(x.Id, x.Code, x.Name, x.UnitName, x.TrackInventory,
                x.ImageFileId, x.Status.ToString().ToUpperInvariant(), x.Version)).ToListAsync(cancellationToken);
    }

    public async Task<Result<ProductItemDto>> CreateProductItemAsync(Guid tenantId, CreateProductItemCommand command,
        CancellationToken cancellationToken)
    {
        var code = command.Code.Trim().ToUpperInvariant();
        if (await dbContext.Set<ProductItem>().AnyAsync(x => x.TenantId == tenantId && x.Code == code, cancellationToken))
            return ResultFactory.Failure<ProductItemDto>("DUPLICATE_CODE", "产品编码已经存在");
        try
        {
            var item = new ProductItem(tenantId, code, command.Name, command.UnitName, command.TrackInventory);
            dbContext.Set<ProductItem>().Add(item);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "catalog.product_item.create", "ProductItem", item.Id,
                null, item.Status.ToString());
            await dbContext.SaveChangesAsync(cancellationToken);
            return ResultFactory.Success(Map(item));
        }
        catch (DomainRuleException exception)
        {
            return ResultFactory.Failure<ProductItemDto>(exception.Code, exception.Message);
        }
    }

    public async Task<Result<ProductItemDto>> UpdateProductItemAsync(Guid tenantId, UpdateProductItemCommand command,
        CancellationToken cancellationToken)
    {
        var item = await dbContext.ProductItems.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == command.Id,
            cancellationToken);
        if (item is null) return ResultFactory.Failure<ProductItemDto>("PRODUCT_NOT_FOUND", "产品不存在");
        if (item.Version != command.ExpectedVersion)
            return ResultFactory.Failure<ProductItemDto>("VERSION_CONFLICT", "产品已被其他人修改，请刷新后重试");
        if (item.TrackInventory != command.TrackInventory && await ProductHasInventoryHistoryAsync(tenantId, item.Id,
                cancellationToken))
            return ResultFactory.Failure<ProductItemDto>("PRODUCT_INVENTORY_MODE_LOCKED",
                "该产品已有订单或库存记录，不能再修改库存跟踪属性");
        try
        {
            var previous = JsonSerializer.Serialize(Map(item));
            item.Update(command.Name, command.UnitName, command.TrackInventory);
            if (command.Status == CatalogItemStatus.Enabled) item.Enable(); else item.Disable();
            AddAudit(tenantId, command.StoreId, command.OperatorId, "catalog.product_item.update", "ProductItem",
                item.Id, previous, JsonSerializer.Serialize(Map(item)));
            await dbContext.SaveChangesAsync(cancellationToken);
            return ResultFactory.Success(Map(item));
        }
        catch (DomainRuleException exception)
        {
            return ResultFactory.Failure<ProductItemDto>(exception.Code, exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ResultFactory.Failure<ProductItemDto>("VERSION_CONFLICT", "产品已被其他人修改，请刷新后重试");
        }
    }

    public async Task<Result<bool>> DeleteProductItemAsync(Guid tenantId, DeleteCatalogItemCommand command,
        CancellationToken cancellationToken)
    {
        var item = await dbContext.ProductItems.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == command.Id,
            cancellationToken);
        if (item is null) return ResultFactory.Failure<bool>("PRODUCT_NOT_FOUND", "产品不存在");
        if (item.Version != command.ExpectedVersion)
            return ResultFactory.Failure<bool>("VERSION_CONFLICT", "产品已被其他人修改，请刷新后重试");
        var isReferenced = item.ImageFileId.HasValue ||
            await dbContext.ProductPriceBookLines.AnyAsync(x => x.TenantId == tenantId && x.ProductItemId == item.Id,
                cancellationToken) || await ProductHasInventoryHistoryAsync(tenantId, item.Id, cancellationToken);
        if (isReferenced)
            return ResultFactory.Failure<bool>("RESOURCE_IN_USE", "该产品已有图片、价格、订单或库存记录，请停用而不是删除");
        try
        {
            dbContext.ProductItems.Remove(item);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "catalog.product_item.delete", "ProductItem",
                item.Id, JsonSerializer.Serialize(Map(item)), null);
            await dbContext.SaveChangesAsync(cancellationToken);
            return ResultFactory.Success(true);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ResultFactory.Failure<bool>("VERSION_CONFLICT", "产品已被其他人修改，请刷新后重试");
        }
        catch (DbUpdateException)
        {
            return ResultFactory.Failure<bool>("RESOURCE_IN_USE", "该产品已被业务引用，请停用而不是删除");
        }
    }

    public async Task<Result<ProductItemDto>> SetProductImageAsync(Guid tenantId, Guid productItemId, Guid operatorId,
        Guid? storeId, FileUploadInput image, CancellationToken cancellationToken)
    {
        var product = await dbContext.ProductItems.SingleOrDefaultAsync(x => x.TenantId == tenantId &&
            x.Id == productItemId, cancellationToken);
        if (product is null)
            return ResultFactory.Failure<ProductItemDto>("PRODUCT_NOT_FOUND", "产品不存在");
        StoredFileRecord? stored = null;
        try
        {
            stored = await fileStorage.StoreImageAsync(tenantId, null, StoredFilePurposes.ProductImage, operatorId,
                image, cancellationToken);
            dbContext.StoredFiles.Add(stored);
            product.SetImage(stored.Id);
            AddAudit(tenantId, storeId, operatorId, "catalog.product_image.set", "ProductItem", product.Id,
                null, "ImageSet");
            await dbContext.SaveChangesAsync(cancellationToken);
            return ResultFactory.Success(Map(product));
        }
        catch (DomainRuleException exception)
        {
            if (stored is not null) await fileStorage.TryDeleteUncommittedAsync(stored);
            return ResultFactory.Failure<ProductItemDto>(exception.Code, exception.Message);
        }
        catch (SecureFileStorageException exception)
        {
            if (stored is not null) await fileStorage.TryDeleteUncommittedAsync(stored);
            return ResultFactory.Failure<ProductItemDto>(exception.Code, exception.Message);
        }
        catch
        {
            if (stored is not null) await fileStorage.TryDeleteUncommittedAsync(stored);
            throw;
        }
    }

    public async Task<Result<StoredFileContent>> ReadProductImageAsync(Guid tenantId, Guid productItemId,
        CancellationToken cancellationToken)
    {
        var file = await dbContext.ProductItems.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Id == productItemId && x.ImageFileId.HasValue)
            .Join(dbContext.StoredFiles.AsNoTracking(), product => product.ImageFileId!.Value, stored => stored.Id,
                (_, stored) => stored)
            .SingleOrDefaultAsync(cancellationToken);
        if (file is null || file.Purpose != StoredFilePurposes.ProductImage)
            return ResultFactory.Failure<StoredFileContent>("FILE_NOT_FOUND", "产品图片不存在");
        try { return ResultFactory.Success(await fileStorage.ReadAsync(file, cancellationToken)); }
        catch (SecureFileStorageException exception)
        { return ResultFactory.Failure<StoredFileContent>(exception.Code, exception.Message); }
    }

    public async Task<IReadOnlyList<PriceBookDto>> ListPriceBooksAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var books = await dbContext.PriceBooks.AsNoTracking().AsSplitQuery().Include(x => x.Lines).Include(x => x.ProductLines)
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.EffectiveFrom)
            .ToListAsync(cancellationToken);
        var names = await ServiceItemNamesAsync(tenantId, cancellationToken);
        var products = await ProductItemNamesAsync(tenantId, cancellationToken);
        return books.Select(book => Map(book, names, products)).ToList();
    }

    public async Task<Result<PriceBookDto>> CreatePriceBookAsync(Guid tenantId, CreatePriceBookCommand command, CancellationToken cancellationToken)
    {
        if (command.Lines.Count == 0 && command.ProductLines.Count == 0)
            return ResultFactory.Failure<PriceBookDto>("VALIDATION_FAILED", "请至少选择一个需要新增或调整价格的服务或产品");
        var serviceItemIds = command.Lines.Select(x => x.ServiceItemId).Distinct().ToArray();
        if (serviceItemIds.Length != command.Lines.Count)
        {
            return ResultFactory.Failure<PriceBookDto>("VALIDATION_FAILED", "同一个项目不能在价格版本中重复");
        }

        var names = await ServiceItemNamesAsync(tenantId, cancellationToken);
        if (serviceItemIds.Any(id => !names.ContainsKey(id)))
        {
            return ResultFactory.Failure<PriceBookDto>("VALIDATION_FAILED", "价格版本包含不存在或无权限的项目");
        }
        var productItemIds = command.ProductLines.Select(x => x.ProductItemId).Distinct().ToArray();
        if (productItemIds.Length != command.ProductLines.Count)
            return ResultFactory.Failure<PriceBookDto>("VALIDATION_FAILED", "同一个产品不能在价格版本中重复");
        var productNames = await ProductItemNamesAsync(tenantId, cancellationToken);
        if (productItemIds.Any(id => !productNames.ContainsKey(id)))
            return ResultFactory.Failure<PriceBookDto>("VALIDATION_FAILED", "价格版本包含不存在或无权限的产品");

        try
        {
            var previous = await dbContext.PriceBooks.AsNoTracking().AsSplitQuery()
                .Include(x => x.Lines).Include(x => x.ProductLines)
                .Where(x => x.TenantId == tenantId && x.Status == PriceBookStatus.Published &&
                    x.EffectiveFrom <= command.EffectiveFrom)
                .OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.PublishedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            var effectiveServicePrices = previous?.Lines.ToDictionary(x => x.ServiceItemId,
                x => x.UnitPriceMinor) ?? [];
            var effectiveProductPrices = previous?.ProductLines.ToDictionary(x => x.ProductItemId,
                x => x.UnitPriceMinor) ?? [];
            foreach (var line in command.Lines) effectiveServicePrices[line.ServiceItemId] = line.UnitPriceMinor;
            foreach (var line in command.ProductLines) effectiveProductPrices[line.ProductItemId] = line.UnitPriceMinor;

            var book = new PriceBook(tenantId, command.Name, command.EffectiveFrom);
            foreach (var line in effectiveServicePrices) book.SetPrice(line.Key, line.Value);
            foreach (var line in effectiveProductPrices) book.SetProductPrice(line.Key, line.Value);

            dbContext.PriceBooks.Add(book);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "catalog.price_book.create", "PriceBook", book.Id,
                null, book.Status.ToString());
            await dbContext.SaveChangesAsync(cancellationToken);
            return ResultFactory.Success(Map(book, names, productNames));
        }
        catch (DomainRuleException exception)
        {
            return ResultFactory.Failure<PriceBookDto>(exception.Code, exception.Message);
        }
    }

    public async Task<Result<PriceBookDto>> PublishPriceBookAsync(Guid tenantId, Guid priceBookId, Guid operatorId,
        Guid? storeId, CancellationToken cancellationToken)
    {
        var book = await dbContext.PriceBooks.AsSplitQuery().Include(x => x.Lines).Include(x => x.ProductLines)
            .SingleOrDefaultAsync(x => x.Id == priceBookId && x.TenantId == tenantId, cancellationToken);
        if (book is null)
        {
            return ResultFactory.Failure<PriceBookDto>("NOT_FOUND", "价格版本不存在");
        }

        try
        {
            var previous = book.Status.ToString();
            book.Publish(DateTimeOffset.UtcNow);
            AddAudit(tenantId, storeId, operatorId, "catalog.price_book.publish", "PriceBook", book.Id, previous,
                book.Status.ToString());
            await dbContext.SaveChangesAsync(cancellationToken);
            return ResultFactory.Success(Map(book, await ServiceItemNamesAsync(tenantId, cancellationToken),
                await ProductItemNamesAsync(tenantId, cancellationToken)));
        }
        catch (DomainRuleException exception)
        {
            return ResultFactory.Failure<PriceBookDto>(exception.Code, exception.Message);
        }
    }

    private async Task<Dictionary<Guid, string>> ServiceItemNamesAsync(Guid tenantId, CancellationToken cancellationToken)
        => await dbContext.ServiceItems.AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

    private async Task<Dictionary<Guid, (string Name, string UnitName)>> ProductItemNamesAsync(Guid tenantId,
        CancellationToken cancellationToken)
    {
        var items = await dbContext.Set<ProductItem>().AsNoTracking().Where(x => x.TenantId == tenantId)
            .Select(x => new { x.Id, x.Name, x.UnitName }).ToListAsync(cancellationToken);
        return items.ToDictionary(x => x.Id, x => (x.Name, x.UnitName));
    }

    private async Task<bool> ProductHasInventoryHistoryAsync(Guid tenantId, Guid productItemId,
        CancellationToken cancellationToken)
        => await dbContext.ServiceOrderLines.AnyAsync(x => x.TenantId == tenantId && x.ProductItemId == productItemId,
               cancellationToken) ||
           await dbContext.InventoryBalances.AnyAsync(x => x.TenantId == tenantId && x.ProductItemId == productItemId,
               cancellationToken) ||
           await dbContext.InventoryReservations.AnyAsync(x => x.TenantId == tenantId && x.ProductItemId == productItemId,
               cancellationToken) ||
           await dbContext.InventoryMovements.AnyAsync(x => x.TenantId == tenantId && x.ProductItemId == productItemId,
               cancellationToken) ||
           await dbContext.InventoryDocumentLines.AnyAsync(x => x.TenantId == tenantId && x.ProductItemId == productItemId,
               cancellationToken) ||
           await dbContext.ProductReturns.AnyAsync(x => x.TenantId == tenantId && x.ProductItemId == productItemId,
               cancellationToken);

    private static ServiceItemDto Map(ServiceItem item)
        => new(item.Id, item.Code, item.Name, item.StandardDurationMinutes, item.Status.ToString().ToUpperInvariant(), item.Version);

    private static ProductItemDto Map(ProductItem item)
        => new(item.Id, item.Code, item.Name, item.UnitName, item.TrackInventory,
            item.ImageFileId, item.Status.ToString().ToUpperInvariant(), item.Version);

    private static PriceBookDto Map(PriceBook book, IReadOnlyDictionary<Guid, string> names,
        IReadOnlyDictionary<Guid, (string Name, string UnitName)> products)
        => new(book.Id, book.Name, book.Status.ToString().ToUpperInvariant(), book.EffectiveFrom, book.PublishedAtUtc,
            book.Lines.Select(line => new PriceBookLineDto(line.ServiceItemId, names.GetValueOrDefault(line.ServiceItemId, "未知项目"), line.UnitPriceMinor)).ToList(),
            book.ProductLines.Select(line =>
            {
                var product = products.GetValueOrDefault(line.ProductItemId, ("未知产品", "—"));
                return new ProductPriceBookLineDto(line.ProductItemId, product.Item1, product.Item2, line.UnitPriceMinor);
            }).ToList());

    private void AddAudit(Guid tenantId, Guid? storeId, Guid operatorId, string action, string entityType, Guid entityId,
        string? previousState, string? currentState) => dbContext.AuditEvents.Add(new AuditEventRecord
    {
        TenantId = tenantId, StoreId = storeId, OperatorId = operatorId, Action = action, EntityType = entityType,
        EntityId = entityId, PreviousState = previousState, CurrentState = currentState,
        TraceId = httpContextAccessor.HttpContext?.TraceIdentifier ?? "background", OccurredAtUtc = DateTimeOffset.UtcNow,
    });
}
