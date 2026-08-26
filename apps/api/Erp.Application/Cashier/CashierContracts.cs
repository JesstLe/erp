using Erp.Application.Common;

namespace Erp.Application.Cashier;

public sealed record CashierVisitDto(Guid Id, string VisitNo, string Status, Guid? CustomerId,
    string CustomerDisplayName, Guid? PlannedServiceItemId, string? PlannedServiceItemName,
    string FacilityNames, DateTimeOffset ArrivedAtUtc, DateTimeOffset? ServiceEndedAtUtc,
    long FacilitySeconds, string? Note);
public sealed record ServiceEmployeeDto(Guid Id, string EmployeeNo, string DisplayName, string PositionCode);
public sealed record ServiceOrderLineDto(Guid Id, string LineType, Guid? ServiceItemId, Guid? ProductItemId,
    string ItemCode, string ItemName, string? UnitName, int Quantity, int ReturnedQuantity,
    int? ActualSeconds, long ReferencePriceMinor, long EnteredPriceMinor,
    long LineAmountMinor, string? PriceOverrideReason, Guid? ServiceEmployeeId, string? EmployeeNo,
    string? EmployeeName);
public sealed record ServiceOrderDto(Guid Id, string OrderNo, Guid VisitId, Guid? CustomerId, string Status,
    Guid? PriceBookId, long ReferenceAmountMinor, long ReceivableMinor, long RefundedMinor, string? Note, uint Version,
    DateTimeOffset CreatedAtUtc, string PriceAuthorizationStatus, Guid? PricePolicyId, int? PricePolicyVersion,
    Guid? PriceAuthorizedBy, DateTimeOffset? PriceAuthorizedAtUtc, IReadOnlyList<ServiceOrderLineDto> Lines);
public sealed record PriceOverridePolicyDto(Guid Id, int PolicyVersion, int ManagerLineDiscountBasisPoints,
    long ManagerOrderDiscountMinor, bool AllowManagerPriceIncrease, DateTimeOffset EffectiveFromUtc, uint Version);
public sealed record PriceOverrideApprovalDto(Guid Id, Guid ServiceOrderId, string OrderNo, string Status,
    Guid RequesterId, string RequesterName, string RequesterRole, Guid PolicyId, int PolicyVersion,
    long ReferenceAmountMinor, long ReceivableMinor, long DifferenceMinor, int MaximumLineDiscountBasisPoints,
    int ManagerLineDiscountBasisPoints, long ManagerOrderDiscountMinor, bool AllowManagerPriceIncrease,
    DateTimeOffset RequestedAtUtc, Guid? DecidedBy, string? DeciderName, DateTimeOffset? DecidedAtUtc,
    string? DecisionNote, uint Version);
public sealed record CreateServiceOrderLineCommand(string? LineType, Guid? ServiceItemId, Guid? ProductItemId,
    Guid? ServiceEmployeeId, int Quantity, int? ActualSeconds, long EnteredPriceMinor,
    string? PriceOverrideReason);
public sealed record CreateServiceOrderCommand(Guid StoreId, Guid? VisitId, Guid? CustomerId, string? Note,
    IReadOnlyList<CreateServiceOrderLineCommand> Lines, Guid CommandId, Guid OperatorId,
    IReadOnlyList<string> OperatorRoles);
public sealed record ConfirmServiceOrderCommand(Guid StoreId, Guid OrderId, uint ExpectedVersion,
    Guid CommandId, Guid OperatorId);
public sealed record VoidServiceOrderCommand(Guid StoreId, Guid OrderId, uint ExpectedVersion,
    string Reason, Guid CommandId, Guid OperatorId);
public sealed record UpdatePriceOverridePolicyCommand(Guid StoreId, int ManagerLineDiscountBasisPoints,
    long ManagerOrderDiscountMinor, bool AllowManagerPriceIncrease, uint ExpectedVersion,
    Guid CommandId, Guid OperatorId);
public sealed record DecidePriceOverrideApprovalCommand(Guid StoreId, Guid ApprovalId, uint ExpectedVersion,
    string? Note, Guid CommandId, Guid ApproverId);
public sealed record ServiceOrderSearchCriteria(string? Query, Guid? CustomerId, Guid? CatalogItemId,
    Guid? EmployeeId, string? Status, DateOnly? FromDate, DateOnly? ToDate);

public interface ICashierService
{
    Task<PageResult<CashierVisitDto>> ListPendingVisitsAsync(Guid tenantId, Guid storeId, int page, int pageSize,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ServiceEmployeeDto>> ListServiceEmployeesAsync(Guid tenantId, Guid storeId,
        CancellationToken cancellationToken);
    Task<PageResult<ServiceOrderDto>> ListOrdersAsync(Guid tenantId, Guid storeId,
        ServiceOrderSearchCriteria criteria, int page, int pageSize, CancellationToken cancellationToken);
    Task<Result<ServiceOrderDto>> GetOrderAsync(Guid tenantId, Guid storeId, Guid orderId, CancellationToken cancellationToken);
    Task<Result<ServiceOrderDto>> CreateOrderAsync(Guid tenantId, CreateServiceOrderCommand command, CancellationToken cancellationToken);
    Task<Result<ServiceOrderDto>> ConfirmOrderAsync(Guid tenantId, ConfirmServiceOrderCommand command, CancellationToken cancellationToken);
    Task<Result<ServiceOrderDto>> VoidOrderAsync(Guid tenantId, VoidServiceOrderCommand command,
        CancellationToken cancellationToken);
    Task<PriceOverridePolicyDto> GetPriceOverridePolicyAsync(Guid tenantId, Guid operatorId,
        CancellationToken cancellationToken);
    Task<Result<PriceOverridePolicyDto>> UpdatePriceOverridePolicyAsync(Guid tenantId,
        UpdatePriceOverridePolicyCommand command, CancellationToken cancellationToken);
    Task<PageResult<PriceOverrideApprovalDto>> ListPriceOverrideApprovalsAsync(Guid tenantId, Guid storeId,
        string? status, int page, int pageSize, CancellationToken cancellationToken);
    Task<Result<PriceOverrideApprovalDto>> ApprovePriceOverrideAsync(Guid tenantId,
        DecidePriceOverrideApprovalCommand command, CancellationToken cancellationToken);
    Task<Result<PriceOverrideApprovalDto>> RejectPriceOverrideAsync(Guid tenantId,
        DecidePriceOverrideApprovalCommand command, CancellationToken cancellationToken);
}
