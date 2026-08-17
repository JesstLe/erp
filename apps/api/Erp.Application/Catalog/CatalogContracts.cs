using Erp.Application.Common;

namespace Erp.Application.Catalog;

public sealed record ServiceItemDto(Guid Id, string Code, string Name, int StandardDurationMinutes, string Status, uint Version);

public sealed record CreateServiceItemCommand(string Code, string Name, int StandardDurationMinutes);

public sealed record PriceBookDto(
    Guid Id,
    string Name,
    string Status,
    DateOnly EffectiveFrom,
    DateTimeOffset? PublishedAtUtc,
    IReadOnlyList<PriceBookLineDto> Lines);

public sealed record PriceBookLineDto(Guid ServiceItemId, string ServiceItemName, long UnitPriceMinor);

public sealed record CreatePriceBookCommand(string Name, DateOnly EffectiveFrom, IReadOnlyList<CreatePriceBookLineCommand> Lines);

public sealed record CreatePriceBookLineCommand(Guid ServiceItemId, long UnitPriceMinor);

public interface ICatalogService
{
    Task<IReadOnlyList<ServiceItemDto>> ListServiceItemsAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<Result<ServiceItemDto>> CreateServiceItemAsync(Guid tenantId, CreateServiceItemCommand command, CancellationToken cancellationToken);

    Task<IReadOnlyList<PriceBookDto>> ListPriceBooksAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<Result<PriceBookDto>> CreatePriceBookAsync(Guid tenantId, CreatePriceBookCommand command, CancellationToken cancellationToken);

    Task<Result<PriceBookDto>> PublishPriceBookAsync(Guid tenantId, Guid priceBookId, CancellationToken cancellationToken);
}

