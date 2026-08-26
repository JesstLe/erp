using Erp.Application.Common;
using Erp.Domain.Catalog;

namespace Erp.Application.Catalog;

public sealed record ServiceItemDto(Guid Id, string Code, string Name, int StandardDurationMinutes, string Status,
    uint Version, string? CommissionMode, int? CommissionRateBasisPoints, long? CommissionFixedMinor);

public sealed record CreateServiceItemCommand(string Name, int StandardDurationMinutes,
    CommissionMode CommissionMode, int? CommissionRateBasisPoints, long? CommissionFixedMinor, Guid OperatorId,
    Guid? StoreId);
public sealed record UpdateServiceItemCommand(Guid Id, string Name, int StandardDurationMinutes,
    CatalogItemStatus Status, CommissionMode CommissionMode, int? CommissionRateBasisPoints,
    long? CommissionFixedMinor, uint ExpectedVersion, Guid OperatorId, Guid? StoreId);
public sealed record DeleteCatalogItemCommand(Guid Id, uint ExpectedVersion, Guid OperatorId, Guid? StoreId);

public sealed record ProductItemDto(Guid Id, string Code, string Name, string UnitName, bool TrackInventory,
    Guid? ImageFileId, string Status, uint Version);

public sealed record CreateProductItemCommand(string Name, string UnitName, bool TrackInventory,
    Guid OperatorId, Guid? StoreId);
public sealed record UpdateProductItemCommand(Guid Id, string Name, string UnitName, bool TrackInventory,
    CatalogItemStatus Status, uint ExpectedVersion, Guid OperatorId, Guid? StoreId);

public sealed record PriceBookDto(
    Guid Id,
    string Name,
    string Status,
    DateOnly EffectiveFrom,
    DateTimeOffset? PublishedAtUtc,
    IReadOnlyList<PriceBookLineDto> Lines,
    IReadOnlyList<ProductPriceBookLineDto> ProductLines,
    uint Version);

public sealed record PriceBookLineDto(Guid ServiceItemId, string ServiceItemName, long UnitPriceMinor);
public sealed record ProductPriceBookLineDto(Guid ProductItemId, string ProductItemName, string UnitName, long UnitPriceMinor);

public sealed record CreatePriceBookCommand(string Name, DateOnly EffectiveFrom, IReadOnlyList<CreatePriceBookLineCommand> Lines,
    IReadOnlyList<CreateProductPriceBookLineCommand> ProductLines, Guid OperatorId, Guid? StoreId);

public sealed record CreatePriceBookLineCommand(Guid ServiceItemId, long UnitPriceMinor);
public sealed record CreateProductPriceBookLineCommand(Guid ProductItemId, long UnitPriceMinor);
public sealed record UpdatePriceBookCommand(Guid Id, string Name, DateOnly EffectiveFrom,
    IReadOnlyList<CreatePriceBookLineCommand> Lines, IReadOnlyList<CreateProductPriceBookLineCommand> ProductLines,
    uint ExpectedVersion, Guid OperatorId, Guid? StoreId);
public sealed record CancelPriceBookCommand(Guid Id, uint ExpectedVersion, Guid OperatorId, Guid? StoreId);
public sealed record DeletePriceBookCommand(Guid Id, uint ExpectedVersion, string Reason, Guid OperatorId,
    Guid? StoreId);
public sealed record CopyPriceBookCommand(Guid Id, string Name, DateOnly EffectiveFrom, Guid OperatorId,
    Guid? StoreId);
public sealed record RetirePriceBookCommand(Guid Id, uint ExpectedVersion, string Reason, Guid OperatorId,
    Guid? StoreId);

public interface ICatalogService
{
    Task<IReadOnlyList<ServiceItemDto>> ListServiceItemsAsync(Guid tenantId, string? query, CatalogItemStatus? status,
        bool includeCommission, CancellationToken cancellationToken);

    Task<Result<ServiceItemDto>> CreateServiceItemAsync(Guid tenantId, CreateServiceItemCommand command, CancellationToken cancellationToken);
    Task<Result<ServiceItemDto>> UpdateServiceItemAsync(Guid tenantId, UpdateServiceItemCommand command,
        CancellationToken cancellationToken);
    Task<Result<bool>> DeleteServiceItemAsync(Guid tenantId, DeleteCatalogItemCommand command,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ProductItemDto>> ListProductItemsAsync(Guid tenantId, string? query, CatalogItemStatus? status,
        CancellationToken cancellationToken);

    Task<Result<ProductItemDto>> CreateProductItemAsync(Guid tenantId, CreateProductItemCommand command,
        CancellationToken cancellationToken);
    Task<Result<ProductItemDto>> UpdateProductItemAsync(Guid tenantId, UpdateProductItemCommand command,
        CancellationToken cancellationToken);
    Task<Result<bool>> DeleteProductItemAsync(Guid tenantId, DeleteCatalogItemCommand command,
        CancellationToken cancellationToken);
    Task<Result<ProductItemDto>> SetProductImageAsync(Guid tenantId, Guid productItemId, Guid operatorId,
        Guid? storeId, FileUploadInput image, CancellationToken cancellationToken);
    Task<Result<StoredFileContent>> ReadProductImageAsync(Guid tenantId, Guid productItemId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PriceBookDto>> ListPriceBooksAsync(Guid tenantId, string? query, PriceBookStatus? status,
        DateOnly? effectiveFrom, DateOnly? effectiveTo, CancellationToken cancellationToken);
    Task<Result<PriceBookDto>> GetPriceBookAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);

    Task<Result<PriceBookDto>> CreatePriceBookAsync(Guid tenantId, CreatePriceBookCommand command, CancellationToken cancellationToken);
    Task<Result<PriceBookDto>> UpdatePriceBookAsync(Guid tenantId, UpdatePriceBookCommand command,
        CancellationToken cancellationToken);
    Task<Result<PriceBookDto>> CancelPriceBookAsync(Guid tenantId, CancelPriceBookCommand command,
        CancellationToken cancellationToken);
    Task<Result<bool>> DeletePriceBookAsync(Guid tenantId, DeletePriceBookCommand command,
        CancellationToken cancellationToken);
    Task<Result<PriceBookDto>> CopyPriceBookAsync(Guid tenantId, CopyPriceBookCommand command,
        CancellationToken cancellationToken);
    Task<Result<PriceBookDto>> RetirePriceBookAsync(Guid tenantId, RetirePriceBookCommand command,
        CancellationToken cancellationToken);

    Task<Result<PriceBookDto>> PublishPriceBookAsync(Guid tenantId, Guid priceBookId, Guid operatorId, Guid? storeId,
        CancellationToken cancellationToken);
}
