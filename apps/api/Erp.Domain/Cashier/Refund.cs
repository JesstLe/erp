using Erp.Domain.Common;

namespace Erp.Domain.Cashier;

public enum RefundStatus { PendingApproval, Completed, Rejected }
public enum RefundRoute { OriginalCash, OriginalMemberAccount }

public sealed record RefundLineDraft(Guid OriginalAllocationId, long AmountMinor,
    PaymentMethodCategory Category, Guid? MemberAccountId);

public sealed class Refund : Entity
{
    private readonly List<RefundLine> _lines = [];
    private Refund() { }

    public Refund(Guid tenantId, Guid storeId, Guid paymentId, string refundNo, string reason,
        Guid requestedBy, IEnumerable<RefundLineDraft> lines, DateTimeOffset now) : base(tenantId)
    {
        StoreId = storeId;
        PaymentId = paymentId;
        RefundNo = Required(refundNo, 40, "退款单号");
        Reason = Required(reason, 500, "退款原因");
        RequestedBy = requestedBy;
        RequestedAtUtc = now;
        foreach (var line in lines)
            _lines.Add(new RefundLine(tenantId, Id, line.OriginalAllocationId, line.AmountMinor,
                line.Category, line.MemberAccountId));
        if (_lines.Count is 0 or > 20)
            throw new DomainRuleException("VALIDATION_FAILED", "退款分摊需要1到20行");
        AmountMinor = checked(_lines.Sum(x => x.AmountMinor));
        Status = RefundStatus.PendingApproval;
    }

    public Guid StoreId { get; private set; }
    public Guid PaymentId { get; private set; }
    public string RefundNo { get; private set; } = string.Empty;
    public RefundStatus Status { get; private set; }
    public long AmountMinor { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public Guid RequestedBy { get; private set; }
    public DateTimeOffset RequestedAtUtc { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public string? RejectionReason { get; private set; }
    public IReadOnlyCollection<RefundLine> Lines => _lines;

    public void Complete(Guid approvedBy, Guid? cashShiftId, DateTimeOffset now)
    {
        if (Status != RefundStatus.PendingApproval)
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "只有待审批退款可以完成");
        foreach (var line in _lines)
            line.Complete(line.Category == PaymentMethodCategory.Cash ? cashShiftId : null, now);
        Status = RefundStatus.Completed;
        ApprovedBy = approvedBy;
        CompletedAtUtc = now;
        Touch();
    }

    public void Reject(Guid approvedBy, string reason)
    {
        if (Status != RefundStatus.PendingApproval)
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "只有待审批退款可以拒绝");
        Status = RefundStatus.Rejected;
        ApprovedBy = approvedBy;
        RejectionReason = Required(reason, 500, "拒绝原因");
        Touch();
    }

    private static string Required(string value, int max, string field)
    {
        var normalized = value.Trim();
        if (normalized.Length is 0 || normalized.Length > max)
            throw new DomainRuleException("VALIDATION_FAILED", $"{field}长度不正确");
        return normalized;
    }
}

public sealed class RefundLine : Entity
{
    private RefundLine() { }

    internal RefundLine(Guid tenantId, Guid refundId, Guid originalAllocationId, long amountMinor,
        PaymentMethodCategory category, Guid? memberAccountId) : base(tenantId)
    {
        if (amountMinor <= 0 || amountMinor > 10_000_000_000)
            throw new DomainRuleException("VALIDATION_FAILED", "退款金额必须大于0且不超过允许范围");
        if (category == PaymentMethodCategory.ManualExternal)
            throw new DomainRuleException("REFUND_ROUTE_UNAVAILABLE", "人工外部登记尚不能伪装为原路退款");
        if ((category == PaymentMethodCategory.InternalAccount) != memberAccountId.HasValue)
            throw new DomainRuleException("VALIDATION_FAILED", "会员退款必须且只能关联原会员账户");
        RefundId = refundId;
        OriginalAllocationId = originalAllocationId;
        AmountMinor = amountMinor;
        Category = category;
        MemberAccountId = memberAccountId;
        Route = category == PaymentMethodCategory.Cash
            ? RefundRoute.OriginalCash : RefundRoute.OriginalMemberAccount;
    }

    public Guid RefundId { get; private set; }
    public Guid OriginalAllocationId { get; private set; }
    public long AmountMinor { get; private set; }
    public PaymentMethodCategory Category { get; private set; }
    public Guid? MemberAccountId { get; private set; }
    public RefundRoute Route { get; private set; }
    public Guid? CashShiftId { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    internal void Complete(Guid? cashShiftId, DateTimeOffset now)
    {
        if (Category == PaymentMethodCategory.Cash && cashShiftId is null)
            throw new DomainRuleException("SHIFT_NOT_OPEN", "现金退款必须归入审批人的当前班次");
        CashShiftId = cashShiftId;
        CompletedAtUtc = now;
        Touch();
    }
}
