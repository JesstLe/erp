using Erp.Domain.Catalog;
using Erp.Domain.Cashier;
using Erp.Domain.Common;
using Erp.Domain.Organization;

namespace Erp.Domain.Tests.Organization;

public sealed class PositionCommissionTests
{
    [Theory]
    [InlineData(null, null, CommissionMode.FixedAmount, null, 2500L)]
    [InlineData(2000, null, CommissionMode.Percentage, 2000, null)]
    [InlineData(2000, 1550, CommissionMode.Percentage, 1550, null)]
    [InlineData(2000, 0, CommissionMode.None, null, null)]
    [InlineData(0, null, CommissionMode.None, null, null)]
    public void PositionAndServiceOverridePrecedence(int? defaultRate, int? projectRate,
        CommissionMode expectedMode, int? expectedRate, long? expectedFixed)
    {
        var tenantId = Guid.NewGuid();
        var item = new ServiceItem(tenantId, "SV000001", "测试项目", 30);
        item.ConfigureCommission(CommissionMode.FixedAmount, null, 2500);
        var position = new EmployeePosition(tenantId, "POS000001", "初级老师");
        position.ConfigureCommission(defaultRate);
        var rule = projectRate.HasValue
            ? new PositionServiceCommission(tenantId, position.Id, item.Id, projectRate.Value) : null;
        var resolved = ResolvedServiceCommission.Resolve(item, position, rule);
        Assert.Equal(expectedMode, resolved.Mode);
        Assert.Equal(expectedRate, resolved.RateBasisPoints);
        Assert.Equal(expectedFixed, resolved.FixedMinor);
    }

    [Fact]
    public void OverrideUsesFinalPriceAndSnapshotSurvivesPositionChanges()
    {
        var tenantId = Guid.NewGuid();
        var item = new ServiceItem(tenantId, "SV000001", "测试项目", 30);
        var position = new EmployeePosition(tenantId, "POS000001", "初级老师");
        position.ConfigureCommission(1550);
        var rule = ResolvedServiceCommission.Resolve(item, position, null);
        var order = new ServiceOrder(tenantId, Guid.NewGuid(), Guid.NewGuid(), null, "SO-001",
            Guid.NewGuid(), null, [new ServiceOrderLineDraft(item.Id, item.Code, item.Name, 2, 600,
                10_000, 8000, "现场改价", Guid.NewGuid(), "EMP000001", "服务老师", rule.Mode,
                rule.RateBasisPoints, rule.FixedMinor, commissionPositionCode: rule.PositionCode,
                commissionPositionName: rule.PositionName, commissionRuleSource: rule.Source)]);
        Assert.Equal(2480L, order.Lines.Single().CommissionAmountMinor);
        position.ConfigureCommission(5000);
        position.Update("高级老师", 0);
        Assert.Equal(2480L, order.Lines.Single().CommissionAmountMinor);
        Assert.Equal(1550, order.Lines.Single().CommissionRateBasisPoints);
        Assert.Equal("初级老师", order.Lines.Single().CommissionPositionNameSnapshot);
        Assert.Equal("PositionDefault", order.Lines.Single().CommissionRuleSourceSnapshot);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10001)]
    public void RatesRejectOutOfRangeValues(int rate)
    {
        var position = new EmployeePosition(Guid.NewGuid(), "POS000001", "自定义岗位");
        Assert.Throws<DomainRuleException>(() => position.ConfigureCommission(rate));
        Assert.Throws<DomainRuleException>(() => new PositionServiceCommission(position.TenantId,
            position.Id, Guid.NewGuid(), rate));
    }

    [Fact]
    public void ResolverRejectsOtherTenantAndMismatchedProject()
    {
        var tenantId = Guid.NewGuid();
        var item = new ServiceItem(tenantId, "SV000001", "测试项目", 30);
        var position = new EmployeePosition(tenantId, "POS000001", "自定义岗位");
        var other = new EmployeePosition(Guid.NewGuid(), "POS000001", "其他品牌岗位");
        Assert.Throws<DomainRuleException>(() => ResolvedServiceCommission.Resolve(item, other, null));
        Assert.Throws<DomainRuleException>(() => ResolvedServiceCommission.Resolve(item, position,
            new PositionServiceCommission(tenantId, position.Id, Guid.NewGuid(), 1000)));
    }
}
