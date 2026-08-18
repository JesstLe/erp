using Erp.Application.Catalog;
using Erp.Application.Common;
using Erp.Domain.Catalog;
using Erp.Domain.Common;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Erp.Infrastructure.Catalog;

public sealed class CatalogService(ErpDbContext dbContext, IHttpContextAccessor httpContextAccessor) : ICatalogService
{
    public async Task<IReadOnlyList<ServiceItemDto>> ListServiceItemsAsync(Guid tenantId, CancellationToken cancellationToken)
        => await dbContext.ServiceItems
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.Code)
            .Select(x => new ServiceItemDto(x.Id, x.Code, x.Name, x.StandardDurationMinutes, x.Status.ToString().ToUpperInvariant(), x.Version))
            .ToListAsync(cancellationToken);

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

    public async Task<IReadOnlyList<ProductItemDto>> ListProductItemsAsync(Guid tenantId, CancellationToken cancellationToken)
        => await dbContext.Set<ProductItem>().AsNoTracking().Where(x => x.TenantId == tenantId).OrderBy(x => x.Code)
            .Select(x => new ProductItemDto(x.Id, x.Code, x.Name, x.UnitName, x.TrackInventory,
                x.Status.ToString().ToUpperInvariant(), x.Version)).ToListAsync(cancellationToken);

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

    private static ServiceItemDto Map(ServiceItem item)
        => new(item.Id, item.Code, item.Name, item.StandardDurationMinutes, item.Status.ToString().ToUpperInvariant(), item.Version);

    private static ProductItemDto Map(ProductItem item)
        => new(item.Id, item.Code, item.Name, item.UnitName, item.TrackInventory,
            item.Status.ToString().ToUpperInvariant(), item.Version);

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
