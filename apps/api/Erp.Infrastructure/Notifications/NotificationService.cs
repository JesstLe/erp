using Erp.Application.Notifications;
using Erp.Application.Security;
using Erp.Domain.Cashier;
using Erp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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
