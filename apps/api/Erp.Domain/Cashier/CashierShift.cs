using Erp.Domain.Common;

namespace Erp.Domain.Cashier;

public enum CashierShiftStatus { Open, ReviewPending, Closed }

public sealed class CashierShift : Entity
{
    private CashierShift() { }

    public CashierShift(Guid tenantId, Guid storeId, Guid operatorId, string shiftNo, long openingCashMinor,
        DateTimeOffset now) : base(tenantId)
    {
        if (openingCashMinor is < 0 or > 10_000_000_000)
            throw new DomainRuleException("VALIDATION_FAILED", "备用金超出允许范围");
        StoreId = storeId;
        OperatorId = operatorId;
        ShiftNo = shiftNo.Trim();
        if (ShiftNo.Length is 0 or > 40) throw new DomainRuleException("VALIDATION_FAILED", "班次号无效");
        OpeningCashMinor = openingCashMinor;
        OpenedAtUtc = now;
        Status = CashierShiftStatus.Open;
    }

    public Guid StoreId { get; private set; }
    public Guid OperatorId { get; private set; }
    public string ShiftNo { get; private set; } = string.Empty;
    public CashierShiftStatus Status { get; private set; }
    public long OpeningCashMinor { get; private set; }
    public long? ExpectedCashMinor { get; private set; }
    public long? SubmittedCashMinor { get; private set; }
    public long? CashDifferenceMinor { get; private set; }
    public long? PendingReconciliationMinor { get; private set; }
    public string? HandoverNote { get; private set; }
    public DateTimeOffset OpenedAtUtc { get; private set; }
    public DateTimeOffset? SubmittedAtUtc { get; private set; }
    public Guid? ReviewedBy { get; private set; }
    public string? ReviewReason { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }

    public void Submit(long netCashMovementMinor, long pendingReconciliationMinor, long submittedCashMinor,
        string? note, DateTimeOffset now)
    {
        if (Status != CashierShiftStatus.Open) throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "当前班次不能提交交班");
        if (netCashMovementMinor < -OpeningCashMinor || pendingReconciliationMinor < 0 || submittedCashMinor < 0)
            throw new DomainRuleException("VALIDATION_FAILED", "交班金额不能为负数");
        ExpectedCashMinor = checked(OpeningCashMinor + netCashMovementMinor);
        SubmittedCashMinor = submittedCashMinor;
        CashDifferenceMinor = submittedCashMinor - ExpectedCashMinor;
        PendingReconciliationMinor = pendingReconciliationMinor;
        HandoverNote = Normalize(note, 500, "交班备注");
        SubmittedAtUtc = now;
        if (CashDifferenceMinor == 0 && PendingReconciliationMinor == 0)
        {
            ClosedAtUtc = now;
            Status = CashierShiftStatus.Closed;
        }
        else
        {
            Status = CashierShiftStatus.ReviewPending;
        }
        Touch();
    }

    public void Review(Guid reviewerId, string? reason, DateTimeOffset now, bool isOwner = false)
    {
        if (Status != CashierShiftStatus.ReviewPending) throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "当前班次不在待复核状态");
        if (reviewerId == OperatorId && !isOwner)
            throw new DomainRuleException("FORBIDDEN_ACTION", "非最高权限账号不能复核本人提交的交班");
        var normalizedReason = Normalize(reason, 500, "复核说明");
        if ((CashDifferenceMinor != 0 || PendingReconciliationMinor > 0) && normalizedReason is null)
            throw new DomainRuleException("VALIDATION_FAILED", "存在差额或待核对外部收款时必须填写复核说明");
        ReviewedBy = reviewerId;
        ReviewReason = normalizedReason;
        ClosedAtUtc = now;
        Status = CashierShiftStatus.Closed;
        Touch();
    }

    private static string? Normalize(string? value, int max, string field)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > max) throw new DomainRuleException("VALIDATION_FAILED", $"{field}最多{max}字");
        return normalized;
    }
}
