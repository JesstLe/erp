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

    [Fact]
    public void MemberTopupPaymentCarriesBusinessSourceWithoutServiceOrderLink()
    {
        var businessId = Guid.CreateVersion7();
        var payment = new Payment(Guid.CreateVersion7(), Guid.CreateVersion7(),
            PaymentBusinessType.MemberTopup, businessId, "PAY202608180002", 10_000,
            [new(Guid.CreateVersion7(), "CASH", "现金", PaymentMethodCategory.Cash, 10_000, null,
                Guid.CreateVersion7())], new DateTimeOffset(2026, 8, 18, 8, 0, 0, TimeSpan.Zero));

        Assert.Equal(PaymentBusinessType.MemberTopup, payment.BusinessType);
        Assert.Equal(businessId, payment.BusinessId);
        Assert.Null(payment.OrderId);
    }

    [Fact]
    public void InternalAccountPaymentRequiresMemberAccountReference()
    {
        Assert.Throws<DomainRuleException>(() => CreatePayment(8_000,
            [new(Guid.CreateVersion7(), "MEMBER_PRINCIPAL", "会员储值本金",
                PaymentMethodCategory.InternalAccount, 8_000, null, null)]));

        var accountId = Guid.CreateVersion7();
        var payment = CreatePayment(8_000,
            [new(Guid.CreateVersion7(), "MEMBER_PRINCIPAL", "会员储值本金",
                PaymentMethodCategory.InternalAccount, 8_000, null, null, accountId)]);

        var allocation = payment.Allocations.Single();
        Assert.Equal(accountId, allocation.MemberAccountId);
        Assert.Equal(PaymentConfirmationStatus.InternalConfirmed, allocation.ConfirmationStatus);
    }

    [Fact]
    public void RefundTotalsAdvancePaymentWithoutOverwritingOriginalPaidAmount()
    {
        var payment = CreatePayment(10_000,
            [new(Guid.CreateVersion7(), "CASH", "现金", PaymentMethodCategory.Cash, 10_000, null,
                Guid.CreateVersion7())]);

        payment.ApplyRefund(4_000);
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(4_000, payment.RefundedMinor);
        Assert.Equal(10_000, payment.PaidMinor);

        payment.ApplyRefund(6_000);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Throws<DomainRuleException>(() => payment.ApplyRefund(1));
    }

    [Fact]
    public void ChannelConfigurationStoresOnlyCredentialProfileName()
    {
        var configuration = new PaymentChannelConfiguration(Guid.CreateVersion7(), Guid.CreateVersion7(),
            PaymentChannelProvider.WeChatPay, PaymentChannelEnvironment.Production, "微信支付",
            "PRIMARY_WECHAT", false);

        Assert.Equal("PRIMARY_WECHAT", configuration.CredentialProfile);
        Assert.False(configuration.IsEnabled);
        Assert.Throws<DomainRuleException>(() => new PaymentChannelConfiguration(Guid.CreateVersion7(),
            Guid.CreateVersion7(), PaymentChannelProvider.Alipay, PaymentChannelEnvironment.Sandbox,
            "支付宝", "../../secrets/private-key", false));
    }

    [Fact]
    public void ChannelOrderTransitionsToPaidAndRejectsConflictingReplay()
    {
        var now = DateTimeOffset.UtcNow;
        var order = new PaymentChannelOrder(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            PaymentChannelProvider.Alipay, "PAY202608180001-A1", 1, 12_300, "服务消费",
            now.AddMinutes(15));

        order.MarkQrReady("https://qr.example.test/payment/one-time-token", now);
        order.MarkPaid("2026081822001000000001", now.AddMinutes(1));
        order.MarkPaid("2026081822001000000001", now.AddMinutes(1));

        Assert.Equal(PaymentChannelOrderStatus.Paid, order.Status);
        Assert.Equal("2026081822001000000001", order.ProviderTradeNo);
        Assert.Throws<DomainRuleException>(() =>
            order.MarkPaid("2026081822001000000002", now.AddMinutes(2)));
        Assert.Throws<DomainRuleException>(() => order.Close(now.AddMinutes(2)));
    }

    [Fact]
    public void VerifiedChannelEventStoresDigestInsteadOfRawPayload()
    {
        var digest = Enumerable.Repeat((byte)0x5A, 32).ToArray();
        var now = DateTimeOffset.UtcNow;
        var notification = new PaymentChannelEvent(Guid.CreateVersion7(), Guid.CreateVersion7(), null,
            PaymentChannelProvider.WeChatPay, "EVT-20260818-0001", "TRANSACTION.SUCCESS", digest, now);

        notification.Complete(PaymentChannelEventStatus.Processed, now.AddSeconds(1));

        Assert.Equal(PaymentChannelEventStatus.Processed, notification.Status);
        Assert.Equal(digest, notification.PayloadSha256);
        Assert.Throws<DomainRuleException>(() => new PaymentChannelEvent(Guid.CreateVersion7(),
            Guid.CreateVersion7(), null, PaymentChannelProvider.Alipay, "event", "trade_status_sync",
            new byte[31], now));
    }

    [Fact]
    public void ChannelPaymentStaysProcessingUntilVerifiedTradeNumberConfirmsIt()
    {
        var shiftId = Guid.CreateVersion7();
        var payment = CreatePayment(12_300,
            [new(Guid.CreateVersion7(), "WECHAT_NATIVE", "微信支付 Native",
                PaymentMethodCategory.ChannelExternal, 12_300, null, shiftId, null,
                PaymentChannelProvider.WeChatPay)]);
        var allocation = payment.Allocations.Single();

        Assert.Equal(PaymentStatus.Processing, payment.Status);
        Assert.Equal(0, payment.PaidMinor);
        Assert.Equal(PaymentConfirmationStatus.ChannelPending, allocation.ConfirmationStatus);
        Assert.Null(allocation.ConfirmedAtUtc);

        payment.ConfirmChannelAllocation(allocation.Id, "42000000000000000001", DateTimeOffset.UtcNow);

        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.Equal(12_300, payment.PaidMinor);
        Assert.Equal(PaymentConfirmationStatus.ChannelConfirmed, allocation.ConfirmationStatus);
        Assert.Equal("42000000000000000001", allocation.ExternalReference);
    }

    [Fact]
    public void PendingChannelPaymentCanCloseWithoutPretendingItWasPaid()
    {
        var payment = CreatePayment(5_000,
            [new(Guid.CreateVersion7(), "ALIPAY_QR", "支付宝订单码",
                PaymentMethodCategory.ChannelExternal, 5_000, null, Guid.CreateVersion7(), null,
                PaymentChannelProvider.Alipay)]);

        payment.CancelPendingChannelPayment();

        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
        Assert.Equal(0, payment.PaidMinor);
        Assert.Equal(PaymentConfirmationStatus.Cancelled,
            payment.Allocations.Single().ConfirmationStatus);
        payment.MarkReversalRequired();
        Assert.Equal(PaymentStatus.ReversalRequired, payment.Status);
    }

    private static Payment CreatePayment(long receivable, IEnumerable<PaymentAllocationDraft> allocations) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "PAY202608180001", receivable,
            allocations, new DateTimeOffset(2026, 8, 18, 8, 0, 0, TimeSpan.Zero));
}
