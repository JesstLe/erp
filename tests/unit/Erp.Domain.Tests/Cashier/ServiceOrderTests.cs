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
    public void MemberDiscountKeepsPricingSnapshotWithoutTriggeringManualApproval()
    {
        var cardTypeId = Guid.CreateVersion7();
        var order = CreateOrder([new ServiceOrderLineDraft(Guid.CreateVersion7(), "S01", "标准服务", 1,
            1800, 10_000, 8_000, "会员折扣：八折储值卡 8折", pricingSource:
            ServiceOrderLinePricingSource.MemberDiscount, memberDiscountBasisPoints: 8_000,
            memberCardTypeId: cardTypeId, memberCardTypeName: "八折储值卡")]);

        Assert.False(order.HasPriceOverride);
        Assert.Equal(0, order.ManualPriceOverrideDiscountMinor);
        Assert.Equal(2_000, order.TotalDiscountMinor);
        Assert.Equal(ServiceOrderLinePricingSource.MemberDiscount, order.Lines.Single().PricingSource);
        Assert.Equal(cardTypeId, order.Lines.Single().MemberCardTypeId);
        Assert.Equal("八折储值卡", order.Lines.Single().MemberCardTypeNameSnapshot);

        order.Confirm(DateTimeOffset.UtcNow);
        Assert.Equal(ServiceOrderStatus.PendingPayment, order.Status);
    }

    [Fact]
    public void PriceOverrideRequiresReason()
    {
        Assert.Throws<DomainRuleException>(() => CreateOrder(
            [new(Guid.CreateVersion7(), "S01", "标准服务", 1, null, 10_000, 9_000, null)]));
    }

    [Fact]
    public void PendingPriceOverrideCannotBeConfirmedUntilIndependentApprovalCompletes()
    {
        var now = new DateTimeOffset(2026, 8, 18, 6, 0, 0, TimeSpan.Zero);
        var requester = Guid.CreateVersion7();
        var approver = Guid.CreateVersion7();
        var order = CreateOrder([new(Guid.CreateVersion7(), "S01", "标准服务", 1, null,
            10_000, 8_000, "顾客活动优惠")]);
        var policy = PriceOverridePolicy.Default(order.TenantId, approver, now);
        order.RequestPriceApproval(policy.Id, policy.PolicyVersion);
        var approval = new PriceOverrideApproval(order.TenantId, order.StoreId, order.Id, requester,
            "CASHIER", policy.Id, policy.PolicyVersion, order.ReferenceAmountMinor, order.ReceivableMinor,
            order.MaximumLineDiscountBasisPoints, policy.ManagerLineDiscountBasisPoints,
            policy.ManagerOrderDiscountMinor, policy.AllowManagerPriceIncrease, now);

        var blocked = Assert.Throws<DomainRuleException>(() => order.Confirm(now));
        Assert.Equal("PRICE_APPROVAL_REQUIRED", blocked.Code);
        Assert.Throws<DomainRuleException>(() => approval.Approve(requester, null, now));

        approval.Approve(approver, "已核对优惠依据", now);
        order.ApprovePriceOverride(approver, now);
        order.Confirm(now);

        Assert.Equal(PriceOverrideApprovalStatus.Approved, approval.Status);
        Assert.Equal(PriceAuthorizationState.Approved, order.PriceAuthorizationStatus);
        Assert.Equal(ServiceOrderStatus.PendingPayment, order.Status);
    }

    [Fact]
    public void ManagerPolicyAllowsOnlyBoundedDiscountAndRequiresApprovalForIncrease()
    {
        var now = DateTimeOffset.UtcNow;
        var policy = PriceOverridePolicy.Default(Guid.CreateVersion7(), Guid.CreateVersion7(), now);
        var withinLimit = CreateOrder([new(Guid.CreateVersion7(), "S01", "标准服务", 1, null,
            50_000, 45_000, "店长权限内优惠")]);
        var overLineLimit = CreateOrder([new(Guid.CreateVersion7(), "S02", "标准服务", 1, null,
            10_000, 8_999, "超过单行十个百分点")]);
        var increase = CreateOrder([new(Guid.CreateVersion7(), "S03", "加项服务", 1, null,
            10_000, 11_000, "现场增加服务内容")]);

        Assert.False(policy.ManagerRequiresApproval(withinLimit));
        Assert.True(policy.ManagerRequiresApproval(overLineLimit));
        Assert.True(policy.ManagerRequiresApproval(increase));
        Assert.Equal(1_001, overLineLimit.MaximumLineDiscountBasisPoints);
    }

    [Fact]
    public void ManagerOrderDiscountDoesNotLetPriceIncreaseOffsetASeparateDiscount()
    {
        var policy = new PriceOverridePolicy(Guid.CreateVersion7(), 2, 2_000, 5_000, true,
            Guid.CreateVersion7(), DateTimeOffset.UtcNow);
        var order = CreateOrder([
            new(Guid.CreateVersion7(), "S01", "优惠服务", 1, null, 100_000, 94_000, "优惠六十元"),
            new(Guid.CreateVersion7(), "S02", "现场增项", 1, null, 10_000, 16_000, "增加服务内容"),
        ]);

        Assert.Equal(order.ReferenceAmountMinor, order.ReceivableMinor);
        Assert.Equal(6_000, order.TotalDiscountMinor);
        Assert.True(policy.ManagerRequiresApproval(order));
    }

    [Fact]
    public void VoidingPendingOverrideCancelsPriceAuthorization()
    {
        var order = CreateOrder([new(Guid.CreateVersion7(), "S01", "标准服务", 1, null,
            10_000, 9_000, "待负责人确认")]);
        order.RequestPriceApproval(Guid.CreateVersion7(), 3);

        order.Void();

        Assert.Equal(ServiceOrderStatus.Voided, order.Status);
        Assert.Equal(PriceAuthorizationState.Cancelled, order.PriceAuthorizationStatus);
    }

    [Fact]
    public void EmptyFacilityDraftCanBeSavedButCannotBeConfirmed()
    {
        var order = CreateOrder([]);

        Assert.Empty(order.Lines);
        Assert.Equal(ServiceOrderStatus.Draft, order.Status);
        Assert.Throws<DomainRuleException>(() => order.Confirm(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void FacilityDraftCanReplaceLinesAndConsultantSnapshot()
    {
        var employeeId = Guid.CreateVersion7();
        var order = CreateOrder([]);

        order.ReplaceDraft(null, "现场补录",
            [new(Guid.CreateVersion7(), "S01", "标准服务", 1, 600, 10_000, 10_000, null)],
            employeeId, "E001", "张顾问");

        Assert.Single(order.Lines);
        Assert.Equal(10_000, order.ReceivableMinor);
        Assert.Equal(employeeId, order.ConsultantEmployeeId);
        Assert.Equal("张顾问", order.ConsultantEmployeeNameSnapshot);
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
    public void ProductLineCanKeepOptionalAddedByEmployeeWithoutCommission()
    {
        var employeeId = Guid.CreateVersion7();
        var order = CreateOrder([ServiceOrderLineDraft.Product(Guid.CreateVersion7(), "P02", "护理用品", "件",
            1, 5_000, 5_000, null, employeeId, "E002", "李店员")]);
        var line = order.Lines.Single();

        Assert.Equal(employeeId, line.ServiceEmployeeId);
        Assert.Equal("E002", line.EmployeeNoSnapshot);
        Assert.Equal("李店员", line.EmployeeNameSnapshot);
        Assert.Equal(CommissionMode.None, line.CommissionModeSnapshot);
        Assert.Equal(0, line.CommissionAmountMinor);
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
