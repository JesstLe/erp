using Erp.Application.Reports;
using Erp.Domain.Cashier;
using Erp.Domain.Facilities;
using Erp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Erp.Infrastructure.Reports;

internal sealed class ReportService(ErpDbContext db, TimeProvider clock) : IReportService
{
    public async Task<OperationsReportDto> GetOperationsAsync(Guid tenantId, Guid storeId, DateOnly? fromDate,
        DateOnly? toDate, CancellationToken cancellationToken)
    {
        var timeZoneId = await db.Stores.Where(x => x.Id == storeId && x.TenantId == tenantId)
            .Select(x => x.TimeZoneId).SingleAsync(cancellationToken);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), timeZone).DateTime);
        var endDate = toDate ?? today;
        var startDate = fromDate ?? endDate.AddDays(-6);
        if (endDate < startDate) throw new ArgumentException("开始日期不得晚于结束日期");
        if (endDate.DayNumber - startDate.DayNumber > 91) throw new ArgumentException("单次报表最多查询92天");
        if (endDate == DateOnly.MaxValue) throw new ArgumentException("结束日期超出允许范围");
        var fromUtc = ToUtc(startDate, timeZone);
        var toUtc = ToUtc(endDate.AddDays(1), timeZone);
        var now = clock.GetUtcNow();

        var payments = await db.Payments.AsNoTracking().Include(x => x.Allocations).Where(x =>
            x.TenantId == tenantId && x.StoreId == storeId && x.Status == PaymentStatus.Paid &&
            x.PaidAtUtc >= fromUtc && x.PaidAtUtc < toUtc).ToListAsync(cancellationToken);
        var orderIds = payments.Select(x => x.OrderId).ToList();
        var orders = await db.ServiceOrders.AsNoTracking().Include(x => x.Lines)
            .Where(x => orderIds.Contains(x.Id)).ToListAsync(cancellationToken);
        var visits = await db.Visits.AsNoTracking().Where(x => x.TenantId == tenantId && x.StoreId == storeId &&
            x.ArrivedAtUtc >= fromUtc && x.ArrivedAtUtc < toUtc).ToListAsync(cancellationToken);
        var sessions = await db.FacilitySessions.AsNoTracking().Include(x => x.Pauses).Where(x =>
            x.TenantId == tenantId && x.StoreId == storeId && x.Status != FacilitySessionStatus.Cancelled &&
            x.StartedAtUtc < toUtc && (x.EndedAtUtc == null || x.EndedAtUtc >= fromUtc)).ToListAsync(cancellationToken);
        var facilities = await db.Facilities.AsNoTracking().Where(x => x.TenantId == tenantId && x.StoreId == storeId)
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken);

        var dates = Enumerable.Range(0, endDate.DayNumber - startDate.DayNumber + 1)
            .Select(offset => startDate.AddDays(offset)).ToList();
        var daily = dates.Select(date =>
        {
            var dayStart = ToUtc(date, timeZone);
            var dayEnd = ToUtc(date.AddDays(1), timeZone);
            var dayPayments = payments.Where(x => x.PaidAtUtc >= dayStart && x.PaidAtUtc < dayEnd).ToList();
            var allocations = dayPayments.SelectMany(x => x.Allocations).ToList();
            return new DailyOperationsDto(date, dayPayments.Sum(x => x.PaidMinor),
                allocations.Where(x => x.ConfirmationStatus != PaymentConfirmationStatus.ManualPendingReconciliation).Sum(x => x.AmountMinor),
                allocations.Where(x => x.ReconciliationStatus == ReconciliationStatus.Pending).Sum(x => x.AmountMinor),
                dayPayments.Count, visits.Count(x => x.ArrivedAtUtc >= dayStart && x.ArrivedAtUtc < dayEnd),
                sessions.Sum(x => x.GetActiveSecondsInRange(dayStart, dayEnd, now)));
        }).ToList();

        var allAllocations = payments.SelectMany(x => x.Allocations).ToList();
        var paymentMix = allAllocations.GroupBy(x => new { x.MethodCodeSnapshot, x.MethodNameSnapshot })
            .Select(group => new PaymentMixDto(group.Key.MethodCodeSnapshot, group.Key.MethodNameSnapshot,
                group.Sum(x => x.AmountMinor), group.Where(x => x.ReconciliationStatus == ReconciliationStatus.Pending)
                    .Sum(x => x.AmountMinor), group.Count()))
            .OrderByDescending(x => x.AmountMinor).ToList();
        var servicePerformance = orders.SelectMany(order => order.Lines.Select(line => new { order.Id, Line = line }))
            .GroupBy(x => new { x.Line.ServiceItemId, x.Line.ItemCodeSnapshot, x.Line.ItemNameSnapshot })
            .Select(group => new ServicePerformanceDto(group.Key.ServiceItemId, group.Key.ItemCodeSnapshot,
                group.Key.ItemNameSnapshot, group.Sum(x => x.Line.Quantity), group.Sum(x => x.Line.LineAmountMinor),
                group.Select(x => x.Id).Distinct().Count())).OrderByDescending(x => x.RevenueMinor).Take(20).ToList();
        var usageRows = sessions.GroupBy(x => x.FacilityId).Select(group => new
        {
            FacilityId = group.Key,
            Seconds = group.Sum(x => x.GetActiveSecondsInRange(fromUtc, toUtc, now)),
        }).Where(x => x.Seconds > 0).ToList();
        var totalUsage = usageRows.Sum(x => x.Seconds);
        var facilityUsage = usageRows.Select(x => new FacilityUsageDto(x.FacilityId,
                facilities.GetValueOrDefault(x.FacilityId, "已停用设施"), x.Seconds,
                totalUsage == 0 ? 0 : decimal.Round((decimal)x.Seconds / totalUsage, 4)))
            .OrderByDescending(x => x.ActiveSeconds).ToList();
        var settled = payments.Sum(x => x.PaidMinor);
        var recorded = allAllocations.Where(x => x.ConfirmationStatus != PaymentConfirmationStatus.ManualPendingReconciliation)
            .Sum(x => x.AmountMinor);
        var pending = allAllocations.Where(x => x.ReconciliationStatus == ReconciliationStatus.Pending)
            .Sum(x => x.AmountMinor);
        var summary = new OperationsSummaryDto(settled, recorded, pending, payments.Count, visits.Count,
            payments.Count == 0 ? 0 : settled / payments.Count, daily.Sum(x => x.FacilityActiveSeconds));
        return new OperationsReportDto(startDate, endDate, timeZoneId, summary, daily, paymentMix,
            servicePerformance, facilityUsage);
    }

    private static DateTimeOffset ToUtc(DateOnly date, TimeZoneInfo timeZone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, timeZone), TimeSpan.Zero);
    }
}
