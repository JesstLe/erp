using Erp.Domain.Common;
using Erp.Domain.Customers;

namespace Erp.Domain.Tests.Customers;

public sealed class CustomerMembershipTests
{
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid StoreId = Guid.CreateVersion7();

    [Fact]
    public void CustomerRejectsFutureBirthDate()
    {
        Assert.Throws<DomainRuleException>(() => new Customer(TenantId, StoreId, "测试顾客", "ciphertext",
            new byte[32], "1234", CustomerGender.Unknown, new DateOnly(2026, 8, 19),
            null, false, false, new DateOnly(2026, 8, 18)));
    }

    [Fact]
    public void CardTypeRejectsOutOfRangeValidity()
    {
        Assert.Throws<DomainRuleException>(() => new MemberCardType(TenantId, "VIP", "会员卡", 3651));
    }

    [Fact]
    public void MemberCardRejectsInvalidValidityRange()
    {
        var today = new DateOnly(2026, 8, 18);
        Assert.Throws<DomainRuleException>(() => new MemberCard(TenantId, Guid.CreateVersion7(), Guid.CreateVersion7(),
            StoreId, "M00001", today, today.AddDays(-1), null));
    }

    [Fact]
    public void NewAccountsAreSeparatedAndStartAtZero()
    {
        var customerId = Guid.CreateVersion7();
        var cardId = Guid.CreateVersion7();
        var accounts = Enum.GetValues<MemberAccountType>()
            .Select(type => new MemberAccount(TenantId, customerId, cardId, type)).ToList();

        Assert.Equal(3, accounts.Count);
        Assert.All(accounts, account => Assert.Equal(0, account.BalanceUnits));
        Assert.Equal(3, accounts.Select(account => account.AccountType).Distinct().Count());
    }

    [Fact]
    public void TopupCreditsPrincipalAndBonusAsSeparateImmutableLedgerEntries()
    {
        var customerId = Guid.CreateVersion7();
        var cardId = Guid.CreateVersion7();
        var businessId = Guid.CreateVersion7();
        var commandId = Guid.CreateVersion7();
        var occurredAt = new DateTimeOffset(2026, 8, 18, 8, 0, 0, TimeSpan.Zero);
        var principal = new MemberAccount(TenantId, customerId, cardId, MemberAccountType.Principal);
        var bonus = new MemberAccount(TenantId, customerId, cardId, MemberAccountType.Bonus);

        var principalLedger = principal.Credit("MemberTopup", businessId, 10_000, commandId, occurredAt);
        var bonusLedger = bonus.Credit("MemberTopup", businessId, 2_000, commandId, occurredAt);

        Assert.Equal(10_000, principal.BalanceUnits);
        Assert.Equal(2_000, bonus.BalanceUnits);
        Assert.Equal((0, 10_000), (principalLedger.BalanceBefore, principalLedger.BalanceAfter));
        Assert.Equal((0, 2_000), (bonusLedger.BalanceBefore, bonusLedger.BalanceAfter));
        Assert.Equal(LedgerDirection.Credit, principalLedger.Direction);
        Assert.Equal(commandId, principalLedger.CommandId);
    }

    [Fact]
    public void TopupRequiresPositivePrincipalAndUsesPrincipalAsReceivable()
    {
        var now = new DateTimeOffset(2026, 8, 18, 8, 0, 0, TimeSpan.Zero);
        Assert.Throws<DomainRuleException>(() => new MemberTopupOrder(TenantId, StoreId,
            Guid.CreateVersion7(), Guid.CreateVersion7(), "TU202608180001", 0, 100, null, now));

        var order = new MemberTopupOrder(TenantId, StoreId, Guid.CreateVersion7(), Guid.CreateVersion7(),
            "TU202608180002", 10_000, 2_000, "开卡储值", now);

        Assert.Equal(10_000, order.ReceivableMinor);
        Assert.Equal(2_000, order.BonusMinor);
        Assert.Equal(MemberTopupStatus.Paid, order.Status);
    }

    [Fact]
    public void MemberDebitRejectsInsufficientBalanceAndWritesAReverseDirectionLedger()
    {
        var account = new MemberAccount(TenantId, Guid.CreateVersion7(), Guid.CreateVersion7(),
            MemberAccountType.Principal);
        var topupId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var now = new DateTimeOffset(2026, 8, 18, 8, 0, 0, TimeSpan.Zero);
        account.Credit("MemberTopup", topupId, 20_000, Guid.CreateVersion7(), now);

        var ledger = account.Debit("ServiceOrder", orderId, 8_000, Guid.CreateVersion7(), now);

        Assert.Equal(12_000, account.BalanceUnits);
        Assert.Equal(LedgerDirection.Debit, ledger.Direction);
        Assert.Equal(20_000, ledger.BalanceBefore);
        Assert.Equal(12_000, ledger.BalanceAfter);
        Assert.Throws<DomainRuleException>(() => account.Debit("ServiceOrder", orderId, 12_001,
            Guid.CreateVersion7(), now));
    }

    [Fact]
    public void VerificationChallengeLimitsAttemptsAndCanOnlyAuthorizeItsExactOrderAndAmountOnce()
    {
        var expectedHash = Enumerable.Repeat((byte)7, 32).ToArray();
        var now = new DateTimeOffset(2026, 8, 18, 8, 0, 0, TimeSpan.Zero);
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        var challenge = new MemberVerificationChallenge(TenantId, StoreId, customerId, orderId,
            50_000, new byte[16], expectedHash, "5678", Guid.CreateVersion7(), now.AddMinutes(5));

        Assert.False(challenge.Verify(new byte[32], now));
        Assert.Equal(4, challenge.AttemptsRemaining);
        Assert.True(challenge.Verify(expectedHash, now));
        challenge.Consume(orderId, customerId, 50_000, now);

        Assert.Equal(MemberVerificationStatus.Used, challenge.Status);
        Assert.Throws<DomainRuleException>(() => challenge.Consume(orderId, customerId, 50_000, now));
    }
}
