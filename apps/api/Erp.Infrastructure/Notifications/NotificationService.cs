using Erp.Application.Notifications;
using Erp.Application.Security;
using Erp.Domain.Cashier;
using Erp.Domain.Customers;
using Erp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace Erp.Infrastructure.Notifications;

internal sealed class NotificationService(ErpDbContext db) : INotificationService
{
    public async Task<NotificationInboxDto> GetInboxAsync(Guid tenantId, Guid storeId, Guid userId,
        IReadOnlyList<string> roles, CancellationToken cancellationToken)
    {
        var items = new List<NotificationItemDto>();
        var owner = roles.Contains(SystemRoles.Owner, StringComparer.OrdinalIgnoreCase);
        var reviewer = owner || roles.Contains(SystemRoles.StoreManager, StringComparer.OrdinalIgnoreCase);

        if (owner)
        {
            var priceApprovals = await (from approval in db.PriceOverrideApprovals.AsNoTracking()
                                        join order in db.ServiceOrders.AsNoTracking()
                                            on approval.ServiceOrderId equals order.Id
                                        where approval.TenantId == tenantId && approval.StoreId == storeId &&
                                            approval.Status == PriceOverrideApprovalStatus.Pending
                                        orderby approval.RequestedAtUtc descending
                                        select new { approval.Id, order.OrderNo, approval.DifferenceMinor,
                                            approval.RequestedAtUtc }).Take(20).ToListAsync(cancellationToken);
            items.AddRange(priceApprovals.Select(x => new NotificationItemDto($"price:{x.Id}",
                "PriceOverrideApproval", "待审批改价",
                $"消费单 {x.OrderNo}，与标准金额相差 {FormatDifference(x.DifferenceMinor)}",
                "warning", "/cashier", x.RequestedAtUtc)));

            var refunds = await db.Refunds.AsNoTracking().Where(x => x.TenantId == tenantId &&
                    x.StoreId == storeId && x.Status == RefundStatus.PendingApproval)
                .OrderByDescending(x => x.RequestedAtUtc).Take(20)
                .Select(x => new { x.Id, x.RefundNo, x.AmountMinor, x.RequestedAtUtc })
                .ToListAsync(cancellationToken);
            items.AddRange(refunds.Select(x => new NotificationItemDto($"refund:{x.Id}",
                "RefundApproval", "待审批退款", $"退款单 {x.RefundNo}，金额 ¥{x.AmountMinor / 100m:F2}",
                "error", "/cashier", x.RequestedAtUtc)));
        }

        if (reviewer)
        {
            var shifts = await db.CashierShifts.AsNoTracking().Where(x => x.TenantId == tenantId &&
                    x.StoreId == storeId && x.Status == CashierShiftStatus.ReviewPending &&
                    x.OperatorId != userId)
                .OrderByDescending(x => x.SubmittedAtUtc).Take(20)
                .Select(x => new { x.Id, x.ShiftNo, x.CashDifferenceMinor, x.PendingReconciliationMinor,
                    x.SubmittedAtUtc }).ToListAsync(cancellationToken);
            items.AddRange(shifts.Select(x => new NotificationItemDto($"shift:{x.Id}", "ShiftReview",
                "待复核交班", $"班次 {x.ShiftNo}，现金差额 ¥{(x.CashDifferenceMinor ?? 0) / 100m:F2}，" +
                $"待核对 ¥{(x.PendingReconciliationMinor ?? 0) / 100m:F2}", "info", "/cashier",
                x.SubmittedAtUtc ?? DateTimeOffset.MinValue)));
        }

        var now = DateTimeOffset.UtcNow;
        var followUpRecords = await db.ServiceRecords.AsNoTracking()
            .Where(record => record.TenantId == tenantId && record.StoreId == storeId &&
                (record.FollowUpAtUtc.HasValue || db.ServiceRecordCorrections.Any(correction =>
                    correction.TenantId == tenantId && correction.ServiceRecordId == record.Id)))
            .Select(record => new { record.Id, record.CustomerId, record.ServiceOccurredAtUtc,
                record.FollowUpAtUtc }).ToListAsync(cancellationToken);
        if (followUpRecords.Count > 0)
        {
            var recordIds = followUpRecords.Select(x => x.Id).ToArray();
            var corrections = await db.ServiceRecordCorrections.AsNoTracking()
                .Where(x => x.TenantId == tenantId && recordIds.Contains(x.ServiceRecordId))
                .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
                .Select(x => new { x.ServiceRecordId, x.FollowUpAtUtc }).ToListAsync(cancellationToken);
            var latest = corrections.GroupBy(x => x.ServiceRecordId).ToDictionary(x => x.Key, x => x.Last());
            var due = followUpRecords.Select(record => new
                {
                    record.Id,
                    record.CustomerId,
                    FollowUpAtUtc = latest.TryGetValue(record.Id, out var correction)
                        ? correction.FollowUpAtUtc : record.FollowUpAtUtc,
                })
                .Where(x => x.FollowUpAtUtc.HasValue && x.FollowUpAtUtc.Value >= now.AddDays(-30) &&
                    x.FollowUpAtUtc.Value <= now.AddHours(24)).ToList();
            if (due.Count > 0)
            {
                var customerIds = due.Select(x => x.CustomerId).Distinct().ToArray();
                var customerNames = await db.Customers.AsNoTracking().Where(x => x.TenantId == tenantId &&
                        customerIds.Contains(x.Id) && x.Status == CustomerStatus.Active)
                    .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
                var latestServices = await db.ServiceRecords.AsNoTracking().Where(x => x.TenantId == tenantId &&
                        customerIds.Contains(x.CustomerId)).GroupBy(x => x.CustomerId)
                    .Select(group => new { CustomerId = group.Key,
                        LatestAtUtc = group.Max(x => x.ServiceOccurredAtUtc) })
                    .ToDictionaryAsync(x => x.CustomerId, x => x.LatestAtUtc, cancellationToken);
                items.AddRange(due.Where(x => customerNames.ContainsKey(x.CustomerId) &&
                        (!latestServices.TryGetValue(x.CustomerId, out var latestService) ||
                         latestService < x.FollowUpAtUtc!.Value))
                    .Select(x => new NotificationItemDto($"care-follow-up:{x.Id}", "CareFollowUp",
                        "顾客护理提醒", $"{customerNames[x.CustomerId]} · 计划回访时间 " +
                        x.FollowUpAtUtc!.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm",
                            CultureInfo.InvariantCulture),
                        x.FollowUpAtUtc.Value <= now ? "error" : "warning",
                        $"/customers?customerId={x.CustomerId:D}&section=care",
                        x.FollowUpAtUtc.Value)));
            }
        }

        var ordered = items.OrderByDescending(x => x.OccurredAtUtc).Take(50).ToList();
        return new NotificationInboxDto(ordered.Count, ordered);
    }

    private static string FormatDifference(long differenceMinor) => differenceMinor switch
    {
        > 0 => $"增加 ¥{differenceMinor / 100m:F2}",
        < 0 => $"优惠 ¥{-differenceMinor / 100m:F2}",
        _ => "¥0.00",
    };
}
