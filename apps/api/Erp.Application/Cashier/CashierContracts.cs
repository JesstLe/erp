using Erp.Application.Common;

namespace Erp.Application.Cashier;

public sealed record CashierVisitDto(Guid Id, string VisitNo, string Status, Guid? CustomerId,
    DateTimeOffset ArrivedAtUtc, DateTimeOffset? ServiceEndedAtUtc, long FacilitySeconds, string? Note);
public sealed record ServiceOrderLineDto(Guid Id, Guid ServiceItemId, string ItemCode, string ItemName,
    int Quantity, int? ActualSeconds, long ReferencePriceMinor, long EnteredPriceMinor,
    long LineAmountMinor, string? PriceOverrideReason);
public sealed record ServiceOrderDto(Guid Id, string OrderNo, Guid VisitId, Guid? CustomerId, string Status,
    Guid PriceBookId, long ReferenceAmountMinor, long ReceivableMinor, long RefundedMinor, string? Note, uint Version,
    DateTimeOffset CreatedAtUtc, IReadOnlyList<ServiceOrderLineDto> Lines);
public sealed record CreateServiceOrderLineCommand(Guid ServiceItemId, int Quantity, int? ActualSeconds,
    long EnteredPriceMinor, string? PriceOverrideReason);
public sealed record CreateServiceOrderCommand(Guid StoreId, Guid? VisitId, Guid? CustomerId, string? Note,
    IReadOnlyList<CreateServiceOrderLineCommand> Lines, Guid CommandId, Guid OperatorId);
public sealed record ConfirmServiceOrderCommand(Guid StoreId, Guid OrderId, uint ExpectedVersion,
    Guid CommandId, Guid OperatorId);

public interface ICashierService
{
    Task<IReadOnlyList<CashierVisitDto>> ListPendingVisitsAsync(Guid tenantId, Guid storeId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ServiceOrderDto>> ListOrdersAsync(Guid tenantId, Guid storeId, CancellationToken cancellationToken);
    Task<Result<ServiceOrderDto>> GetOrderAsync(Guid tenantId, Guid storeId, Guid orderId, CancellationToken cancellationToken);
    Task<Result<ServiceOrderDto>> CreateOrderAsync(Guid tenantId, CreateServiceOrderCommand command, CancellationToken cancellationToken);
    Task<Result<ServiceOrderDto>> ConfirmOrderAsync(Guid tenantId, ConfirmServiceOrderCommand command, CancellationToken cancellationToken);
}
