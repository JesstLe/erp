using Erp.Application.Common;

namespace Erp.Application.Inventory;

public sealed record SupplierDto(Guid Id, string Code, string Name, string? ContactName, string? Mobile,
    string? SettlementTerms, string Status, uint Version);
public sealed record SaveSupplierCommand(Guid? Id, string Name, string? ContactName,
    string? Mobile, string? SettlementTerms, uint? ExpectedVersion, Guid OperatorId);
public sealed record ChangeSupplierStatusCommand(Guid SupplierId, bool Enable, uint ExpectedVersion,
    Guid OperatorId);

public sealed record InventoryLotDto(Guid Id, Guid StoreId, Guid ProductItemId, string ProductCode,
    string ProductName, string UnitName, string BatchNo, DateOnly? ExpiresOn, long UnitCostMinor,
    int OriginalQuantity, int RemainingQuantity, string SourceType, DateTimeOffset CreatedAtUtc);

public sealed record PurchaseReceiptLineDto(Guid Id, Guid ProductItemId, string ProductCode,
    string ProductName, string UnitName, int Quantity, long UnitCostMinor, long LineCostMinor,
    string BatchNo, DateOnly? ExpiresOn);
public sealed record PurchaseReceiptDto(Guid Id, Guid StoreId, Guid SupplierId, string SupplierName,
    string ReceiptNo, string? ExternalNo, string Note, long TotalCostMinor, Guid PostedBy,
    DateTimeOffset PostedAtUtc, IReadOnlyList<PurchaseReceiptLineDto> Lines);
public sealed record PostPurchaseReceiptLineCommand(Guid ProductItemId, int Quantity, long UnitCostMinor,
    string BatchNo, DateOnly? ExpiresOn);
public sealed record PostPurchaseReceiptCommand(Guid StoreId, Guid SupplierId, string? ExternalNo,
    string Note, IReadOnlyList<PostPurchaseReceiptLineCommand> Lines, Guid CommandId, Guid OperatorId);

public sealed record StocktakeLineDto(Guid Id, Guid ProductItemId, string ProductCode, string ProductName,
    string UnitName, int BookQuantity, int CountedQuantity, int DifferenceQuantity);
public sealed record StocktakeDto(Guid Id, Guid StoreId, string StocktakeNo, string Reason,
    Guid RequestedBy, DateTimeOffset FrozenAtUtc, string Status, Guid? ApprovedBy,
    DateTimeOffset? PostedAtUtc, string? DecisionReason, uint Version,
    IReadOnlyList<StocktakeLineDto> Lines);
public sealed record CreateStocktakeLineCommand(Guid ProductItemId, int CountedQuantity);
public sealed record CreateStocktakeCommand(Guid StoreId, string Reason,
    IReadOnlyList<CreateStocktakeLineCommand> Lines, Guid CommandId, Guid OperatorId);
public sealed record DecideStocktakeCommand(Guid StocktakeId, Guid StoreId, string Reason,
    uint ExpectedVersion, Guid CommandId, Guid OperatorId);

public sealed record InventoryTransferLineDto(Guid Id, Guid ProductItemId, string ProductCode,
    string ProductName, string UnitName, int Quantity);
public sealed record InventoryTransferDto(Guid Id, Guid SourceStoreId, Guid DestinationStoreId,
    string TransferNo, string Reason, Guid RequestedBy, DateTimeOffset RequestedAtUtc, string Status,
    Guid? ShippedBy, DateTimeOffset? ShippedAtUtc, Guid? ReceivedBy, DateTimeOffset? ReceivedAtUtc,
    string? DecisionReason, uint Version, IReadOnlyList<InventoryTransferLineDto> Lines);
public sealed record CreateInventoryTransferLineCommand(Guid ProductItemId, int Quantity);
public sealed record CreateInventoryTransferCommand(Guid SourceStoreId, Guid DestinationStoreId,
    string Reason, IReadOnlyList<CreateInventoryTransferLineCommand> Lines, Guid CommandId,
    Guid OperatorId);
public sealed record TransitionInventoryTransferCommand(Guid TransferId, string Reason,
    uint ExpectedVersion, Guid CommandId, Guid OperatorId);

public interface ISupplyChainService
{
    Task<PageResult<SupplierDto>> ListSuppliersAsync(Guid tenantId, string? keyword, bool includeDisabled,
        int page, int pageSize, CancellationToken cancellationToken);
    Task<Result<SupplierDto>> SaveSupplierAsync(Guid tenantId, SaveSupplierCommand command,
        CancellationToken cancellationToken);
    Task<Result<SupplierDto>> ChangeSupplierStatusAsync(Guid tenantId,
        ChangeSupplierStatusCommand command, CancellationToken cancellationToken);
    Task<PageResult<InventoryLotDto>> ListLotsAsync(Guid tenantId, Guid storeId, Guid? productItemId,
        bool expiringOnly, int page, int pageSize, CancellationToken cancellationToken);
    Task<PageResult<PurchaseReceiptDto>> ListPurchaseReceiptsAsync(Guid tenantId, Guid storeId,
        int page, int pageSize, CancellationToken cancellationToken);
    Task<Result<PurchaseReceiptDto>> PostPurchaseReceiptAsync(Guid tenantId,
        PostPurchaseReceiptCommand command, CancellationToken cancellationToken);
    Task<PageResult<StocktakeDto>> ListStocktakesAsync(Guid tenantId, Guid storeId, string? status,
        int page, int pageSize, CancellationToken cancellationToken);
    Task<Result<StocktakeDto>> CreateStocktakeAsync(Guid tenantId, CreateStocktakeCommand command,
        CancellationToken cancellationToken);
    Task<Result<StocktakeDto>> ApproveStocktakeAsync(Guid tenantId, DecideStocktakeCommand command,
        CancellationToken cancellationToken);
    Task<Result<StocktakeDto>> CancelStocktakeAsync(Guid tenantId, DecideStocktakeCommand command,
        CancellationToken cancellationToken);
    Task<PageResult<InventoryTransferDto>> ListTransfersAsync(Guid tenantId, Guid? storeId,
        string? status, int page, int pageSize, CancellationToken cancellationToken);
    Task<Result<InventoryTransferDto>> CreateTransferAsync(Guid tenantId,
        CreateInventoryTransferCommand command, CancellationToken cancellationToken);
    Task<Result<InventoryTransferDto>> ShipTransferAsync(Guid tenantId,
        TransitionInventoryTransferCommand command, CancellationToken cancellationToken);
    Task<Result<InventoryTransferDto>> ReceiveTransferAsync(Guid tenantId,
        TransitionInventoryTransferCommand command, CancellationToken cancellationToken);
    Task<Result<InventoryTransferDto>> CancelTransferAsync(Guid tenantId,
        TransitionInventoryTransferCommand command, CancellationToken cancellationToken);
}
