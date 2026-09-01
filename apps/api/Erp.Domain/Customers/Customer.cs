using Erp.Domain.Common;

namespace Erp.Domain.Customers;

public enum CustomerStatus { Active, Disabled, Merged }
public enum CustomerGender { Unknown, Female, Male, Other }

public sealed class Customer : Entity
{
    private Customer() { }

    public Customer(Guid tenantId, Guid homeStoreId, string name, string mobileCiphertext, byte[] mobileLookupHash,
        string mobileLastFour, CustomerGender gender, DateOnly? birthDate, string? sourceCode,
        bool serviceNotificationConsent, bool marketingConsent, DateOnly currentDate, string? residence = null)
        : base(tenantId)
    {
        HomeStoreId = homeStoreId;
        Name = RequireText(name, 100, "顾客姓名");
        MobileCiphertext = RequireText(mobileCiphertext, 2048, "手机号密文");
        MobileLookupHash = mobileLookupHash.Length == 32 ? mobileLookupHash : throw new DomainRuleException("VALIDATION_FAILED", "手机号查询摘要无效");
        MobileLastFour = mobileLastFour.Length == 4 && mobileLastFour.All(char.IsDigit)
            ? mobileLastFour
            : throw new DomainRuleException("VALIDATION_FAILED", "手机号尾号无效");
        if (birthDate > currentDate)
            throw new DomainRuleException("VALIDATION_FAILED", "生日不能晚于今天");
        Gender = gender;
        BirthDate = birthDate;
        Residence = OptionalText(residence, 300);
        SourceCode = OptionalText(sourceCode, 40);
        ServiceNotificationConsent = serviceNotificationConsent;
        MarketingConsent = marketingConsent;
        Status = CustomerStatus.Active;
    }

    public Guid HomeStoreId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string MobileCiphertext { get; private set; } = string.Empty;
    public byte[] MobileLookupHash { get; private set; } = [];
    public string MobileLastFour { get; private set; } = string.Empty;
    public CustomerGender Gender { get; private set; }
    public DateOnly? BirthDate { get; private set; }
    public string? Residence { get; private set; }
    public string? SourceCode { get; private set; }
    public bool ServiceNotificationConsent { get; private set; }
    public bool MarketingConsent { get; private set; }
    public CustomerStatus Status { get; private set; }
    public Guid? MergedIntoCustomerId { get; private set; }
    public DateTimeOffset? MergedAtUtc { get; private set; }
    public Guid? MergedBy { get; private set; }
    public string? MergeReason { get; private set; }

    public void UpdateProfile(string name, string mobileCiphertext, byte[] mobileLookupHash,
        string mobileLastFour, CustomerGender gender, DateOnly? birthDate, string? sourceCode,
        bool serviceNotificationConsent, bool marketingConsent, DateOnly currentDate)
    {
        if (Status == CustomerStatus.Merged)
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "已合并顾客档案不能再修改");
        Name = RequireText(name, 100, "顾客姓名");
        MobileCiphertext = RequireText(mobileCiphertext, 2048, "手机号密文");
        MobileLookupHash = mobileLookupHash.Length == 32 ? mobileLookupHash
            : throw new DomainRuleException("VALIDATION_FAILED", "手机号查询摘要无效");
        MobileLastFour = mobileLastFour.Length == 4 && mobileLastFour.All(char.IsDigit)
            ? mobileLastFour
            : throw new DomainRuleException("VALIDATION_FAILED", "手机号尾号无效");
        if (birthDate > currentDate)
            throw new DomainRuleException("VALIDATION_FAILED", "生日不能晚于今天");
        Gender = gender;
        BirthDate = birthDate;
        SourceCode = OptionalText(sourceCode, 40);
        ServiceNotificationConsent = serviceNotificationConsent;
        MarketingConsent = marketingConsent;
        Touch();
    }

    public void ChangeHomeStore(Guid homeStoreId)
    {
        if (Status == CustomerStatus.Merged)
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "已合并顾客档案不能再修改归属门店");
        if (homeStoreId == Guid.Empty)
            throw new DomainRuleException("VALIDATION_FAILED", "顾客归属门店无效");
        if (HomeStoreId == homeStoreId) return;
        HomeStoreId = homeStoreId;
        Touch();
    }

    public void UpdateResidence(string? residence)
    {
        if (Status == CustomerStatus.Merged)
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "已合并顾客档案不能再修改");
        Residence = OptionalText(residence, 300);
    }

    public void Disable()
    {
        if (Status != CustomerStatus.Active)
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "只有正常顾客档案可以停用");
        Status = CustomerStatus.Disabled;
        Touch();
    }

    public void Restore()
    {
        if (Status != CustomerStatus.Disabled)
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "只有已停用顾客档案可以恢复");
        Status = CustomerStatus.Active;
        Touch();
    }

    public void MergeInto(Guid targetCustomerId, Guid operatorId, string reason, DateTimeOffset mergedAtUtc)
    {
        if (Status == CustomerStatus.Merged)
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "该顾客档案已经合并");
        if (targetCustomerId == Id || targetCustomerId == Guid.Empty || operatorId == Guid.Empty)
            throw new DomainRuleException("VALIDATION_FAILED", "顾客合并目标或操作人无效");
        var normalizedReason = OptionalText(reason, 500);
        if (normalizedReason?.Length < 2)
            throw new DomainRuleException("VALIDATION_FAILED", "合并原因必须为2到500字");
        Status = CustomerStatus.Merged;
        MergedIntoCustomerId = targetCustomerId;
        MergedAtUtc = mergedAtUtc;
        MergedBy = operatorId;
        MergeReason = normalizedReason;
        Touch();
    }

    private static string RequireText(string value, int max, string field)
    {
        var normalized = value.Trim();
        if (normalized.Length is 0 || normalized.Length > max)
            throw new DomainRuleException("VALIDATION_FAILED", $"{field}长度不正确");
        return normalized;
    }

    private static string? OptionalText(string? value, int max)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)) return null;
        if (normalized.Length > max) throw new DomainRuleException("VALIDATION_FAILED", "可选字段长度超限");
        return normalized;
    }
}
