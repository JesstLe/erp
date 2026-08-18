using Erp.Domain.Cashier;
using Erp.Domain.Catalog;
using Erp.Domain.Common;

namespace Erp.Domain.Tests.Cashier;

public sealed class ServiceOrderTests
{
    [Fact]
    public void TotalsUseEnteredPriceAndKeepReferenceSnapshot()
    {
        var order = CreateOrder([new(Guid.CreateVersion7(), "S01", "标准服务", 2, 3600, 10_000, 8_000, "现场服务内容调整")]);

        Assert.Equal(20_000, order.ReferenceAmountMinor);
        Assert.Equal(16_000, order.ReceivableMinor);
        Assert.Equal(3600, order.Lines.Single().ActualSeconds);
    }

    [Fact]
    public void ActualDurationDoesNotChangeAmount()
    {
        var shortOrder = CreateOrder([new(Guid.CreateVersion7(), "S01", "标准服务", 1, 600, 10_000, 10_000, null)]);
        var longOrder = CreateOrder([new(Guid.CreateVersion7(), "S01", "标准服务", 1, 7200, 10_000, 10_000, null)]);

        Assert.Equal(shortOrder.ReceivableMinor, longOrder.ReceivableMinor);
    }

    [Fact]
    public void PriceOverrideRequiresReason()
    {
        Assert.Throws<DomainRuleException>(() => CreateOrder(
            [new(Guid.CreateVersion7(), "S01", "标准服务", 1, null, 10_000, 9_000, null)]));
    }

    [Fact]
    public void EmptyOrderIsRejected()
    {
        Assert.Throws<DomainRuleException>(() => CreateOrder([]));
    }

    [Fact]
    public void ConfirmMovesDraftToPendingPaymentOnlyOnce()
    {
        var order = CreateOrder([new(Guid.CreateVersion7(), "S01", "标准服务", 1, null, 10_000, 10_000, null)]);
        var now = new DateTimeOffset(2026, 8, 18, 6, 0, 0, TimeSpan.Zero);

        order.Confirm(now);

        Assert.Equal(ServiceOrderStatus.PendingPayment, order.Status);
        Assert.Equal(now, order.ConfirmedAtUtc);
        Assert.Throws<DomainRuleException>(() => order.Confirm(now.AddMinutes(1)));
    }

    [Fact]
    public void CheckoutMustPassThroughProcessingBeforeSettlement()
    {
        var order = CreateOrder([new(Guid.CreateVersion7(), "S01", "标准服务", 1, null, 10_000, 10_000, null)]);
        var now = new DateTimeOffset(2026, 8, 18, 6, 0, 0, TimeSpan.Zero);
        order.Confirm(now);

        Assert.Throws<DomainRuleException>(() => order.Settle(now));
        order.BeginCheckout();
        Assert.Equal(ServiceOrderStatus.PaymentProcessing, order.Status);
        order.Settle(now);
        Assert.Equal(ServiceOrderStatus.Settled, order.Status);
    }

    [Fact]
    public void RefundTotalsAdvanceSettledOrderAndRejectExcess()
    {
        var order = CreateOrder([new(Guid.CreateVersion7(), "S01", "标准服务", 1, null, 10_000, 10_000, null)]);
        order.Confirm(DateTimeOffset.UtcNow);
        order.BeginCheckout();
        order.Settle(DateTimeOffset.UtcNow);

        order.ApplyRefund(4_000);
        Assert.Equal(ServiceOrderStatus.PartiallyRefunded, order.Status);
        order.ApplyRefund(6_000);
        Assert.Equal(ServiceOrderStatus.Refunded, order.Status);
        Assert.Equal(10_000, order.RefundedMinor);
        Assert.Throws<DomainRuleException>(() => order.ApplyRefund(1));
    }

    [Fact]
    public void ProductLineKeepsUnitAndSupportsBoundedReturns()
    {
        var productId = Guid.CreateVersion7();
        var order = CreateOrder([ServiceOrderLineDraft.Product(productId, "P01", "护理液", "瓶",
            2, 3_000, 3_000, null)]);
        var line = order.Lines.Single();

        Assert.Equal(ServiceOrderLineType.Product, line.LineType);
        Assert.Equal(productId, line.ProductItemId);
        Assert.Null(line.ServiceItemId);
        Assert.Equal("瓶", line.UnitNameSnapshot);
        Assert.Null(line.ActualSeconds);

        line.ApplyProductReturn(1);
        Assert.Equal(1, line.ReturnedQuantity);
        Assert.Throws<DomainRuleException>(() => line.ApplyProductReturn(2));
    }

    [Fact]
    public void PendingOrderCanBeVoidedButSettledOrderCannot()
    {
        var pending = CreateOrder([new(Guid.CreateVersion7(), "S01", "标准服务", 1, null, 10_000,
            10_000, null)]);
        pending.Confirm(DateTimeOffset.UtcNow);
        pending.Void();
        Assert.Equal(ServiceOrderStatus.Voided, pending.Status);

        var settled = CreateOrder([new(Guid.CreateVersion7(), "S02", "标准服务", 1, null, 10_000,
            10_000, null)]);
        settled.Confirm(DateTimeOffset.UtcNow);
        settled.BeginCheckout();
        settled.Settle(DateTimeOffset.UtcNow);
        Assert.Throws<DomainRuleException>(() => settled.Void());
    }

    [Fact]
    public void PercentageCommissionUsesEnteredLineAmountAndKeepsEmployeeSnapshot()
    {
        var employeeId = Guid.CreateVersion7();
        var order = CreateOrder([new ServiceOrderLineDraft(Guid.CreateVersion7(), "S01", "标准服务", 2,
            3600, 10_000, 8_000, "现场折扣", employeeId, "E001", "王技师",
            CommissionMode.Percentage, 1_250)]);
        var line = order.Lines.Single();

        Assert.Equal(employeeId, line.ServiceEmployeeId);
        Assert.Equal("王技师", line.EmployeeNameSnapshot);
        Assert.Equal(16_000, line.CommissionBasisMinor);
        Assert.Equal(2_000, line.CommissionAmountMinor);
    }

    [Fact]
    public void FixedCommissionIsPerServiceUnitAndCannotExceedLineAmount()
    {
        var employeeId = Guid.CreateVersion7();
        var order = CreateOrder([new ServiceOrderLineDraft(Guid.CreateVersion7(), "S01", "标准服务", 2,
            null, 10_000, 10_000, null, employeeId, "E001", "王技师",
            CommissionMode.FixedAmount, null, 3_000)]);

        Assert.Equal(6_000, order.Lines.Single().CommissionAmountMinor);
        var error = Assert.Throws<DomainRuleException>(() => CreateOrder([new ServiceOrderLineDraft(
            Guid.CreateVersion7(), "S02", "低价服务", 1, null, 1_000, 1_000, null, employeeId, "E001",
            "王技师", CommissionMode.FixedAmount, null, 2_000)]));
        Assert.Equal("COMMISSION_EXCEEDS_LINE_AMOUNT", error.Code);
    }

    [Fact]
    public void ConfiguredCommissionRequiresServiceEmployee()
    {
        var error = Assert.Throws<DomainRuleException>(() => CreateOrder([new ServiceOrderLineDraft(
            Guid.CreateVersion7(), "S01", "标准服务", 1, null, 10_000, 10_000, null,
            commissionMode: CommissionMode.Percentage, commissionRateBasisPoints: 1_000)]));

        Assert.Equal("SERVICE_EMPLOYEE_REQUIRED", error.Code);
    }

    private static ServiceOrder CreateOrder(IEnumerable<ServiceOrderLineDraft> lines) => new(Guid.CreateVersion7(),
        Guid.CreateVersion7(), Guid.CreateVersion7(), null, "SO202608180001", Guid.CreateVersion7(), null, lines);
}
