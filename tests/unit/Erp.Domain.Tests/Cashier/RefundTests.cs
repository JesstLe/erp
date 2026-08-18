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

    private static readonly DateTimeOffset Now = new(2026, 8, 18, 9, 0, 0, TimeSpan.Zero);
}
