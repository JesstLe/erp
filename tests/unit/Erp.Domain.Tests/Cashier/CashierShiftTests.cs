using Erp.Domain.Cashier;
using Erp.Domain.Common;

namespace Erp.Domain.Tests.Cashier;

public sealed class CashierShiftTests
{
    [Fact]
    public void SubmitSnapshotsExpectedCashAndDifference()
    {
        var shift = CreateShift(5_000);

        shift.Submit(10_000, 2_000, 14_500, "交班清点", Now.AddHours(8));

        Assert.Equal(CashierShiftStatus.ReviewPending, shift.Status);
        Assert.Equal(15_000, shift.ExpectedCashMinor);
        Assert.Equal(-500, shift.CashDifferenceMinor);
        Assert.Equal(2_000, shift.PendingReconciliationMinor);
    }

    [Fact]
    public void ReviewMustBeIndependent()
    {
        var shift = CreateShift(0);
        shift.Submit(0, 0, 0, null, Now.AddHours(8));

        Assert.Throws<DomainRuleException>(() => shift.Review(shift.OperatorId, null, Now.AddHours(9)));
    }

    [Fact]
    public void DifferenceOrPendingExternalRequiresReviewReason()
    {
        var shift = CreateShift(0);
        shift.Submit(10_000, 1_000, 9_900, null, Now.AddHours(8));

        Assert.Throws<DomainRuleException>(() => shift.Review(Guid.CreateVersion7(), null, Now.AddHours(9)));
        shift.Review(Guid.CreateVersion7(), "已核对现金差额和外部流水", Now.AddHours(9));
        Assert.Equal(CashierShiftStatus.Closed, shift.Status);
    }

    [Fact]
    public void CashRefundReducesExpectedCashButCannotExceedOpeningCashAndReceipts()
    {
        var shift = CreateShift(5_000);

        shift.Submit(-2_000, 0, 3_000, "当班现金退款", Now.AddHours(8));

        Assert.Equal(3_000, shift.ExpectedCashMinor);
        Assert.Throws<DomainRuleException>(() => CreateShift(5_000)
            .Submit(-5_001, 0, 0, null, Now.AddHours(8)));
    }

    private static readonly DateTimeOffset Now = new(2026, 8, 18, 0, 0, 0, TimeSpan.Zero);
    private static CashierShift CreateShift(long openingCash) => new(Guid.CreateVersion7(), Guid.CreateVersion7(),
        Guid.CreateVersion7(), "SH202608180001", openingCash, Now);
}
