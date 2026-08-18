using Erp.Domain.Common;
using Erp.Domain.Platform;

namespace Erp.Domain.Tests.Platform;

public sealed class MerchantRegistrationApplicationTests
{
    [Fact]
    public void PendingApplicationCanBeApprovedOnlyOnce()
    {
        var now = new DateTimeOffset(2026, 8, 19, 0, 0, 0, TimeSpan.Zero);
        var application = Create(now);
        var tenantId = Guid.CreateVersion7();
        var reviewerId = Guid.CreateVersion7();

        application.Approve(tenantId, reviewerId, "资料复核通过", now.AddMinutes(5));

        Assert.Equal(MerchantRegistrationStatus.Approved, application.Status);
        Assert.Equal(tenantId, application.TenantId);
        Assert.Equal((uint)1, application.Version);
        var exception = Assert.Throws<DomainRuleException>(() =>
            application.Reject(reviewerId, "不能重复处理", now.AddMinutes(6)));
        Assert.Equal("REGISTRATION_ALREADY_REVIEWED", exception.Code);
    }

    [Fact]
    public void InvalidOwnerAccountIsRejected()
    {
        var exception = Assert.Throws<DomainRuleException>(() => new MerchantRegistrationApplication(
            "MR202608190001", "测试商户", "测试门店", "测试联系人", "ciphertext", new byte[32], "8000",
            null, null, "有 空格", null, "127.0.0.1", DateTimeOffset.UtcNow));
        Assert.Equal("VALIDATION_FAILED", exception.Code);
    }

    private static MerchantRegistrationApplication Create(DateTimeOffset now) => new(
        "MR202608190001", "测试商户", "测试门店", "测试联系人", "ciphertext", new byte[32], "8000",
        null, null, "merchant.owner", "测试申请", "127.0.0.1", now);
}
