namespace Erp.Application.Reports;

public sealed record OperationsSummaryDto(long SettledRevenueMinor, long RecordedFundsMinor,
    long PendingReconciliationMinor, int SettledOrderCount, int VisitCount, long AverageTicketMinor,
    long FacilityActiveSeconds);
public sealed record DailyOperationsDto(DateOnly Date, long SettledRevenueMinor, long RecordedFundsMinor,
    long PendingReconciliationMinor, int OrderCount, int VisitCount, long FacilityActiveSeconds);
public sealed record PaymentMixDto(string MethodCode, string MethodName, long AmountMinor,
    long PendingReconciliationMinor, int AllocationCount);
public sealed record ServicePerformanceDto(Guid ServiceItemId, string ItemCode, string ItemName,
    int Quantity, long RevenueMinor, int OrderCount);
public sealed record FacilityUsageDto(Guid FacilityId, string FacilityName, long ActiveSeconds, decimal UsageShare);
public sealed record OperationsReportDto(DateOnly FromDate, DateOnly ToDate, string TimeZoneId,
    OperationsSummaryDto Summary, IReadOnlyList<DailyOperationsDto> Daily,
    IReadOnlyList<PaymentMixDto> PaymentMix, IReadOnlyList<ServicePerformanceDto> Services,
    IReadOnlyList<FacilityUsageDto> Facilities);

public interface IReportService
{
    Task<OperationsReportDto> GetOperationsAsync(Guid tenantId, Guid storeId, DateOnly? fromDate,
        DateOnly? toDate, CancellationToken cancellationToken);
}
