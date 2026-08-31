using Erp.Application.Reports;
using Erp.Domain.Cashier;
using Erp.Domain.Customers;
using Erp.Domain.Facilities;
using Erp.Domain.Organization;
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
            x.TenantId == tenantId && x.StoreId == storeId &&
            (x.Status == PaymentStatus.Paid || x.Status == PaymentStatus.PartiallyRefunded ||
             x.Status == PaymentStatus.Refunded) &&
            x.BusinessType == PaymentBusinessType.ServiceOrder &&
            x.PaidAtUtc >= fromUtc && x.PaidAtUtc < toUtc).ToListAsync(cancellationToken);
        var refunds = await db.Refunds.AsNoTracking().Include(x => x.Lines).Where(x =>
            x.TenantId == tenantId && x.StoreId == storeId && x.Status == RefundStatus.Completed &&
            db.Payments.Any(payment => payment.Id == x.PaymentId &&
                payment.BusinessType == PaymentBusinessType.ServiceOrder) &&
            x.CompletedAtUtc >= fromUtc && x.CompletedAtUtc < toUtc).ToListAsync(cancellationToken);
        var refundedAllocationIds = refunds.SelectMany(x => x.Lines).Select(x => x.OriginalAllocationId)
            .Distinct().ToList();
        var refundedAllocations = await db.PaymentAllocations.AsNoTracking().Where(x =>
            refundedAllocationIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var orderIds = payments.Where(x => x.OrderId.HasValue).Select(x => x.OrderId!.Value).ToList();
        var orders = await db.ServiceOrders.AsNoTracking().Include(x => x.Lines)
            .Where(x => orderIds.Contains(x.Id)).ToListAsync(cancellationToken);
        var visits = await db.Visits.AsNoTracking().Where(x => x.TenantId == tenantId && x.StoreId == storeId &&
            x.ArrivedAtUtc >= fromUtc && x.ArrivedAtUtc < toUtc).ToListAsync(cancellationToken);
        var sessions = await db.FacilitySessions.AsNoTracking().Include(x => x.Pauses).Where(x =>
            x.TenantId == tenantId && x.StoreId == storeId && x.Status != FacilitySessionStatus.Cancelled &&
            x.StartedAtUtc < toUtc && (x.EndedAtUtc == null || x.EndedAtUtc >= fromUtc)).ToListAsync(cancellationToken);
        var facilities = await db.Facilities.AsNoTracking().Where(x => x.TenantId == tenantId && x.StoreId == storeId)
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken);
        var storedValue = await db.MemberTopupOrders.AsNoTracking().Where(x =>
                x.TenantId == tenantId && x.StoreId == storeId &&
                x.Status != Erp.Domain.Customers.MemberTopupStatus.Cancelled)
            .Select(x => new
            {
                Principal = x.PrincipalMinor - x.RefundedPrincipalMinor,
                Bonus = x.BonusMinor - x.RevokedBonusMinor,
            }).ToListAsync(cancellationToken);

        var dates = Enumerable.Range(0, endDate.DayNumber - startDate.DayNumber + 1)
            .Select(offset => startDate.AddDays(offset)).ToList();
        var daily = dates.Select(date =>
        {
            var dayStart = ToUtc(date, timeZone);
            var dayEnd = ToUtc(date.AddDays(1), timeZone);
            var dayPayments = payments.Where(x => x.PaidAtUtc >= dayStart && x.PaidAtUtc < dayEnd).ToList();
            var allocations = dayPayments.SelectMany(x => x.Allocations).ToList();
            var dayRefunds = refunds.Where(x => x.CompletedAtUtc >= dayStart && x.CompletedAtUtc < dayEnd)
                .SelectMany(x => x.Lines).ToList();
            var gross = dayPayments.Sum(x => x.PaidMinor);
            var refundAmount = dayRefunds.Sum(x => x.AmountMinor);
            return new DailyOperationsDto(date, gross,
                allocations.Where(x => x.ConfirmationStatus != PaymentConfirmationStatus.ManualPendingReconciliation).Sum(x => x.AmountMinor) -
                    dayRefunds.Sum(x => x.AmountMinor),
                allocations.Where(x => x.ReconciliationStatus == ReconciliationStatus.Pending).Sum(x => x.AmountMinor),
                refundAmount, gross - refundAmount,
                dayPayments.Count, visits.Count(x => x.ArrivedAtUtc >= dayStart && x.ArrivedAtUtc < dayEnd),
                sessions.Sum(x => x.GetActiveSecondsInRange(dayStart, dayEnd, now)));
        }).ToList();

        var allAllocations = payments.SelectMany(x => x.Allocations).ToList();
        var paymentMix = allAllocations.Select(x => new
            {
                x.MethodCodeSnapshot, x.MethodNameSnapshot, Gross = x.AmountMinor,
                Pending = x.ReconciliationStatus == ReconciliationStatus.Pending ? x.AmountMinor : 0L,
                Refund = 0L, Count = 1
            }).Concat(refunds.SelectMany(x => x.Lines).Select(line =>
            {
                var allocation = refundedAllocations[line.OriginalAllocationId];
                return new
                {
                    allocation.MethodCodeSnapshot, allocation.MethodNameSnapshot, Gross = 0L,
                    Pending = 0L, Refund = line.AmountMinor, Count = 0
                };
            })).GroupBy(x => new { x.MethodCodeSnapshot, x.MethodNameSnapshot })
            .Select(group => new PaymentMixDto(group.Key.MethodCodeSnapshot, group.Key.MethodNameSnapshot,
                group.Sum(x => x.Gross), group.Sum(x => x.Pending), group.Sum(x => x.Refund),
                group.Sum(x => x.Gross) - group.Sum(x => x.Refund), group.Sum(x => x.Count)))
            .OrderByDescending(x => x.AmountMinor).ToList();
        var servicePerformance = orders.SelectMany(order => order.Lines
                .Where(line => line.LineType == ServiceOrderLineType.Service && line.ServiceItemId.HasValue)
                .Select(line => new { order.Id, Line = line }))
            .GroupBy(x => new { x.Line.ServiceItemId, x.Line.ItemCodeSnapshot, x.Line.ItemNameSnapshot })
            .Select(group => new ServicePerformanceDto(group.Key.ServiceItemId!.Value, group.Key.ItemCodeSnapshot,
                group.Key.ItemNameSnapshot, group.Sum(x => x.Line.Quantity), group.Sum(x => x.Line.LineAmountMinor),
                group.Select(x => x.Id).Distinct().Count())).OrderByDescending(x => x.RevenueMinor).Take(20).ToList();
        var employeeCommissions = orders.SelectMany(order => order.Lines
                .Where(line => line.LineType == ServiceOrderLineType.Service && line.ServiceEmployeeId.HasValue)
                .Select(line => new
                {
                    Order = order,
                    Line = line,
                    RefundDeduction = AllocateRefundDeduction(line.CommissionAmountMinor, order.RefundedMinor,
                        order.ReceivableMinor),
                }))
            .GroupBy(x => new
            {
                EmployeeId = x.Line.ServiceEmployeeId!.Value,
                EmployeeNo = x.Line.EmployeeNoSnapshot!,
                EmployeeName = x.Line.EmployeeNameSnapshot!,
            })
            .Select(group => new EmployeeCommissionDto(group.Key.EmployeeId, group.Key.EmployeeNo,
                group.Key.EmployeeName, group.Sum(x => x.Line.Quantity),
                group.Select(x => x.Order.Id).Distinct().Count(), group.Sum(x => x.Line.LineAmountMinor),
                group.Sum(x => x.Line.CommissionAmountMinor), group.Sum(x => x.RefundDeduction),
                group.Sum(x => x.Line.CommissionAmountMinor - x.RefundDeduction)))
            .OrderByDescending(x => x.NetCommissionMinor).ThenBy(x => x.EmployeeName).ToList();
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
        var refunded = refunds.Sum(x => x.AmountMinor);
        var recorded = allAllocations.Where(x => x.ConfirmationStatus != PaymentConfirmationStatus.ManualPendingReconciliation)
            .Sum(x => x.AmountMinor);
        var pending = allAllocations.Where(x => x.ReconciliationStatus == ReconciliationStatus.Pending)
            .Sum(x => x.AmountMinor);
        var todayRevenue = daily.SingleOrDefault(x => x.Date == today)?.NetRevenueMinor;
        if (!todayRevenue.HasValue)
        {
            var todayFromUtc = ToUtc(today, timeZone);
            var todayToUtc = ToUtc(today.AddDays(1), timeZone);
            var todaySettled = await db.Payments.AsNoTracking().Where(x => x.TenantId == tenantId &&
                    x.StoreId == storeId && x.BusinessType == PaymentBusinessType.ServiceOrder &&
                    (x.Status == PaymentStatus.Paid || x.Status == PaymentStatus.PartiallyRefunded ||
                     x.Status == PaymentStatus.Refunded) && x.PaidAtUtc >= todayFromUtc &&
                    x.PaidAtUtc < todayToUtc)
                .SumAsync(x => (long?)x.PaidMinor, cancellationToken) ?? 0;
            var todayRefunded = await db.Refunds.AsNoTracking().Where(x => x.TenantId == tenantId &&
                    x.StoreId == storeId && x.Status == RefundStatus.Completed &&
                    x.CompletedAtUtc >= todayFromUtc && x.CompletedAtUtc < todayToUtc &&
                    db.Payments.Any(payment => payment.Id == x.PaymentId &&
                        payment.BusinessType == PaymentBusinessType.ServiceOrder))
                .SumAsync(x => (long?)x.AmountMinor, cancellationToken) ?? 0;
            todayRevenue = todaySettled - todayRefunded;
        }
        var summary = new OperationsSummaryDto(settled, recorded - refunded, pending, refunded,
            settled - refunded, todayRevenue.Value, payments.Count, visits.Count,
            payments.Count == 0 ? 0 : settled / payments.Count, daily.Sum(x => x.FacilityActiveSeconds),
            storedValue.Sum(x => x.Principal), storedValue.Sum(x => x.Bonus),
            storedValue.Sum(x => checked(x.Principal + x.Bonus)));
        return new OperationsReportDto(startDate, endDate, timeZoneId, summary, daily, paymentMix,
            servicePerformance, employeeCommissions, facilityUsage);
    }

    public async Task<BrandStoreFinancialOverviewDto> GetStoreOverviewAsync(Guid tenantId,
        DateOnly? fromDate, DateOnly? toDate, CancellationToken cancellationToken)
    {
        var stores = await db.Stores.AsNoTracking().Where(x => x.TenantId == tenantId &&
                x.Status == StoreStatus.Enabled).OrderBy(x => x.Code).ToListAsync(cancellationToken);
        if (stores.Count == 0)
            throw new ArgumentException("当前品牌没有可用门店");

        var referenceTimeZone = TimeZoneInfo.FindSystemTimeZoneById(stores[0].TimeZoneId);
        var referenceToday = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(clock.GetUtcNow(), referenceTimeZone).DateTime);
        var endDate = toDate ?? referenceToday;
        var startDate = fromDate ?? endDate.AddDays(-6);
        ValidateDateRange(startDate, endDate);

        var periods = stores.ToDictionary(store => store.Id, store =>
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(store.TimeZoneId);
            var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
            return new StorePeriod(localToday, ToUtc(startDate, zone), ToUtc(endDate.AddDays(1), zone),
                ToUtc(localToday, zone), ToUtc(localToday.AddDays(1), zone));
        });
        var storeIds = stores.Select(x => x.Id).ToArray();
        var minimumUtc = periods.Values.Min(x => x.PeriodFromUtc < x.TodayFromUtc
            ? x.PeriodFromUtc : x.TodayFromUtc);
        var maximumUtc = periods.Values.Max(x => x.PeriodToUtc > x.TodayToUtc
            ? x.PeriodToUtc : x.TodayToUtc);

        var payments = await db.Payments.AsNoTracking().Where(x => x.TenantId == tenantId &&
                storeIds.Contains(x.StoreId) && x.BusinessType == PaymentBusinessType.ServiceOrder &&
                (x.Status == PaymentStatus.Paid || x.Status == PaymentStatus.PartiallyRefunded ||
                 x.Status == PaymentStatus.Refunded) && x.PaidAtUtc >= minimumUtc && x.PaidAtUtc < maximumUtc)
            .Select(x => new { x.StoreId, x.PaidMinor, x.PaidAtUtc }).ToListAsync(cancellationToken);
        var refunds = await db.Refunds.AsNoTracking().Where(x => x.TenantId == tenantId &&
                storeIds.Contains(x.StoreId) && x.Status == RefundStatus.Completed &&
                x.CompletedAtUtc >= minimumUtc && x.CompletedAtUtc < maximumUtc &&
                db.Payments.Any(payment => payment.Id == x.PaymentId &&
                    payment.BusinessType == PaymentBusinessType.ServiceOrder))
            .Select(x => new { x.StoreId, x.AmountMinor, x.CompletedAtUtc }).ToListAsync(cancellationToken);
        var storedValues = await db.MemberTopupOrders.AsNoTracking().Where(x => x.TenantId == tenantId &&
                storeIds.Contains(x.StoreId) &&
                x.Status != Erp.Domain.Customers.MemberTopupStatus.Cancelled)
            .Select(x => new
            {
                x.StoreId,
                Principal = x.PrincipalMinor - x.RefundedPrincipalMinor,
                Bonus = x.BonusMinor - x.RevokedBonusMinor,
            }).ToListAsync(cancellationToken);
        var pendingAllocations = await (from allocation in db.PaymentAllocations.AsNoTracking()
            join payment in db.Payments.AsNoTracking() on allocation.PaymentId equals payment.Id
            where payment.TenantId == tenantId && storeIds.Contains(payment.StoreId) &&
                  allocation.ReconciliationStatus == ReconciliationStatus.Pending &&
                  (payment.Status == PaymentStatus.Paid || payment.Status == PaymentStatus.PartiallyRefunded ||
                   payment.Status == PaymentStatus.Refunded)
            select new { payment.StoreId, allocation.AmountMinor }).ToListAsync(cancellationToken);
        var shifts = await db.CashierShifts.AsNoTracking().Where(x => x.TenantId == tenantId &&
                storeIds.Contains(x.StoreId) &&
                (x.Status == CashierShiftStatus.Open || x.Status == CashierShiftStatus.ReviewPending))
            .Select(x => new { x.StoreId, x.Status, x.PendingReconciliationMinor })
            .ToListAsync(cancellationToken);
        var runs = await db.PaymentChannelReconciliationRuns.AsNoTracking().Where(x =>
                x.TenantId == tenantId && storeIds.Contains(x.StoreId) &&
                x.BusinessDate >= startDate && x.BusinessDate <= endDate &&
                (x.Status == PaymentChannelReconciliationRunStatus.Matched ||
                 x.Status == PaymentChannelReconciliationRunStatus.Differences))
            .Select(x => new { x.Id, x.StoreId, x.Provider, x.BusinessDate, x.AttemptNo })
            .ToListAsync(cancellationToken);
        var latestRuns = runs.GroupBy(x => new { x.StoreId, x.Provider, x.BusinessDate })
            .Select(group => group.OrderByDescending(x => x.AttemptNo).ThenByDescending(x => x.Id).First())
            .ToList();
        var latestRunIds = latestRuns.Select(x => x.Id).ToArray();
        var unresolvedRunIds = await db.PaymentChannelReconciliationItems.AsNoTracking().Where(x =>
                latestRunIds.Contains(x.RunId) &&
                x.Status != PaymentChannelReconciliationItemStatus.Matched &&
                x.Status != PaymentChannelReconciliationItemStatus.Resolved)
            .Select(x => x.RunId).ToListAsync(cancellationToken);

        var rows = stores.Select(store =>
        {
            var period = periods[store.Id];
            var storePayments = payments.Where(x => x.StoreId == store.Id &&
                x.PaidAtUtc >= period.PeriodFromUtc && x.PaidAtUtc < period.PeriodToUtc).ToList();
            var storeRefunds = refunds.Where(x => x.StoreId == store.Id &&
                x.CompletedAtUtc >= period.PeriodFromUtc && x.CompletedAtUtc < period.PeriodToUtc).ToList();
            var todayPayments = payments.Where(x => x.StoreId == store.Id &&
                x.PaidAtUtc >= period.TodayFromUtc &&
                x.PaidAtUtc < period.TodayToUtc).Sum(x => x.PaidMinor);
            var todayRefunds = refunds.Where(x => x.StoreId == store.Id &&
                x.CompletedAtUtc >= period.TodayFromUtc &&
                x.CompletedAtUtc < period.TodayToUtc).Sum(x => x.AmountMinor);
            var topups = storedValues.Where(x => x.StoreId == store.Id).ToList();
            var pending = pendingAllocations.Where(x => x.StoreId == store.Id).ToList();
            var storeShifts = shifts.Where(x => x.StoreId == store.Id).ToList();
            var storeRunIds = latestRuns.Where(x => x.StoreId == store.Id).Select(x => x.Id).ToHashSet();
            return new StoreFinancialOverviewDto(store.Id, store.Code, store.Name, store.TimeZoneId,
                period.LocalDate, todayPayments - todayRefunds, storePayments.Sum(x => x.PaidMinor),
                storeRefunds.Sum(x => x.AmountMinor),
                storePayments.Sum(x => x.PaidMinor) - storeRefunds.Sum(x => x.AmountMinor),
                topups.Sum(x => x.Principal), topups.Sum(x => x.Bonus),
                topups.Sum(x => checked(x.Principal + x.Bonus)), pending.Sum(x => x.AmountMinor),
                pending.Count, unresolvedRunIds.Count(storeRunIds.Contains),
                storeShifts.Count(x => x.Status == CashierShiftStatus.Open),
                storeShifts.Count(x => x.Status == CashierShiftStatus.ReviewPending),
                storeShifts.Where(x => x.Status == CashierShiftStatus.ReviewPending)
                    .Sum(x => x.PendingReconciliationMinor ?? 0));
        }).ToList();

        return new BrandStoreFinancialOverviewDto(startDate, endDate, rows.Sum(x => x.TodayRevenueMinor),
            rows.Sum(x => x.PeriodNetRevenueMinor), rows.Sum(x => x.StoredValueNetMinor),
            rows.Sum(x => x.PendingReconciliationMinor), rows.Sum(x => x.ChannelDifferenceCount), rows);
    }

    public async Task<DashboardOverviewDto> GetDashboardOverviewAsync(Guid tenantId, Guid? storeId,
        CancellationToken cancellationToken)
    {
        var stores = await db.Stores.AsNoTracking().Where(x => x.TenantId == tenantId &&
                x.Status == StoreStatus.Enabled && (!storeId.HasValue || x.Id == storeId.Value))
            .OrderBy(x => x.Code).ToListAsync(cancellationToken);
        if (stores.Count == 0)
            throw new ArgumentException("当前经营范围没有可用门店");

        var storeIds = stores.Select(x => x.Id).ToArray();
        var zones = stores.ToDictionary(x => x.Id,
            x => TimeZoneInfo.FindSystemTimeZoneById(x.TimeZoneId));
        var localTodayByStore = stores.ToDictionary(x => x.Id, x => DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zones[x.Id]).DateTime));
        var referenceToday = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zones[stores[0].Id]).DateTime);
        var trendFromDate = referenceToday.AddDays(-29);
        var monthFromDate = new DateOnly(referenceToday.Year, referenceToday.Month, 1);
        var financialOverview = await GetStoreOverviewAsync(tenantId, monthFromDate, referenceToday,
            cancellationToken);
        var financialRows = financialOverview.Stores.Where(x => storeIds.Contains(x.StoreId))
            .ToDictionary(x => x.StoreId);

        var minimumRecentUtc = stores.Min(store => ToUtc(trendFromDate, zones[store.Id]));
        var maximumRecentUtc = stores.Max(store => ToUtc(referenceToday.AddDays(1), zones[store.Id]));
        var recentPayments = await db.Payments.AsNoTracking().Include(x => x.Allocations).Where(x =>
                x.TenantId == tenantId && storeIds.Contains(x.StoreId) &&
                x.BusinessType == PaymentBusinessType.ServiceOrder &&
                (x.Status == PaymentStatus.Paid || x.Status == PaymentStatus.PartiallyRefunded ||
                 x.Status == PaymentStatus.Refunded) && x.PaidAtUtc >= minimumRecentUtc &&
                x.PaidAtUtc < maximumRecentUtc)
            .ToListAsync(cancellationToken);
        var recentRefunds = await db.Refunds.AsNoTracking().Where(x => x.TenantId == tenantId &&
                storeIds.Contains(x.StoreId) && x.Status == RefundStatus.Completed &&
                x.CompletedAtUtc >= minimumRecentUtc && x.CompletedAtUtc < maximumRecentUtc &&
                db.Payments.Any(payment => payment.Id == x.PaymentId &&
                    payment.BusinessType == PaymentBusinessType.ServiceOrder))
            .Select(x => new { x.StoreId, x.AmountMinor, x.CompletedAtUtc })
            .ToListAsync(cancellationToken);
        var recentVisits = await db.Visits.AsNoTracking().Where(x => x.TenantId == tenantId &&
                storeIds.Contains(x.StoreId) && x.ArrivedAtUtc >= minimumRecentUtc &&
                x.ArrivedAtUtc < maximumRecentUtc)
            .Select(x => new { x.StoreId, x.ArrivedAtUtc }).ToListAsync(cancellationToken);

        var paymentDays = recentPayments.Select(x => new
        {
            Payment = x,
            Date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.PaidAtUtc!.Value, zones[x.StoreId]).DateTime),
        }).ToList();
        var refundDays = recentRefunds.Select(x => new
        {
            x.AmountMinor,
            Date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.CompletedAtUtc!.Value,
                zones[x.StoreId]).DateTime),
        }).ToList();
        var visitDays = recentVisits.Select(x => new
        {
            x.StoreId,
            Date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.ArrivedAtUtc,
                zones[x.StoreId]).DateTime),
        }).ToList();
        var trend = Enumerable.Range(0, 30).Select(offset =>
        {
            var date = trendFromDate.AddDays(offset);
            var dayPayments = paymentDays.Where(x => x.Date == date).ToList();
            return new DashboardTrendDto(date,
                dayPayments.Sum(x => x.Payment.PaidMinor) - refundDays.Where(x => x.Date == date)
                    .Sum(x => x.AmountMinor), dayPayments.Count, visitDays.Count(x => x.Date == date));
        }).ToList();

        var lifetimePayments = await db.Payments.AsNoTracking().Where(x => x.TenantId == tenantId &&
                storeIds.Contains(x.StoreId) && x.BusinessType == PaymentBusinessType.ServiceOrder &&
                (x.Status == PaymentStatus.Paid || x.Status == PaymentStatus.PartiallyRefunded ||
                 x.Status == PaymentStatus.Refunded))
            .GroupBy(x => x.StoreId).Select(group => new { StoreId = group.Key, Amount = group.Sum(x => x.PaidMinor) })
            .ToDictionaryAsync(x => x.StoreId, x => x.Amount, cancellationToken);
        var lifetimeRefunds = await db.Refunds.AsNoTracking().Where(x => x.TenantId == tenantId &&
                storeIds.Contains(x.StoreId) && x.Status == RefundStatus.Completed &&
                db.Payments.Any(payment => payment.Id == x.PaymentId &&
                    payment.BusinessType == PaymentBusinessType.ServiceOrder))
            .GroupBy(x => x.StoreId).Select(group => new { StoreId = group.Key, Amount = group.Sum(x => x.AmountMinor) })
            .ToDictionaryAsync(x => x.StoreId, x => x.Amount, cancellationToken);

        var accountBalances = await (from account in db.MemberAccounts.AsNoTracking()
            join card in db.MemberCards.AsNoTracking() on account.CardId equals card.Id
            where account.TenantId == tenantId && storeIds.Contains(card.StoreId) &&
                  (account.AccountType == MemberAccountType.Principal ||
                   account.AccountType == MemberAccountType.Bonus)
            select new { card.StoreId, account.AccountType, account.BalanceUnits })
            .ToListAsync(cancellationToken);
        var activeCards = await db.MemberCards.AsNoTracking().Where(x => x.TenantId == tenantId &&
                storeIds.Contains(x.StoreId) && x.Status == MemberCardStatus.Active)
            .Select(x => new { x.StoreId, x.CustomerId, x.ValidTo }).ToListAsync(cancellationToken);
        var activeCustomers = await db.Customers.AsNoTracking().Where(x => x.TenantId == tenantId &&
                storeIds.Contains(x.HomeStoreId) && x.Status == CustomerStatus.Active)
            .GroupBy(x => x.HomeStoreId).Select(group => new { StoreId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.StoreId, x => x.Count, cancellationToken);
        var activeFacilities = await db.FacilitySessions.AsNoTracking().Where(x => x.TenantId == tenantId &&
                storeIds.Contains(x.StoreId) &&
                (x.Status == FacilitySessionStatus.Active || x.Status == FacilitySessionStatus.Paused))
            .Select(x => new { x.StoreId, x.FacilityId }).Distinct().ToListAsync(cancellationToken);

        var storeSnapshots = stores.Select(store =>
        {
            var financial = financialRows[store.Id];
            var balances = accountBalances.Where(x => x.StoreId == store.Id).ToList();
            var principal = balances.Where(x => x.AccountType == MemberAccountType.Principal)
                .Sum(x => x.BalanceUnits);
            var bonus = balances.Where(x => x.AccountType == MemberAccountType.Bonus)
                .Sum(x => x.BalanceUnits);
            var localToday = financial.LocalDate;
            var memberCount = activeCards.Where(x => x.StoreId == store.Id &&
                    (!x.ValidTo.HasValue || x.ValidTo.Value >= localToday))
                .Select(x => x.CustomerId).Distinct().Count();
            var lifetime = lifetimePayments.GetValueOrDefault(store.Id) -
                           lifetimeRefunds.GetValueOrDefault(store.Id);
            return new DashboardStoreSnapshotDto(store.Id, store.Code, store.Name,
                financial.TodayRevenueMinor, financial.PeriodNetRevenueMinor, lifetime,
                principal, bonus, checked(principal + bonus), memberCount,
                activeFacilities.Count(x => x.StoreId == store.Id), financial.PendingReconciliationMinor,
                financial.PendingReconciliationCount, financial.OpenShiftCount,
                financial.ReviewPendingShiftCount);
        }).ToList();

        var paymentMix = paymentDays.Where(x => x.Date >= trendFromDate && x.Date <= referenceToday)
            .SelectMany(x => x.Payment.Allocations).GroupBy(x => new
            {
                x.MethodCodeSnapshot,
                x.MethodNameSnapshot,
            }).Select(group => new DashboardPaymentMixDto(group.Key.MethodCodeSnapshot,
                group.Key.MethodNameSnapshot, group.Sum(x => x.AmountMinor), group.Count()))
            .OrderByDescending(x => x.AmountMinor).ToList();
        var principalBalance = storeSnapshots.Sum(x => x.StoredValuePrincipalBalanceMinor);
        var bonusBalance = storeSnapshots.Sum(x => x.StoredValueBonusBalanceMinor);
        var todaySettledOrders = paymentDays.Count(x => x.Date == localTodayByStore[x.Payment.StoreId]);
        var todayVisits = visitDays.Count(x => x.Date == localTodayByStore[x.StoreId]);
        var scopeName = stores.Count == 1 ? stores[0].Name : $"全部门店（{stores.Count} 家）";

        return new DashboardOverviewDto(scopeName, trendFromDate, referenceToday,
            storeSnapshots.Sum(x => x.TodayRevenueMinor), storeSnapshots.Sum(x => x.MonthRevenueMinor),
            storeSnapshots.Sum(x => x.LifetimeRevenueMinor), principalBalance, bonusBalance,
            checked(principalBalance + bonusBalance), storeSnapshots.Sum(x => x.ActiveMemberCount),
            activeCustomers.Values.Sum(), todayVisits, todaySettledOrders,
            storeSnapshots.Sum(x => x.ActiveFacilityCount),
            storeSnapshots.Sum(x => x.PendingReconciliationMinor),
            storeSnapshots.Sum(x => x.PendingReconciliationCount),
            storeSnapshots.Sum(x => x.OpenShiftCount), storeSnapshots.Sum(x => x.ReviewPendingShiftCount),
            trend, paymentMix, storeSnapshots);
    }

    private static long AllocateRefundDeduction(long commissionMinor, long refundedMinor, long receivableMinor)
    {
        if (commissionMinor <= 0 || refundedMinor <= 0 || receivableMinor <= 0) return 0;
        return Math.Min(commissionMinor, (long)decimal.Round(
            (decimal)commissionMinor * refundedMinor / receivableMinor, 0, MidpointRounding.AwayFromZero));
    }

    private static DateTimeOffset ToUtc(DateOnly date, TimeZoneInfo timeZone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, timeZone), TimeSpan.Zero);
    }

    private static void ValidateDateRange(DateOnly startDate, DateOnly endDate)
    {
        if (endDate < startDate) throw new ArgumentException("开始日期不得晚于结束日期");
        if (endDate.DayNumber - startDate.DayNumber > 91) throw new ArgumentException("单次报表最多查询92天");
        if (endDate == DateOnly.MaxValue) throw new ArgumentException("结束日期超出允许范围");
    }

    private sealed record StorePeriod(DateOnly LocalDate, DateTimeOffset PeriodFromUtc, DateTimeOffset PeriodToUtc,
        DateTimeOffset TodayFromUtc, DateTimeOffset TodayToUtc);
}
