using Erp.Domain.Cashier;
using Erp.Domain.Common;

namespace Erp.Domain.Tests.Cashier;

public sealed class RefundTests
{
    [Fact]
    public void MemberRefundCompletesAgainstOriginalAccountAndCannotCompleteTwice()
    {
        var accountId = Guid.CreateVersion7();
        var refund = new Refund(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            "RF202608180001", "顾客确认退回未消费服务", Guid.CreateVersion7(),
            [new(Guid.CreateVersion7(), 5_000, PaymentMethodCategory.InternalAccount, accountId)], Now);

        refund.Complete(Guid.CreateVersion7(), null, Now.AddMinutes(1));

        Assert.Equal(RefundStatus.Completed, refund.Status);
        Assert.Equal(RefundRoute.OriginalMemberAccount, refund.Lines.Single().Route);
        Assert.Equal(accountId, refund.Lines.Single().MemberAccountId);
        Assert.Throws<DomainRuleException>(() => refund.Complete(Guid.CreateVersion7(), null, Now));
    }

    [Fact]
    public void CashRefundNeedsApproverShiftAndManualExternalCannotPretendOriginalRoute()
    {
        var cashRefund = new Refund(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            "RF202608180002", "现金原路退回", Guid.CreateVersion7(),
            [new(Guid.CreateVersion7(), 2_000, PaymentMethodCategory.Cash, null)], Now);

        Assert.Throws<DomainRuleException>(() => cashRefund.Complete(Guid.CreateVersion7(), null, Now));
        Assert.Throws<DomainRuleException>(() => new Refund(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), "RF202608180003", "人工渠道退款", Guid.CreateVersion7(),
            [new(Guid.CreateVersion7(), 2_000, PaymentMethodCategory.ManualExternal, null)], Now));
    }

    [Fact]
    public void RejectionReleasesRequestWithoutCompletingAnyLine()
    {
        var refund = new Refund(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            "RF202608180004", "退款申请", Guid.CreateVersion7(),
            [new(Guid.CreateVersion7(), 2_000, PaymentMethodCategory.Cash, null)], Now);

        refund.Reject(Guid.CreateVersion7(), "申请资料不完整");

        Assert.Equal(RefundStatus.Rejected, refund.Status);
        Assert.Null(refund.Lines.Single().CompletedAtUtc);
    }

    [Fact]
    public void ChannelRefundWaitsForProviderSuccessBeforeCompletingLocalRefund()
    {
        var approverId = Guid.CreateVersion7();
        var refund = new Refund(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            "RF202608180005", "微信原路退款", Guid.CreateVersion7(),
            [new(Guid.CreateVersion7(), 8_800, PaymentMethodCategory.ChannelExternal, null)], Now);

        Assert.Equal(RefundRoute.OriginalChannel, refund.Lines.Single().Route);
        Assert.Throws<DomainRuleException>(() => refund.Complete(approverId, null, Now.AddMinutes(1)));

        refund.BeginChannelProcessing(approverId);

        Assert.Equal(RefundStatus.Processing, refund.Status);
        Assert.Equal(approverId, refund.ApprovedBy);
        Assert.Null(refund.CompletedAtUtc);
        Assert.Null(refund.Lines.Single().CompletedAtUtc);

        refund.CompleteChannel(Now.AddMinutes(2));

        Assert.Equal(RefundStatus.Completed, refund.Status);
        Assert.Equal(Now.AddMinutes(2), refund.CompletedAtUtc);
        Assert.Equal(Now.AddMinutes(2), refund.Lines.Single().CompletedAtUtc);
    }

    [Fact]
    public void ChannelRefundKeepsStableMerchantNumberAcrossFailureRetryAndSuccess()
    {
        var channelRefund = new PaymentChannelRefund(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), PaymentChannelProvider.Alipay,
            "RF202608180006", "PAY202608180001-A1", "2026081822001000000001", 12_300);

        channelRefund.MarkProcessing("ALI-REFUND-1");
        channelRefund.MarkFailed("ALIPAY_SYSTEM_ERROR");
        channelRefund.MarkProcessing("ALI-REFUND-1");
        channelRefund.RecordQuery(Now.AddMinutes(1));
        channelRefund.MarkSucceeded("ALI-REFUND-1", Now.AddMinutes(2));

        Assert.Equal("RF202608180006", channelRefund.OutRefundNo);
        Assert.Equal(PaymentChannelRefundStatus.Succeeded, channelRefund.Status);
        Assert.Equal("ALI-REFUND-1", channelRefund.ProviderRefundNo);
        Assert.Null(channelRefund.FailureCode);
        Assert.Equal(Now.AddMinutes(1), channelRefund.LastQueriedAtUtc);
        Assert.Equal(Now.AddMinutes(2), channelRefund.SucceededAtUtc);
        Assert.Throws<DomainRuleException>(() => channelRefund.MarkFailed("ALIPAY_LATE_FAILURE"));
    }

    private static readonly DateTimeOffset Now = new(2026, 8, 18, 9, 0, 0, TimeSpan.Zero);
}
