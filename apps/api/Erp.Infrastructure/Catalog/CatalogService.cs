using Erp.Application.Catalog;
using Erp.Application.Common;
using Erp.Domain.Catalog;
using Erp.Domain.Common;
using Erp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Erp.Infrastructure.Catalog;

public sealed class CatalogService(ErpDbContext dbContext) : ICatalogService
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
            await dbContext.SaveChangesAsync(cancellationToken);
            return ResultFactory.Success(Map(item));
        }
        catch (DomainRuleException exception)
        {
            return ResultFactory.Failure<ServiceItemDto>(exception.Code, exception.Message);
        }
    }

    public async Task<IReadOnlyList<PriceBookDto>> ListPriceBooksAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var books = await dbContext.PriceBooks.AsNoTracking().Include(x => x.Lines)
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.EffectiveFrom)
            .ToListAsync(cancellationToken);
        var names = await ServiceItemNamesAsync(tenantId, cancellationToken);
        return books.Select(book => Map(book, names)).ToList();
    }

    public async Task<Result<PriceBookDto>> CreatePriceBookAsync(Guid tenantId, CreatePriceBookCommand command, CancellationToken cancellationToken)
    {
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

        try
        {
            var book = new PriceBook(tenantId, command.Name, command.EffectiveFrom);
            foreach (var line in command.Lines)
            {
                book.SetPrice(line.ServiceItemId, line.UnitPriceMinor);
            }

            dbContext.PriceBooks.Add(book);
            await dbContext.SaveChangesAsync(cancellationToken);
            return ResultFactory.Success(Map(book, names));
        }
        catch (DomainRuleException exception)
        {
            return ResultFactory.Failure<PriceBookDto>(exception.Code, exception.Message);
        }
    }

    public async Task<Result<PriceBookDto>> PublishPriceBookAsync(Guid tenantId, Guid priceBookId, CancellationToken cancellationToken)
    {
        var book = await dbContext.PriceBooks.Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == priceBookId && x.TenantId == tenantId, cancellationToken);
        if (book is null)
        {
            return ResultFactory.Failure<PriceBookDto>("NOT_FOUND", "价格版本不存在");
        }

        try
        {
            book.Publish(DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
            return ResultFactory.Success(Map(book, await ServiceItemNamesAsync(tenantId, cancellationToken)));
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

    private static ServiceItemDto Map(ServiceItem item)
        => new(item.Id, item.Code, item.Name, item.StandardDurationMinutes, item.Status.ToString().ToUpperInvariant(), item.Version);

    private static PriceBookDto Map(PriceBook book, IReadOnlyDictionary<Guid, string> names)
        => new(book.Id, book.Name, book.Status.ToString().ToUpperInvariant(), book.EffectiveFrom, book.PublishedAtUtc,
            book.Lines.Select(line => new PriceBookLineDto(line.ServiceItemId, names.GetValueOrDefault(line.ServiceItemId, "未知项目"), line.UnitPriceMinor)).ToList());
}
