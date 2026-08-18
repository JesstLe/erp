using System.Text.RegularExpressions;
using Erp.Domain.Common;

namespace Erp.Domain.Platform;

public sealed partial class MerchantRegistrationApplication
{
    private MerchantRegistrationApplication()
    {
    }

    public MerchantRegistrationApplication(string applicationNo, string merchantName, string storeName,
        string contactName, string contactMobileCiphertext, byte[] contactMobileHash, string contactMobileLastFour,
        string? contactEmailCiphertext, byte[]? contactEmailHash, string desiredOwnerAccount, string? note,
        string sourceIp, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        ApplicationNo = Require(applicationNo, 32, nameof(applicationNo));
        MerchantName = Require(merchantName, 100, nameof(merchantName), 2);
        StoreName = Require(storeName, 100, nameof(storeName), 2);
        ContactName = Require(contactName, 60, nameof(contactName), 2);
        ContactMobileCiphertext = Require(contactMobileCiphertext, 2048, nameof(contactMobileCiphertext));
        ContactMobileHash = contactMobileHash.Length == 32 ? contactMobileHash :
            throw new DomainRuleException("VALIDATION_FAILED", "联系手机号摘要无效");
        ContactMobileLastFour = Require(contactMobileLastFour, 4, nameof(contactMobileLastFour));
        ContactEmailCiphertext = EmptyToNull(contactEmailCiphertext, 2048, nameof(contactEmailCiphertext));
        ContactEmailHash = contactEmailHash;
        DesiredOwnerAccount = NormalizeAccount(desiredOwnerAccount);
        NormalizedDesiredOwnerAccount = DesiredOwnerAccount.ToUpperInvariant();
        Note = EmptyToNull(note, 500, nameof(note));
        SourceIp = Require(sourceIp, 64, nameof(sourceIp));
        Status = MerchantRegistrationStatus.PendingReview;
        CreatedAtUtc = UpdatedAtUtc = now;
    }

    public Guid Id { get; private set; }
    public string ApplicationNo { get; private set; } = string.Empty;
    public string MerchantName { get; private set; } = string.Empty;
    public string StoreName { get; private set; } = string.Empty;
    public string ContactName { get; private set; } = string.Empty;
    public string ContactMobileCiphertext { get; private set; } = string.Empty;
    public byte[] ContactMobileHash { get; private set; } = [];
    public string ContactMobileLastFour { get; private set; } = string.Empty;
    public string? ContactEmailCiphertext { get; private set; }
    public byte[]? ContactEmailHash { get; private set; }
    public string DesiredOwnerAccount { get; private set; } = string.Empty;
    public string NormalizedDesiredOwnerAccount { get; private set; } = string.Empty;
    public string? Note { get; private set; }
    public string SourceIp { get; private set; } = string.Empty;
    public MerchantRegistrationStatus Status { get; private set; }
    public Guid? ReviewedByPlatformUserId { get; private set; }
    public DateTimeOffset? ReviewedAtUtc { get; private set; }
    public string? ReviewReason { get; private set; }
    public Guid? TenantId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public uint Version { get; private set; }

    public void Approve(Guid tenantId, Guid reviewerId, string reason, DateTimeOffset now)
    {
        EnsurePending();
        TenantId = tenantId;
        ReviewedByPlatformUserId = reviewerId;
        ReviewedAtUtc = now;
        ReviewReason = Require(reason, 500, nameof(reason), 2);
        Status = MerchantRegistrationStatus.Approved;
        UpdatedAtUtc = now;
        Version++;
    }

    public void Reject(Guid reviewerId, string reason, DateTimeOffset now)
    {
        EnsurePending();
        ReviewedByPlatformUserId = reviewerId;
        ReviewedAtUtc = now;
        ReviewReason = Require(reason, 500, nameof(reason), 2);
        Status = MerchantRegistrationStatus.Rejected;
        UpdatedAtUtc = now;
        Version++;
    }

    private void EnsurePending()
    {
        if (Status != MerchantRegistrationStatus.PendingReview)
            throw new DomainRuleException("REGISTRATION_ALREADY_REVIEWED", "该注册申请已经处理");
    }

    private static string NormalizeAccount(string value)
    {
        var normalized = Require(value, 100, nameof(DesiredOwnerAccount), 4);
        if (!AccountPattern().IsMatch(normalized))
            throw new DomainRuleException("VALIDATION_FAILED", "负责人账号格式不正确");
        return normalized;
    }

    private static string Require(string value, int maxLength, string field, int minLength = 1)
    {
        var normalized = value.Trim();
        if (normalized.Length < minLength || normalized.Length > maxLength)
            throw new DomainRuleException("VALIDATION_FAILED", $"{field}长度不正确");
        return normalized;
    }

    private static string? EmptyToNull(string? value, int maxLength, string field)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)) return null;
        if (normalized.Length > maxLength)
            throw new DomainRuleException("VALIDATION_FAILED", $"{field}长度不正确");
        return normalized;
    }

    [GeneratedRegex("^[A-Za-z0-9._@-]{4,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex AccountPattern();
}

public enum MerchantRegistrationStatus
{
    PendingReview = 1,
    Approved = 2,
    Rejected = 3,
}
