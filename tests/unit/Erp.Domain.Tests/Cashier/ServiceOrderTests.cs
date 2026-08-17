using Erp.Domain.Cashier;
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

    private static ServiceOrder CreateOrder(IEnumerable<ServiceOrderLineDraft> lines) => new(Guid.CreateVersion7(),
        Guid.CreateVersion7(), Guid.CreateVersion7(), null, "SO202608180001", Guid.CreateVersion7(), null, lines);
}
