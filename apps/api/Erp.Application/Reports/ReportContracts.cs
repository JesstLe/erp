namespace Erp.Application.Reports;

public sealed record OperationsSummaryDto(long SettledRevenueMinor, long RecordedFundsMinor,
    long PendingReconciliationMinor, long RefundMinor, long NetRevenueMinor, long TodayRevenueMinor,
    int SettledOrderCount, int VisitCount, long AverageTicketMinor, long FacilityActiveSeconds,
    long StoredValuePrincipalMinor, long StoredValueBonusMinor, long StoredValueNetMinor);
public sealed record DailyOperationsDto(DateOnly Date, long SettledRevenueMinor, long RecordedFundsMinor,
    long PendingReconciliationMinor, long RefundMinor, long NetRevenueMinor, int OrderCount, int VisitCount,
    long FacilityActiveSeconds);
public sealed record PaymentMixDto(string MethodCode, string MethodName, long AmountMinor,
    long PendingReconciliationMinor, long RefundMinor, long NetAmountMinor, int AllocationCount);
public sealed record ServicePerformanceDto(Guid ServiceItemId, string ItemCode, string ItemName,
    int Quantity, long RevenueMinor, int OrderCount);
public sealed record EmployeeCommissionDto(Guid EmployeeId, string EmployeeNo, string EmployeeName,
    int ServiceQuantity, int OrderCount, long GrossServiceRevenueMinor, long GrossCommissionMinor,
    long RefundDeductionMinor, long NetCommissionMinor);
public sealed record FacilityUsageDto(Guid FacilityId, string FacilityName, long ActiveSeconds, decimal UsageShare);
public sealed record OperationsReportDto(DateOnly FromDate, DateOnly ToDate, string TimeZoneId,
    OperationsSummaryDto Summary, IReadOnlyList<DailyOperationsDto> Daily,
    IReadOnlyList<PaymentMixDto> PaymentMix, IReadOnlyList<ServicePerformanceDto> Services,
    IReadOnlyList<EmployeeCommissionDto> EmployeeCommissions, IReadOnlyList<FacilityUsageDto> Facilities);
public sealed record StoreFinancialOverviewDto(Guid StoreId, string StoreCode, string StoreName,
    string TimeZoneId, DateOnly LocalDate, long TodayRevenueMinor, long PeriodRevenueMinor,
    long PeriodRefundMinor, long PeriodNetRevenueMinor, long StoredValuePrincipalMinor,
    long StoredValueBonusMinor, long StoredValueNetMinor, long PendingReconciliationMinor,
    int PendingReconciliationCount, int ChannelDifferenceCount, int OpenShiftCount,
    int ReviewPendingShiftCount, long ReviewPendingShiftAmountMinor);
public sealed record BrandStoreFinancialOverviewDto(DateOnly FromDate, DateOnly ToDate,
    long TodayRevenueMinor, long PeriodNetRevenueMinor, long StoredValueNetMinor,
    long PendingReconciliationMinor, int ChannelDifferenceCount,
    IReadOnlyList<StoreFinancialOverviewDto> Stores);
public sealed record DashboardTrendDto(DateOnly Date, long NetRevenueMinor, int OrderCount, int VisitCount);
public sealed record DashboardPaymentMixDto(string MethodCode, string MethodName, long AmountMinor,
    int AllocationCount);
public sealed record DashboardStoreSnapshotDto(Guid StoreId, string StoreCode, string StoreName,
    long TodayRevenueMinor, long MonthRevenueMinor, long LifetimeRevenueMinor,
    long StoredValuePrincipalBalanceMinor, long StoredValueBonusBalanceMinor,
    long StoredValueBalanceMinor, int ActiveMemberCount, int ActiveFacilityCount,
    long PendingReconciliationMinor, int PendingReconciliationCount, int OpenShiftCount,
    int ReviewPendingShiftCount);
public sealed record DashboardOverviewDto(string ScopeName, DateOnly TrendFromDate, DateOnly TrendToDate,
    long TodayRevenueMinor, long MonthRevenueMinor, long LifetimeRevenueMinor,
    long StoredValuePrincipalBalanceMinor, long StoredValueBonusBalanceMinor,
    long StoredValueBalanceMinor, int ActiveMemberCount, int ActiveCustomerCount,
    int TodayVisitCount, int TodaySettledOrderCount, int ActiveFacilityCount,
    long PendingReconciliationMinor, int PendingReconciliationCount, int OpenShiftCount,
    int ReviewPendingShiftCount, IReadOnlyList<DashboardTrendDto> Trend,
    IReadOnlyList<DashboardPaymentMixDto> PaymentMix, IReadOnlyList<DashboardStoreSnapshotDto> Stores);

public interface IReportService
{
    Task<EmployeeCommissionReportDto> GetEmployeeCommissionsAsync(Guid tenantId, IReadOnlyList<Guid> storeIds,
        DateOnly? fromDate, DateOnly? toDate, string? query, int page, int pageSize,
        CancellationToken cancellationToken);
    Task<OperationsReportDto> GetOperationsAsync(Guid tenantId, Guid storeId, DateOnly? fromDate,
        DateOnly? toDate, CancellationToken cancellationToken);
    Task<BrandStoreFinancialOverviewDto> GetStoreOverviewAsync(Guid tenantId, DateOnly? fromDate,
        DateOnly? toDate, CancellationToken cancellationToken);
    Task<DashboardOverviewDto> GetDashboardOverviewAsync(Guid tenantId, Guid? storeId,
        CancellationToken cancellationToken);
}

public sealed record EmployeeCommissionLineDto(Guid LineId, Guid OrderId, string OrderNo,
    Guid StoreId, string StoreCode, string StoreName, DateTimeOffset PaidAtUtc,
    Guid EmployeeId, string EmployeeNo, string EmployeeName, string? PositionCode, string? PositionName,
    string ItemCode, string ItemName, int Quantity, long UnitPriceMinor, long LineAmountMinor,
    string CommissionMode, int? RateBasisPoints, long? FixedMinor, string? RuleSource,
    long GrossCommissionMinor, long RefundDeductionMinor, long NetCommissionMinor, long OrderRefundedMinor);
public sealed record EmployeeCommissionTotalsDto(int OrderCount, int LineCount, int ServiceQuantity,
    long ServiceRevenueMinor, long GrossCommissionMinor, long RefundDeductionMinor, long NetCommissionMinor);
public sealed record EmployeeCommissionReportDto(string TimeZoneId, EmployeeCommissionTotalsDto Totals,
    IReadOnlyList<EmployeeCommissionDto> Employees, IReadOnlyList<EmployeeCommissionLineDto> Items,
    int Total, int Page, int PageSize);
