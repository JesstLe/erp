using Erp.Domain.Cashier;
using Erp.Domain.Common;

namespace Erp.Domain.Tests.Cashier;

public sealed class PaymentChannelReconciliationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RunCompletesWithImmutableDigestAndDifferenceCounts()
    {
        var run = new PaymentChannelReconciliationRun(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), PaymentChannelProvider.WeChatPay, new DateOnly(2026, 8, 17), 1,
            Guid.CreateVersion7(), Now);
        var digest = Enumerable.Range(0, 32).Select(x => (byte)x).ToArray();

        run.Complete(3, 2, 1, digest, Now.AddMinutes(1));
        digest[0] = 255;

        Assert.Equal(PaymentChannelReconciliationRunStatus.Differences, run.Status);
        Assert.Equal((3, 2, 1), (run.ChannelEntryCount, run.MatchedCount, run.DifferenceCount));
        Assert.NotEqual(255, run.SourceSha256![0]);
        Assert.Throws<DomainRuleException>(() => run.Fail("LATE_FAILURE", Now.AddMinutes(2)));
    }

    [Fact]
    public void DifferenceRequiresReasonAndCannotChangeMatchedItem()
    {
        var difference = NewItem(PaymentChannelReconciliationItemStatus.AmountMismatch);
        Assert.Throws<DomainRuleException>(() => difference.Resolve(Guid.CreateVersion7(), " ", Now));

        difference.Resolve(Guid.CreateVersion7(), "已核对渠道流水，登记人工差异单", Now);

        Assert.Equal(PaymentChannelReconciliationItemStatus.Resolved, difference.Status);
        Assert.NotNull(difference.ResolvedBy);
        var matched = NewItem(PaymentChannelReconciliationItemStatus.Matched);
        Assert.Throws<DomainRuleException>(() => matched.Resolve(Guid.CreateVersion7(), "无需处理", Now));
    }

    [Fact]
    public void ItemTypeRequiresCorrespondingMerchantNumber()
    {
        Assert.Throws<DomainRuleException>(() => new PaymentChannelReconciliationItem(Guid.CreateVersion7(),
            Guid.CreateVersion7(), PaymentChannelReconciliationItemType.Refund,
            PaymentChannelReconciliationItemStatus.ChannelOnly, "REFUND:RF-1", "PAY-1", null,
            null, null, null, null, 1_000, 0, null, "SUCCESS"));
    }

    private static PaymentChannelReconciliationItem NewItem(PaymentChannelReconciliationItemStatus status) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), PaymentChannelReconciliationItemType.Payment,
            status, "PAY:PAY-1", "PAY-1", null, "WX-1", Guid.CreateVersion7(), null, 10_000,
            9_000, 0, "Paid/ChannelConfirmed", "SUCCESS");
}
