using Erp.Domain.Cashier;
using Erp.Domain.Common;

namespace Erp.Domain.Tests.Cashier;

public sealed class PaymentTests
{
    [Fact]
    public void BalancedCashPaymentIsRecordedAndPaid()
    {
        var shiftId = Guid.CreateVersion7();
        var payment = CreatePayment(10_000,
            [new(Guid.CreateVersion7(), "CASH", "现金", PaymentMethodCategory.Cash, 10_000, null, shiftId)]);

        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.Equal(10_000, payment.PaidMinor);
        Assert.Equal(PaymentConfirmationStatus.CashRecorded, payment.Allocations.Single().ConfirmationStatus);
        Assert.Equal(ReconciliationStatus.NotRequired, payment.Allocations.Single().ReconciliationStatus);
    }

    [Fact]
    public void UnbalancedAllocationsAreRejected()
    {
        Assert.Throws<DomainRuleException>(() => CreatePayment(10_000,
            [new(Guid.CreateVersion7(), "CASH", "现金", PaymentMethodCategory.Cash, 9_000, null, Guid.CreateVersion7())]));
    }

    [Fact]
    public void ManualExternalRequiresReferenceAndStaysPendingReconciliation()
    {
        Assert.Throws<DomainRuleException>(() => CreatePayment(10_000,
            [new(Guid.CreateVersion7(), "WECHAT_MANUAL", "微信人工登记", PaymentMethodCategory.ManualExternal,
                10_000, null, Guid.CreateVersion7())]));

        var payment = CreatePayment(10_000,
            [new(Guid.CreateVersion7(), "WECHAT_MANUAL", "微信人工登记", PaymentMethodCategory.ManualExternal,
                10_000, "WX-TEST-0001", Guid.CreateVersion7())]);

        var allocation = payment.Allocations.Single();
        Assert.Equal(PaymentConfirmationStatus.ManualPendingReconciliation, allocation.ConfirmationStatus);
        Assert.Equal(ReconciliationStatus.Pending, allocation.ReconciliationStatus);
    }

    [Fact]
    public void CashAndManualExternalRequireOpenShiftReference()
    {
        Assert.Throws<DomainRuleException>(() => CreatePayment(10_000,
            [new(Guid.CreateVersion7(), "CASH", "现金", PaymentMethodCategory.Cash, 10_000, null, null)]));
    }

    private static Payment CreatePayment(long receivable, IEnumerable<PaymentAllocationDraft> allocations) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "PAY202608180001", receivable,
            allocations, new DateTimeOffset(2026, 8, 18, 8, 0, 0, TimeSpan.Zero));
}
