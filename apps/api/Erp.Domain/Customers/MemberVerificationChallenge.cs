using System.Security.Cryptography;
using Erp.Domain.Common;

namespace Erp.Domain.Customers;

public enum MemberVerificationStatus { Active, Verified, Used, Locked, Expired }

public sealed class MemberVerificationChallenge : Entity
{
    private MemberVerificationChallenge() { }

    public MemberVerificationChallenge(Guid tenantId, Guid storeId, Guid customerId, Guid orderId,
        long authorizedAmountMinor, byte[] codeSalt, byte[] codeHash, string mobileLastFour,
        Guid requestedBy, DateTimeOffset expiresAtUtc) : base(tenantId)
    {
        if (authorizedAmountMinor < 50_000 || authorizedAmountMinor > 10_000_000_000)
            throw new DomainRuleException("VALIDATION_FAILED", "验证码授权金额不在允许范围");
        if (codeSalt.Length != 16 || codeHash.Length != 32)
            throw new DomainRuleException("VALIDATION_FAILED", "验证码摘要格式无效");
        if (mobileLastFour.Length != 4 || !mobileLastFour.All(char.IsDigit))
            throw new DomainRuleException("VALIDATION_FAILED", "手机号尾号无效");
        StoreId = storeId;
        CustomerId = customerId;
        OrderId = orderId;
        AuthorizedAmountMinor = authorizedAmountMinor;
        CodeSalt = codeSalt;
        CodeHash = codeHash;
        MobileLastFour = mobileLastFour;
        RequestedBy = requestedBy;
        ExpiresAtUtc = expiresAtUtc;
        AttemptsRemaining = 5;
        Status = MemberVerificationStatus.Active;
    }

    public Guid StoreId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid OrderId { get; private set; }
    public long AuthorizedAmountMinor { get; private set; }
    public byte[] CodeSalt { get; private set; } = [];
    public byte[] CodeHash { get; private set; } = [];
    public string MobileLastFour { get; private set; } = string.Empty;
    public Guid RequestedBy { get; private set; }
    public MemberVerificationStatus Status { get; private set; }
    public int AttemptsRemaining { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? VerifiedAtUtc { get; private set; }
    public DateTimeOffset? UsedAtUtc { get; private set; }

    public bool Verify(byte[] candidateHash, DateTimeOffset now)
    {
        EnsureActive(now);
        if (!CryptographicOperations.FixedTimeEquals(CodeHash, candidateHash))
        {
            AttemptsRemaining--;
            if (AttemptsRemaining == 0) Status = MemberVerificationStatus.Locked;
            Touch();
            return false;
        }

        Status = MemberVerificationStatus.Verified;
        VerifiedAtUtc = now;
        Touch();
        return true;
    }

    public void Consume(Guid orderId, Guid customerId, long amountMinor, DateTimeOffset now)
    {
        if (Status != MemberVerificationStatus.Verified)
            throw new DomainRuleException("MEMBER_VERIFICATION_REQUIRED", "会员验证码尚未通过或已经使用");
        if (now > ExpiresAtUtc)
        {
            Status = MemberVerificationStatus.Expired;
            throw new DomainRuleException("MEMBER_VERIFICATION_EXPIRED", "会员验证码已过期，请重新获取");
        }
        if (OrderId != orderId || CustomerId != customerId || AuthorizedAmountMinor != amountMinor)
            throw new DomainRuleException("MEMBER_VERIFICATION_MISMATCH", "验证码授权的消费单或金额不一致");
        Status = MemberVerificationStatus.Used;
        UsedAtUtc = now;
        Touch();
    }

    public void Supersede()
    {
        if (Status is MemberVerificationStatus.Active or MemberVerificationStatus.Verified)
        {
            Status = MemberVerificationStatus.Expired;
            Touch();
        }
    }

    private void EnsureActive(DateTimeOffset now)
    {
        if (Status != MemberVerificationStatus.Active)
            throw new DomainRuleException("MEMBER_VERIFICATION_NOT_ACTIVE", "验证码不可继续使用");
        if (now > ExpiresAtUtc)
        {
            Status = MemberVerificationStatus.Expired;
            throw new DomainRuleException("MEMBER_VERIFICATION_EXPIRED", "验证码已过期，请重新获取");
        }
    }
}
