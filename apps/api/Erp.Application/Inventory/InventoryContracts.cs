using Erp.Application.Common;

namespace Erp.Application.Inventory;

public sealed record InventoryBalanceDto(Guid ProductItemId, string ProductCode, string ProductName,
    string UnitName, bool TrackInventory, int OnHandQuantity, int ReservedQuantity, int AvailableQuantity,
    uint Version);

public sealed record InventoryMovementDto(Guid Id, Guid ProductItemId, string ProductCode, string ProductName,
    string UnitName, string MovementType, string Direction, int Quantity, int OnHandBefore, int OnHandAfter,
    string SourceType, Guid SourceId, Guid SourceLineId, Guid CommandId, Guid? OperatorId,
    DateTimeOffset OccurredAtUtc);

public sealed record InventoryDocumentLineDto(Guid Id, Guid ProductItemId, string ProductCode,
    string ProductName, string UnitName, int Quantity);

public sealed record InventoryDocumentDto(Guid Id, string DocumentNo, string DocumentType, string Reason,
    Guid PostedBy, DateTimeOffset PostedAtUtc, IReadOnlyList<InventoryDocumentLineDto> Lines);

public sealed record ProductReturnDto(Guid Id, Guid OrderId, Guid OrderLineId, Guid ProductItemId,
    string ProductCode, string ProductName, string UnitName, int Quantity, string Reason, Guid ReturnedBy,
    DateTimeOffset ReturnedAtUtc);

public sealed record PostInventoryDocumentLineCommand(Guid ProductItemId, int Quantity);
public sealed record PostInventoryDocumentCommand(Guid StoreId, string DocumentType, string Reason,
    IReadOnlyList<PostInventoryDocumentLineCommand> Lines, Guid CommandId, Guid OperatorId);
public sealed record ReturnProductCommand(Guid StoreId, Guid OrderId, Guid OrderLineId, int Quantity,
    string Reason, uint ExpectedOrderVersion, Guid CommandId, Guid OperatorId);

public interface IInventoryService
{
    Task<IReadOnlyList<InventoryBalanceDto>> ListBalancesAsync(Guid tenantId, Guid storeId,
        CancellationToken cancellationToken);
    Task<PageResult<InventoryMovementDto>> ListMovementsAsync(Guid tenantId, Guid storeId,
        Guid? productItemId, int page, int pageSize, CancellationToken cancellationToken);
    Task<PageResult<InventoryDocumentDto>> ListDocumentsAsync(Guid tenantId, Guid storeId,
        int page, int pageSize, CancellationToken cancellationToken);
    Task<Result<InventoryDocumentDto>> PostDocumentAsync(Guid tenantId, PostInventoryDocumentCommand command,
        CancellationToken cancellationToken);
    Task<Result<ProductReturnDto>> ReturnProductAsync(Guid tenantId, ReturnProductCommand command,
        CancellationToken cancellationToken);
}
