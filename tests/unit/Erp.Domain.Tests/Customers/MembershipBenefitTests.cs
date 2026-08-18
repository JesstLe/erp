using Erp.Domain.Common;
using Erp.Domain.Customers;

namespace Erp.Domain.Tests.Customers;

public sealed class MembershipBenefitTests
{
    [Fact]
    public void ServicePassRedeemsPurchasedUsesBeforeBonusAndCanReverse()
    {
        var pass = new ServicePass(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), "护理十次卡", 2, 1, new DateOnly(2026, 8, 1),
            new DateOnly(2026, 12, 31), "购卡发放");
        var operatorId = Guid.NewGuid();
        var redeemed = pass.Redeem(2, null, "本次护理核销", Guid.NewGuid(), operatorId,
            new DateOnly(2026, 8, 18), DateTimeOffset.UtcNow);

        Assert.Equal(-2, redeemed.PurchasedUsesDelta);
        Assert.Equal(0, redeemed.BonusUsesDelta);
        Assert.Equal(1, pass.RemainingUses);
        Assert.Equal(ServicePassStatus.Active, pass.Status);

        var reversed = pass.Reverse(redeemed, "误操作撤销", Guid.NewGuid(), operatorId,
            new DateOnly(2026, 8, 18), DateTimeOffset.UtcNow);
        Assert.Equal(2, reversed.PurchasedUsesDelta);
        Assert.Equal(3, pass.RemainingUses);
    }

    [Fact]
    public void ServicePassCannotRedeemAfterExpiry()
    {
        var pass = new ServicePass(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), "护理卡", 1, 0, new DateOnly(2026, 1, 1),
            new DateOnly(2026, 8, 17), "购卡发放");

        var error = Assert.Throws<DomainRuleException>(() => pass.Redeem(1, null, "核销",
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 8, 18), DateTimeOffset.UtcNow));
        Assert.Equal("SERVICE_PASS_NOT_ACTIVE", error.Code);
    }

    [Fact]
    public void PointGrantTracksFifoConsumptionRestoreAndExpiry()
    {
        var grant = new MemberPointGrant(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), 100, new DateOnly(2026, 8, 31), "PointManualCredit", Guid.NewGuid());

        Assert.Equal(60, grant.Consume(60));
        Assert.Equal(40, grant.RemainingUnits);
        grant.Restore(20, new DateOnly(2026, 8, 18));
        Assert.Equal(60, grant.RemainingUnits);
        Assert.Equal(60, grant.Expire(new DateOnly(2026, 9, 1)));
        Assert.Equal(MemberPointGrantStatus.Expired, grant.Status);
        Assert.Equal(0, grant.RemainingUnits);
    }
}
