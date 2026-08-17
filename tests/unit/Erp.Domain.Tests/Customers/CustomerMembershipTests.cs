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
}
