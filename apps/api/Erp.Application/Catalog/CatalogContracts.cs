using Erp.Application.Common;

namespace Erp.Application.Catalog;

public sealed record ServiceItemDto(Guid Id, string Code, string Name, int StandardDurationMinutes, string Status, uint Version);

public sealed record CreateServiceItemCommand(string Code, string Name, int StandardDurationMinutes, Guid OperatorId,
    Guid? StoreId);

public sealed record ProductItemDto(Guid Id, string Code, string Name, string UnitName, bool TrackInventory,
    Guid? ImageFileId, string Status, uint Version);

public sealed record CreateProductItemCommand(string Code, string Name, string UnitName, bool TrackInventory,
    Guid OperatorId, Guid? StoreId);

public sealed record PriceBookDto(
    Guid Id,
    string Name,
    string Status,
    DateOnly EffectiveFrom,
    DateTimeOffset? PublishedAtUtc,
    IReadOnlyList<PriceBookLineDto> Lines,
    IReadOnlyList<ProductPriceBookLineDto> ProductLines);

public sealed record PriceBookLineDto(Guid ServiceItemId, string ServiceItemName, long UnitPriceMinor);
public sealed record ProductPriceBookLineDto(Guid ProductItemId, string ProductItemName, string UnitName, long UnitPriceMinor);

public sealed record CreatePriceBookCommand(string Name, DateOnly EffectiveFrom, IReadOnlyList<CreatePriceBookLineCommand> Lines,
    IReadOnlyList<CreateProductPriceBookLineCommand> ProductLines, Guid OperatorId, Guid? StoreId);

public sealed record CreatePriceBookLineCommand(Guid ServiceItemId, long UnitPriceMinor);
public sealed record CreateProductPriceBookLineCommand(Guid ProductItemId, long UnitPriceMinor);

public interface ICatalogService
{
    Task<IReadOnlyList<ServiceItemDto>> ListServiceItemsAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<Result<ServiceItemDto>> CreateServiceItemAsync(Guid tenantId, CreateServiceItemCommand command, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProductItemDto>> ListProductItemsAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<Result<ProductItemDto>> CreateProductItemAsync(Guid tenantId, CreateProductItemCommand command,
        CancellationToken cancellationToken);
    Task<Result<ProductItemDto>> SetProductImageAsync(Guid tenantId, Guid productItemId, Guid operatorId,
        Guid? storeId, FileUploadInput image, CancellationToken cancellationToken);
    Task<Result<StoredFileContent>> ReadProductImageAsync(Guid tenantId, Guid productItemId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PriceBookDto>> ListPriceBooksAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<Result<PriceBookDto>> CreatePriceBookAsync(Guid tenantId, CreatePriceBookCommand command, CancellationToken cancellationToken);

    Task<Result<PriceBookDto>> PublishPriceBookAsync(Guid tenantId, Guid priceBookId, Guid operatorId, Guid? storeId,
        CancellationToken cancellationToken);
}
